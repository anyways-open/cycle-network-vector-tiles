using System.Collections.Generic;
using System.Linq;
using NetTopologySuite.IO.VectorTiles;

namespace ANYWAYS.VectorTiles.CycleNetworks
{
    public static class VectorTileTreeExtensions
    {
        public static IEnumerable<VectorTile> Tiles(this VectorTileTree tree)
        {
            return tree.Select(id => tree[id]);
        }
    }
}