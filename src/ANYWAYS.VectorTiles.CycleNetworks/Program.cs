using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NetTopologySuite.Features;
using NetTopologySuite.IO;
using OsmSharp;
using OsmSharp.Complete;
using OsmSharp.Db;
using OsmSharp.Streams;
using Serilog;
using Serilog.Formatting.Json;

namespace ANYWAYS.VectorTiles.CycleNetworks
{
    class Program
    {
        static void Main(string[] args)
        {       
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
            
            var pbfSource = new OsmSharp.Streams.PBFOsmStreamSource(File.OpenRead(args[0]));
            var source = new OsmSharp.Streams.Filters.OsmStreamFilterProgress();
            source.RegisterSource(pbfSource);
            
            // pass over source stream and determine all members of route relations where are interested in.
            var members = new HashSet<OsmGeoKey>();
            var foundRouteRelations = 0;
            while (true)
            {
                if (!source.MoveNext(true, true, false)) break;
                if (!(source.Current() is Relation current)) continue;
                if (current.Members == null) continue;

                if (current.Tags == null || !current.Tags.Contains("type", "route") ||
                    !current.Tags.Contains("route", "bicycle")) continue;

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
                if (osmComplete.Type != OsmGeoType.Relation) continue;
                if (!(osmComplete is CompleteRelation relation)) continue;

                var relationFeatures = relation.ToFeatureCollection();
                foreach (var feature in relationFeatures.Features)
                {
                    features.Add(feature);   
                }
            }

            using var outputStream = File.Open(args[1], FileMode.Create);
            using var streamWriter = new StreamWriter(outputStream);
            
            var serializer = GeoJsonSerializer.Create();
            serializer.Serialize(streamWriter, features);
        }
    }
}