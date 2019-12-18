using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        static void Main(string[] args)
        {       
            #if DEBUG
            args = new[]
            {
                "/data/work/data/OSM/antwerpen.osm.pbf", "cyclenetworks"
            };
            #endif

            var inputFile = args[0];
            var outputFolder = args[1];
            
            var logFile = Path.Combine("logs", "log-{Date}.txt");
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .Enrich.FromLogContext()
                .WriteTo.RollingFile(new JsonFormatter(), logFile)
                .WriteTo.LiterateConsole()
                .CreateLogger();
            
            // optionally link OsmSharp and Itinero logging.
            // TODO: improve this by logging to the correct levels.
            OsmSharp.Logging.Logger.LogAction = (o, level, message, parameters) =>
            {
                Log.Information($"[{o}] {level} - {message}");
            };
            
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
                            Type =  member.Type
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
                { // nothing found that can be used.
                    continue;
                }
                
                // make sure to keep all members.
                foundRouteRelations++;
                foreach (var member in current.Members)
                {
                    var memberKey = new OsmGeoKey
                    {
                        Id = member.Id,
                        Type =  member.Type
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
                    var relationFeatures = relation.ToFeatureCollection();
                    foreach (var feature in relationFeatures.Features)
                    {
                        features.Add(feature);
                    }
                }
            }
#if DEBUG
            using var outputStream = File.Open("debug.geojson", FileMode.Create);
            using var streamWriter = new StreamWriter(outputStream);
            
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
        }
    }
}