using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ANYWAYS.Tools;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using NetTopologySuite.IO.VectorTiles;
using NetTopologySuite.IO.VectorTiles.Mapbox;
using OsmSharp;
using OsmSharp.Complete;
using OsmSharp.Db;
using OsmSharp.Streams;

namespace ANYWAYS.VectorTiles.CycleNetworks
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly WorkerConfiguration _configuration;
        private readonly Downloader _downloader;

        private const string Local = "data.osm.pbf";

        public Worker(WorkerConfiguration configuration, Downloader downloader, ILogger<Worker> logger)
        {
            _logger = logger;
            _configuration = configuration;
            _downloader = downloader;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Worker running at: {time}, triggered every {refreshTime}", 
                    DateTimeOffset.Now, _configuration.RefreshTime);

                await this.RunAsync(stoppingToken);
                
                await Task.Delay(_configuration.RefreshTime, stoppingToken);
            }
        }

        private async Task RunAsync(CancellationToken stoppingToken)
        {
            // download file (if md5 files don't match).
            var local = Path.Combine(_configuration.DataPath, Local);
            if (!await _downloader.Get(_configuration.SourceUrl, local))
            {
                return;
            }

            if (!File.Exists(local))
            {
                _logger.LogCritical($"Local file not found: {local}.");
                return;
            }

            try
            {
                var localStream = File.OpenRead(local);
                var pbfSource = new PBFOsmStreamSource(localStream);
                var source = new OsmSharp.Streams.Filters.OsmStreamFilterProgress();
                source.RegisterSource(pbfSource);

                // pass over source stream and:
                // - determine all members of route relations where are interested in.
                // - keep master relations relationships.
                var members = new HashSet<OsmGeoKey>();
                var masterRelations = new Dictionary<OsmGeoKey, List<Relation>>();
                var foundRouteRelations = 0;
                while (true)
                {
                    if (!source.MoveNext(true, true, false)) break;
                    if (!(source.Current() is Relation current)) continue;
                    if (current.Members == null) continue;

                    if (current.Tags == null) continue;

                    if (current.Tags.Contains("type", "network") &&
                        current.Tags.Contains("network", "rcn"))
                    {
                        // this is a network master relation.
                        foreach (var member in current.Members)
                        {
                            var memberKey = new OsmGeoKey
                            {
                                Id = member.Id,
                                Type = member.Type
                            };
                            if (!masterRelations.TryGetValue(memberKey, out var masters))
                            {
                                masters = new List<Relation>(1);
                                masterRelations[memberKey] = masters;
                            }

                            masters.Add(current);
                        }
                    }
                    else if (current.Tags.Contains("type", "route") &&
                             current.Tags.Contains("route", "bicycle"))
                    {
                        // this is an actual route.
                    }
                    else
                    {
                        // nothing found that can be used.
                        continue;
                    }

                    // make sure to keep all members.
                    foundRouteRelations++;
                    foreach (var member in current.Members)
                    {
                        var memberKey = new OsmGeoKey
                        {
                            Id = member.Id,
                            Type = member.Type
                        };
                        members.Add(memberKey);
                    }
                }

                _logger.LogInformation($"Found {foundRouteRelations} with {members.Count} members.");

                // filter stream, keeping only the relevant objects.
                var filteredSource = source.Where(x =>
                {
                    if (!x.Id.HasValue) return false;

                    var key = new OsmGeoKey
                    {
                        Id = x.Id.Value,
                        Type = x.Type
                    };

                    switch (x.Type)
                    {
                        case OsmGeoType.Node:
                            return true;
                        case OsmGeoType.Way:
                            return members.Contains(key);
                        case OsmGeoType.Relation:
                            return (x.Tags != null &&
                                    x.Tags.Contains("type", "route") &&
                                    x.Tags.Contains("route", "bicycle"));
                    }

                    return false;
                });

                // convert this to complete objects.
                var features = new FeatureCollection();
                foreach (var osmComplete in filteredSource.ToComplete())
                {
                    if (osmComplete is Node node)
                    {
                        if (!node.Id.HasValue) continue;
                        var key = new OsmGeoKey
                        {
                            Id = node.Id.Value,
                            Type = node.Type
                        };

                        if (members.Contains(key))
                        {
                            var nodeFeatures = node.ToFeatureCollection();
                            foreach (var feature in nodeFeatures)
                            {
                                features.Add(feature);
                            }
                        }
                    }

                    if (osmComplete is CompleteRelation relation)
                    {
                        if (osmComplete.Tags.TryGetValue("ref", out var refValue))
                        {
                            if (!string.IsNullOrWhiteSpace(refValue))
                            {
                                osmComplete.Tags.AddOrReplace("ref_length", refValue.Length.ToString());
                            }
                        }

                        var relationFeatures = relation.ToFeatureCollection();
                        foreach (var feature in relationFeatures)
                        {
                            features.Add(feature);
                        }
                    }
                }
#if DEBUG
                await using var outputStream = File.Open("debug.geojson", FileMode.Create);
                await using var streamWriter = new StreamWriter(outputStream);
                
                var serializer = GeoJsonSerializer.Create();
                serializer.Serialize(streamWriter, features);
#endif

                // build the vector tile tree.
                var tree = new VectorTileTree {{features, ConfigureFeature}};

                IEnumerable<(IFeature feature, int zoom, string layerName)> ConfigureFeature(IFeature feature)
                {
                    for (var z = 0; z <= 14; z++)
                    {
                        if (feature.Geometry is LineString)
                        {
                            yield return (feature, z, "cyclenetwork");
                        }
                        else
                        {
                            yield return (feature, z, "cyclenodes");
                        }
                    }
                }

                // write the tiles to disk as mvt (but stop when cancelled is requested).
                tree.Select(id => tree[id]).Where(x => !stoppingToken.IsCancellationRequested)
                    .Write(_configuration.TargetPath);

                // write the mvt.
                var mvtFile = Path.Combine(_configuration.TargetPath, "mvt.json");
                if (File.Exists(mvtFile)) File.Delete(mvtFile);
                File.Copy("vectortile.spec.json", mvtFile);

                _logger.LogInformation($"Tiles written.");
            }
            catch (Exception e)
            {
                _logger.LogCritical(e, $"Unhandled exception when writing tiles.");
            }
        }

        private void Cancel()
        {
            // delete md5 file, causing a refresh.
            var localMd5 = _configuration.DataPath + Local + ".md5";
            if (File.Exists(localMd5)) File.Delete(localMd5);
        }
    }
}