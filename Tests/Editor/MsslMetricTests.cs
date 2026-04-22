using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.Gate;
using Narazaka.VRChat.Jnto.Editor.Phase2.GpuPipeline;
using Narazaka.VRChat.Jnto.Editor.Phase2.Tiling;

public class MsslMetricTests
{
    [Test]
    public void Identical_Gpu_ReturnsNearZero()
    {
        var t = MakePattern(256);
        var grid = UvTileGrid.Create(256, 256);
        MarkAllCovered(grid);
        var r = FullR(grid);
        var calib = DegradationCalibration.Default();

        using (var ctxA = GpuTextureContext.FromTexture2D(t))
        {
            var metric = new MsslMetric();
            var scores = new float[grid.Tiles.Length];
            metric.Evaluate(ctxA.Original, ctxA.Original, grid, r, calib, scores);

            foreach (var s in scores)
                Assert.Less(s, 0.05f, "identical textures should score near-zero");
        }

        Object.DestroyImmediate(t);
        Object.DestroyImmediate(calib);
    }

    [Test]
    public void Blurred_Gpu_ReturnsHigh()
    {
        var a = MakePattern(256);
        var b = Blurred(a);
        var grid = UvTileGrid.Create(256, 256);
        MarkAllCovered(grid);
        var r = FullR(grid);
        var calib = DegradationCalibration.Default();

        using (var ctxA = GpuTextureContext.FromTexture2D(a))
        using (var ctxB = GpuTextureContext.FromTexture2D(b))
        {
            var metric = new MsslMetric();
            var scores = new float[grid.Tiles.Length];
            metric.Evaluate(ctxA.Original, ctxB.Original, grid, r, calib, scores);

            float max = 0f;
            foreach (var s in scores) if (s > max) max = s;
            Assert.Greater(max, 0.3f, "heavy blur should produce high MSSL on GPU");
        }

        Object.DestroyImmediate(a);
        Object.DestroyImmediate(b);
        Object.DestroyImmediate(calib);
    }

    [Test]
    public void NoCoverageTiles_StayZero()
    {
        var a = MakePattern(256);
        var b = Blurred(a);
        var grid = UvTileGrid.Create(256, 256);
        // Mark only the first tile (everything else r=0 stays zero)
        grid.Tiles[0] = new TileStats { HasCoverage = true, Density = 100f, BoneWeight = 1f };
        var r = new float[grid.Tiles.Length];
        r[0] = grid.TileSize;
        var calib = DegradationCalibration.Default();

        using (var ctxA = GpuTextureContext.FromTexture2D(a))
        using (var ctxB = GpuTextureContext.FromTexture2D(b))
        {
            var metric = new MsslMetric();
            var scores = new float[grid.Tiles.Length];
            metric.Evaluate(ctxA.Original, ctxB.Original, grid, r, calib, scores);

            for (int i = 1; i < scores.Length; i++)
                Assert.AreEqual(0f, scores[i], 0.001f);
        }

        Object.DestroyImmediate(a);
        Object.DestroyImmediate(b);
        Object.DestroyImmediate(calib);
    }

    static void MarkAllCovered(UvTileGrid g)
    {
        for (int i = 0; i < g.Tiles.Length; i++)
            g.Tiles[i] = new TileStats { HasCoverage = true, Density = 100f, BoneWeight = 1f };
    }

    static float[] FullR(UvTileGrid g)
    {
        var r = new float[g.Tiles.Length];
        for (int i = 0; i < r.Length; i++) r[i] = g.TileSize;
        return r;
    }

    static Texture2D MakePattern(int n)
    {
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            float v = (((x / 4) + (y / 4)) & 1) == 0 ? 0.1f : 0.9f;
            px[y * n + x] = new Color(v, v, v, 1f);
        }
        t.SetPixels(px); t.Apply();
        return t;
    }

    static Texture2D Blurred(Texture2D src)
    {
        int w = src.width, h = src.height;
        var p = src.GetPixels();
        for (int pass = 0; pass < 4; pass++)
        {
            var q = new Color[p.Length];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                Color s = Color.black; int n = 0;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int xx = x + dx, yy = y + dy;
                    if (xx < 0 || yy < 0 || xx >= w || yy >= h) continue;
                    s += p[yy * w + xx]; n++;
                }
                q[y * w + x] = s / n;
            }
            p = q;
        }
        var dst = new Texture2D(w, h, TextureFormat.RGBA32, false);
        dst.SetPixels(p); dst.Apply();
        return dst;
    }
}
