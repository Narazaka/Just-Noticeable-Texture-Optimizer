using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.Gate;
using Narazaka.VRChat.Jnto.Editor.Phase2.Tiling;

public class MsslMetricCpuTests
{
    [Test]
    public void Identical_ReturnsNearZero()
    {
        var t = MakeCheckerboard(128);
        var grid = UvTileGrid.Create(128, 128);
        MarkAllCovered(grid);
        var r = FullR(grid);
        var calib = DegradationCalibration.Default();

        var metric = new MsslMetricCpu();
        var scores = metric.EvaluateDebug(t, t, grid, r, calib);

        foreach (var s in scores)
            Assert.Less(s, 0.01f);

        Object.DestroyImmediate(t);
        Object.DestroyImmediate(calib);
    }

    [Test]
    public void HeavyBlur_ProducesHighScore()
    {
        var a = MakeCheckerboard(128);
        var b = HeavyBlur(a);
        var grid = UvTileGrid.Create(128, 128);
        MarkAllCovered(grid);
        var r = FullR(grid);
        var calib = DegradationCalibration.Default();

        var metric = new MsslMetricCpu();
        var scores = metric.EvaluateDebug(a, b, grid, r, calib);

        float maxScore = 0f;
        foreach (var s in scores) if (s > maxScore) maxScore = s;
        Assert.Greater(maxScore, 0.5f, "heavy blur should produce high MSSL");

        Object.DestroyImmediate(a);
        Object.DestroyImmediate(b);
        Object.DestroyImmediate(calib);
    }

    [Test]
    public void NoCoverageTiles_StayZero()
    {
        var a = MakeCheckerboard(128);
        var b = HeavyBlur(a);
        var grid = UvTileGrid.Create(128, 128);
        // Mark only the first tile, leave others as no-coverage
        grid.Tiles[0] = new TileStats { HasCoverage = true, Density = 100f, BoneWeight = 1f };
        var r = new float[grid.Tiles.Length];
        r[0] = grid.TileSize;
        var calib = DegradationCalibration.Default();

        var metric = new MsslMetricCpu();
        var scores = metric.EvaluateDebug(a, b, grid, r, calib);

        for (int i = 1; i < scores.Length; i++)
            Assert.AreEqual(0f, scores[i], "no-coverage tiles must remain zero");

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

    static Texture2D MakeCheckerboard(int n)
    {
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
            px[y * n + x] = (((x >> 1) + (y >> 1)) & 1) == 0 ? Color.black : Color.white;
        t.SetPixels(px);
        t.Apply();
        return t;
    }

    static Texture2D HeavyBlur(Texture2D src)
    {
        int w = src.width, h = src.height;
        var p = src.GetPixels();
        for (int pass = 0; pass < 5; pass++)
        {
            var q = new Color[p.Length];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                Color sum = Color.black;
                int n = 0;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int xx = x + dx, yy = y + dy;
                    if (xx < 0 || yy < 0 || xx >= w || yy >= h) continue;
                    sum += p[yy * w + xx];
                    n++;
                }
                q[y * w + x] = sum / n;
            }
            p = q;
        }
        var dst = new Texture2D(w, h, TextureFormat.RGBA32, false);
        dst.SetPixels(p);
        dst.Apply();
        return dst;
    }
}
