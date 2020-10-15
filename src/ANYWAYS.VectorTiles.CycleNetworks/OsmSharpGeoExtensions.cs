using System;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using OsmSharp.Complete;
using OsmSharp.Tags;

namespace ANYWAYS.VectorTiles.CycleNetworks
{
    /// <summary>
    /// A set of extension methods we believe somehow should existing in OsmSharp.Geo.
    /// </summary>
    internal static class OsmSharpGeoExtensions
    {
        /// <summary>
        /// Returns a linestring exactly representing the way.
        /// </summary>
        /// <param name="way">The way.</param>
        /// <returns>A linestring.</returns>
        public static LineString ToLineString(this CompleteWay way)
        {
            if (way == null) throw new ArgumentNullException(nameof(way));
            if (way.Nodes == null) throw new ArgumentException($"{nameof(way)} has no nodes.");
            if (way.Nodes.Length < 2) throw new ArgumentException($"{nameof(way)} has not enough nodes.");
            
            var coordinates = new Coordinate[way.Nodes.Length];
            for (var n = 0; n < way.Nodes.Length; n++)
            {
                var node = way.Nodes[n];
                
                if (node.Longitude == null || node.Latitude == null) continue;
                
                coordinates[n] = new Coordinate(node.Longitude.Value, node.Latitude.Value);
            }
            
            return new LineString(coordinates);
        }

        public static AttributesTable ToAttributesTable(this TagsCollectionBase tags)
        {
            var table = new AttributesTable();
            foreach (var t in tags)
            {
                table[t.Key] = t.Value;
            }

            return table;
        }
    }
}