using System.Collections.Generic;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using OsmSharp;
using OsmSharp.Complete;
using OsmSharp.Tags;

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
            
            foreach (var member in routeRelation.Members)
            {
                if (member.Member is not CompleteWay way) continue;
                if (way.Nodes == null || way.Nodes.Length < 2) continue;
                    
                var lineString = way.ToLineString();
                
                var attributes = new AttributesTable();
                if (way?.Tags != null)
                {
                    foreach (var t in way.Tags)
                    {
                        if (attributes.Exists(t.Key))
                        {
                            attributes[t.Key] = t.Value;
                        }
                        else
                        {
                            attributes.Add(t.Key, t.Value);
                        }
                    }
                }

                if (routeRelation?.Tags != null)
                {
                    foreach (var t in routeRelation.Tags)
                    {
                        if (attributes.Exists(t.Key))
                        {
                            attributes[t.Key] = t.Value;
                        }
                        else
                        {
                            attributes.Add(t.Key, t.Value);
                        }
                    }
                }
                    
                features.Add(new Feature(lineString, attributes));
            }
            
            return features;
        }

        /// <summary>
        /// Converts a single node that represents relevant network information to one or more features.
        /// </summary>
        /// <param name="node">A node.</param>
        /// <param name="extraTags">Merge in extra tags.</param>
        /// <returns>A feature collection representing the node info.</returns>
        public static FeatureCollection ToFeatureCollection(this Node node, IEnumerable<Tag>? extraTags = null)
        {
            var features = new FeatureCollection();
            if (!node.Latitude.HasValue || !node.Longitude.HasValue) return features;

            var attributes = new AttributesTable();
            if (extraTags != null)
            {
                foreach (var t in extraTags)
                {
                    if (attributes.Exists(t.Key))
                    {
                        attributes[t.Key] = t.Value;
                    }
                    else
                    {
                        attributes.Add(t.Key, t.Value);
                    }
                }
            }

            if (node.Tags != null)
            {
                foreach (var t in node.Tags)
                {
                    if (attributes.Exists(t.Key))
                    {
                        attributes[t.Key] = t.Value;
                    }
                    else
                    {
                        attributes.Add(t.Key, t.Value);
                    }
                }
            }
            
            features.Add(new Feature(new Point(new Coordinate(node.Longitude.Value, node.Latitude.Value)), attributes));

            return features;
        }
    }
}