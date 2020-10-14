using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using NetTopologySuite.IO.VectorTiles;
using NetTopologySuite.IO.VectorTiles.Mapbox;
using OsmSharp;
using OsmSharp.Complete;
using OsmSharp.Db;
using OsmSharp.Streams;
using OsmSharp.Tags;
using Serilog;
using Serilog.Formatting.Json;

namespace ANYWAYS.VectorTiles.CycleNetworks
{
    class Program
    {
        
        static async Task Main(string[] args)
        {
            var config = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", true, true)
                .Build();

            EnableLogging(config);

            var webSource = config["webSource"];

            var planetFile = config["source"];

            var target = config["target"];
            var hashFile = Path.Combine(target,
                "planet.cycle-vector-tiles.hash");

            await Process(webSource, planetFile, target, hashFile);
        }

        private static async Task Process(string webSource, string planetFile, string target,
            string hashFile)
        {
            var hashFileInfo = new FileInfo(hashFile);
            if (hashFileInfo.Directory == null ||
                !hashFileInfo.Directory.Exists)
            {
                Log.Fatal($"Output path doesn't exist: {target}.");
                return;
            }
            
            var lockFile = new FileInfo(Path.Combine(target, "cycle-vector-tiles.lock"));
            if (LockHelper.IsLocked(lockFile.FullName))
            {
                return;
            }

            await Download.Get(webSource, planetFile);

            if (!File.Exists(planetFile))
            {
                Log.Fatal($"Planet file not found: {planetFile}.");
                return;
            }

            try
            {
                LockHelper.WriteLock(lockFile.FullName);
                
                var inputFile = planetFile;
                var outputFolder = target;

                var pbfSource = new OsmSharp.Streams.PBFOsmStreamSource(File.OpenRead(inputFile));
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

                Log.Information($"Found {foundRouteRelations} with {members.Count} members.");

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
                            foreach (var feature in nodeFeatures.Features)
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
                        foreach (var feature in relationFeatures.Features)
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

                // write the tiles to disk as mvt.
                tree.Write(outputFolder);

                var mvtFile = Path.Combine(outputFolder, "mvt.json");
                if (File.Exists(mvtFile)) File.Delete(mvtFile);
                File.Copy("vectortile.spec.json", mvtFile);

                Log.Information($"Tiles written.");
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
            finally
            {
                File.Delete(lockFile.FullName);
            }
        }

        private static void EnableLogging(IConfigurationRoot config)
        {
            // enable logging.
            OsmSharp.Logging.Logger.LogAction = (origin, level, message, parameters) =>
            {
                var formattedMessage = $"{origin} - {message}";
                switch (level)
                {
                    case "critical":
                        Log.Fatal(formattedMessage);
                        break;
                    case "error":
                        Log.Error(formattedMessage);
                        break;
                    case "warning":
                        Log.Warning(formattedMessage);
                        break;
                    case "verbose":
                        Log.Verbose(formattedMessage);
                        break;
                    case "information":
                        Log.Information(formattedMessage);
                        break;
                    default:
                        Log.Debug(formattedMessage);
                        break;
                }
            };

            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(config)
                .CreateLogger();
        }
    }
}