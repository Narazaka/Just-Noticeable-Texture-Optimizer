using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.Tiling;

namespace Narazaka.VRChat.Jnto.Editor.Tests.Fixtures
{
    public static class TestGridFactory
    {
        public static UvTileGrid AllCovered(int w, int h, float density = 100f, float boneWeight = 1f)
        {
            var g = UvTileGrid.Create(w, h);
            for (int i = 0; i < g.Tiles.Length; i++)
                g.Tiles[i] = new TileStats { HasCoverage = true, Density = density, BoneWeight = boneWeight };
            return g;
        }

        public static UvTileGrid CenterOnly(int w, int h, float density = 100f, float boneWeight = 1f)
        {
            var g = UvTileGrid.Create(w, h);
            int x0 = g.TilesX / 4, x1 = g.TilesX * 3 / 4;
            int y0 = g.TilesY / 4, y1 = g.TilesY * 3 / 4;
            for (int ty = y0; ty < y1; ty++)
            for (int tx = x0; tx < x1; tx++)
                g.Tiles[ty * g.TilesX + tx] = new TileStats { HasCoverage = true, Density = density, BoneWeight = boneWeight };
            return g;
        }

        public static UvTileGrid Empty(int w, int h)
        {
            // 全 HasCoverage=false (Create のデフォルト)
            return UvTileGrid.Create(w, h);
        }

        public static UvTileGrid SingleTileCovered(int w, int h, int tx, int ty, float density = 100f, float boneWeight = 1f)
        {
            var g = UvTileGrid.Create(w, h);
            g.Tiles[ty * g.TilesX + tx] = new TileStats { HasCoverage = true, Density = density, BoneWeight = boneWeight };
            return g;
        }

        public static float[] FullR(UvTileGrid g)
        {
            var r = new float[g.Tiles.Length];
            for (int i = 0; i < r.Length; i++)
                r[i] = g.Tiles[i].HasCoverage ? g.TileSize : 0f;
            return r;
        }

        public static float[] ZeroR(UvTileGrid g) => new float[g.Tiles.Length];
    }
}
