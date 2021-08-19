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
            // hookup OsmSharp logging.
            OsmSharp.Logging.Logger.LogAction = (origin, level, message, parameters) =>
            {
                var formattedMessage = $"{origin} - {message}";
                switch (level)
                {
                    case "critical":
                        _logger.LogCritical(formattedMessage);
                        break;
                    case "error":
                        _logger.LogError(formattedMessage);
                        break;
                    case "warning":
                        _logger.LogWarning(formattedMessage);
                        break;
                    case "verbose":
                        _logger.LogTrace(formattedMessage);
                        break;
                    case "information":
                        _logger.LogInformation(formattedMessage);
                        break;
                    default:
                        _logger.LogDebug(formattedMessage);
                        break;
                }
            };

            _logger.LogInformation("Worker running at: {time}, triggered every {refreshTime}",
                DateTimeOffset.Now, _configuration.RefreshTime);

            while (!stoppingToken.IsCancellationRequested)
            {
                await this.RunAsync(stoppingToken);

                await Task.Delay(_configuration.RefreshTime, stoppingToken);
            }
        }

        private async Task RunAsync(CancellationToken stoppingToken)
        {
            // download file (if md5 files don't match).
            var local = Path.Combine(_configuration.DataPath, Local);
            if (!await _downloader.Get(_configuration.SourceUrl, local, "temp", cancellationToken: stoppingToken))
            {
                return;
            }

            _logger.LogInformation($"Downloaded new file, refreshing tiles.");

            if (!File.Exists(local))
            {
                _logger.LogCritical($"Local file not found: {local}.");
                return;
            }

            try
            {
                bool IsRelevant(Relation current)
                {
                    var networks = new HashSet<string> { "lcn", "rcn", "ncn" };
                    if (!current.Tags.TryGetValue("type", out var type)) return false;

                    switch (type)
                    {
                        case "network":
                        {
                            if (current.Tags.TryGetValue("network", out var network) &&
                                networks.Contains(network))
                            {
                                return true;
                            }

                            break;
                        }
                        case "route":
                        {
                            if (current.Tags.Contains("route", "bicycle"))
                            {
                                return true;
                            }

                            break;
                        }
                    }

                    return false;
                }

                var localStream = File.OpenRead(local);
                var pbfSource = new PBFOsmStreamSource(localStream);
                var source = new OsmSharp.Streams.Filters.OsmStreamFilterProgress();
                source.RegisterSource(pbfSource);

                // pass over source stream and:
                // - determine all members of route relations where are interested in.
                // - keep master relations relationships.
                var membersInRelations = new Dictionary<OsmGeoKey, List<Relation>>();
                var foundRouteRelations = 0;
                while (true)
                {
                    if (stoppingToken.IsCancellationRequested) break;
                    if (!source.MoveNext(true, true, false)) break;
                    if (source.Current() is not Relation current) continue;
                    if (current.Members == null) continue;
                    if (current.Tags == null) continue;
                    if (!IsRelevant(current)) continue;

                    // this is a network relation.
                    foundRouteRelations++;
                    foreach (var member in current.Members)
                    {
                        var memberKey = new OsmGeoKey
                        {
                            Id = member.Id,
                            Type = member.Type
                        };
                        if (!membersInRelations.TryGetValue(memberKey, out var masters))
                        {
                            masters = new List<Relation>(1);
                            membersInRelations[memberKey] = masters;
                        }

                        masters.Add(current);
                    }
                }

                if (stoppingToken.IsCancellationRequested)
                {
                    this.CancelledWhenProcessing();
                    return;
                }

                _logger.LogInformation($"Found {foundRouteRelations} with {membersInRelations.Count} members.");

                // filter stream, keeping only the relevant objects.
                IEnumerable<OsmGeo> FilterSource()
                {
                    foreach (var x in source)
                    {
                        if (stoppingToken.IsCancellationRequested) yield break;
                        if (!x.Id.HasValue) yield break;

                        var key = new OsmGeoKey
                        {
                            Id = x.Id.Value,
                            Type = x.Type
                        };

                        switch (x.Type)
                        {
                            case OsmGeoType.Node:
                                yield return x;
                                break;
                            case OsmGeoType.Way:
                                if (membersInRelations.ContainsKey(key))
                                {
                                    yield return x;
                                }

                                break;
                            case OsmGeoType.Relation:
                                if (x is Relation r && IsRelevant(r))
                                {
                                    yield return x;
                                }

                                break;
                        }
                    }
                }

                var filteredSource = FilterSource();
                if (stoppingToken.IsCancellationRequested)
                {
                    this.CancelledWhenProcessing();
                    return;
                }

                // convert this to complete objects.
                var features = new FeatureCollection();
                foreach (var osmComplete in filteredSource.ToComplete())
                {
                    if (stoppingToken.IsCancellationRequested) break;
                    switch (osmComplete)
                    {
                        case Node { Id: null }:
                            continue;
                        case Node node:
                        {
                            var key = new OsmGeoKey
                            {
                                Id = node.Id.Value,
                                Type = node.Type
                            };

                            if (membersInRelations.TryGetValue(key, out var masters))
                            {
                                foreach (var master in masters)
                                {
                                    var nodeFeatures = node.ToFeatureCollection(master.Tags);
                                    foreach (var feature in nodeFeatures)
                                    {
                                        features.Add(feature);
                                    }
                                }
                            }

                            break;
                        }
                        case CompleteRelation relation:
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

                            break;
                        }
                    }
                }

                if (stoppingToken.IsCancellationRequested)
                {
                    this.CancelledWhenProcessing();
                    return;
                }

#if DEBUG
                await using var outputStream = File.Open("debug.geojson", FileMode.Create);
                await using var streamWriter = new StreamWriter(outputStream);

                var serializer = GeoJsonSerializer.Create();
                serializer.Serialize(streamWriter, features);
#endif

                // build the vector tile tree.
                var tree = new VectorTileTree { { features, ConfigureFeature } };

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
                IEnumerable<VectorTile> GetTiles(IEnumerable<ulong> tiles)
                {
                    foreach (var tileId in tiles)
                    {
                        if (stoppingToken.IsCancellationRequested)
                        {
                            this.CancelledWhenProcessing();
                            yield break;
                        }

                        var tile = new NetTopologySuite.IO.VectorTiles.Tiles.Tile(tileId);
                        _logger.LogInformation($"Writing {_configuration.TargetPath}{tile.Zoom}/{tile.X}/{tile.Y}.mvt");
                        yield return tree[tileId];
                    }
                }

                GetTiles(tree).Write(_configuration.TargetPath);
                if (stoppingToken.IsCancellationRequested)
                {
                    this.CancelledWhenProcessing();
                    return;
                }

                // write the mvt.
                var mvtFile = Path.Combine(_configuration.TargetPath, "mvt.json");
                if (File.Exists(mvtFile)) File.Delete(mvtFile);
                File.Copy("vectortile.spec.json", mvtFile);

                _logger.LogInformation($"Tiles written.");
            }
            catch (Exception e)
            {
                // we cannot be sure things went well, best to try again.
                this.ForceRetry();

                _logger.LogCritical(e, $"Unhandled exception when writing tiles.");
            }
        }

        private void ForceRetry()
        {
            // delete md5 file, causing a retry.
            var localMd5 = _configuration.DataPath + Local + ".md5";
            if (File.Exists(localMd5)) File.Delete(localMd5);
        }

        private void CancelledWhenProcessing()
        {
            this.ForceRetry();

            _logger.LogWarning($"Processing was cancelled!");
        }
    }
}