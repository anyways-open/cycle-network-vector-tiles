using NetTopologySuite.Features;
using OsmSharp.Complete;
using OsmSharp.Geo;

namespace ANYWAYS.VectorTiles.CycleNetworks
{
    internal static class RouteRelationHandler
    {
        /// <summary>
        /// Converts a single route relation to one or more features.
        /// </summary>
        /// <param name="routeRelation">The route relation.</param>
        /// <returns>A feature collection representing the route relation.</returns>
        public static FeatureCollection ToFeatureCollection(this CompleteRelation routeRelation)
        {
            var features = new FeatureCollection();
            if (routeRelation.Members == null) return features;
            
            var attributes = routeRelation.Tags.ToAttributeTable();
            foreach (var member in routeRelation.Members)
            {
                if (!(member.Member is CompleteWay way)) continue;
                if (way.Nodes == null || way.Nodes.Length < 2) continue;
                    
                var lineString = way.ToLineString();
                    
                features.Add(new Feature(lineString, attributes));
            }
            
            return features;
        }
    }
}