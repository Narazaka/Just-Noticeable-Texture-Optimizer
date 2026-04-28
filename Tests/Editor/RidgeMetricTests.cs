using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.Gate;
using Narazaka.VRChat.Jnto.Editor.Phase2.GpuPipeline;
using Narazaka.VRChat.Jnto.Editor.Phase2.Tiling;

[Category("GPU")]
public class RidgeMetricTests
{
    [Test]
    public void Stripes_Blurred_RidgeLoss()
    {
        var a = MakeStripes(128, 4);
        var b = Blurred(a, 3);
        var grid = UvTileGrid.Create(128, 128);
        MarkAllCovered(grid);
        var r = FullR(grid);
        var calib = DegradationCalibration.Default();

        using (var ctxA = GpuTextureContext.FromTexture2D(a))
        using (var ctxB = GpuTextureContext.FromTexture2D(b))
        {
            var metric = new RidgeMetric();
            var scores = new float[grid.Tiles.Length];
            metric.Evaluate(ctxA.Original, ctxB.Original, grid, r, calib, scores);

            float max = 0f;
            foreach (var s in scores) if (s > max) max = s;
            Assert.Greater(max, 0.3f, "blurred stripes should trigger ridge loss");
        }

        Object.DestroyImmediate(a);
        Object.DestroyImmediate(b);
        Object.DestroyImmediate(calib);
    }

    [Test]
    public void Identical_NearZero()
    {
        var t = MakeStripes(128, 4);
        var grid = UvTileGrid.Create(128, 128);
        MarkAllCovered(grid);
        var r = FullR(grid);
        var calib = DegradationCalibration.Default();

        using (var ctx = GpuTextureContext.FromTexture2D(t))
        {
            var metric = new RidgeMetric();
            var scores = new float[grid.Tiles.Length];
            metric.Evaluate(ctx.Original, ctx.Original, grid, r, calib, scores);

            foreach (var s in scores) Assert.Less(s, 0.05f);
        }

        Object.DestroyImmediate(t);
        Object.DestroyImmediate(calib);
    }

    [Test]
    public void NoCoverage_Zero()
    {
        var a = MakeStripes(128, 4);
        var b = Blurred(a, 3);
        var grid = UvTileGrid.Create(128, 128);
        var r = new float[grid.Tiles.Length];
        var calib = DegradationCalibration.Default();

        using (var ctxA = GpuTextureContext.FromTexture2D(a))
        using (var ctxB = GpuTextureContext.FromTexture2D(b))
        {
            var metric = new RidgeMetric();
            var scores = new float[grid.Tiles.Length];
            metric.Evaluate(ctxA.Original, ctxB.Original, grid, r, calib, scores);
            foreach (var s in scores) Assert.AreEqual(0f, s, 0.001f);
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

    static Texture2D MakeStripes(int n, int period)
    {
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
            px[y * n + x] = (x / period) % 2 == 0 ? Color.black : Color.white;
        t.SetPixels(px);
        t.Apply();
        return t;
    }

    static Texture2D Blurred(Texture2D src, int passes)
    {
        int w = src.width, h = src.height;
        var p = src.GetPixels();
        for (int pass = 0; pass < passes; pass++)
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
        dst.SetPixels(p);
        dst.Apply();
        return dst;
    }
}
