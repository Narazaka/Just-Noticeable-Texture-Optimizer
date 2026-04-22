using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.Gate;
using Narazaka.VRChat.Jnto.Editor.Phase2.GpuPipeline;
using Narazaka.VRChat.Jnto.Editor.Phase2.Tiling;

public class BlockBoundaryMetricTests
{
    [Test]
    public void GridStepPattern_DetectsBoundary()
    {
        // orig: smooth gradient (境界なし) / cand: 4-px step pattern (BC ブロック境界を模す)
        // 「圧縮で追加で生まれた block 境界」= orig vs cand 差分でスコア化される前提。
        var orig = MakeGradient(128);
        var cand = MakeGridSteps(128, period: 4);
        var grid = UvTileGrid.Create(128, 128);
        MarkAllCovered(grid);
        var r = FullR(grid);
        var calib = DegradationCalibration.Default();

        using (var ctxO = GpuTextureContext.FromTexture2D(orig))
        using (var ctxC = GpuTextureContext.FromTexture2D(cand))
        {
            var metric = new BlockBoundaryMetric();
            var scores = new float[grid.Tiles.Length];
            metric.Evaluate(ctxO.Original, ctxC.Original, grid, r, calib, scores);

            float maxScore = 0f;
            foreach (var s in scores) if (s > maxScore) maxScore = s;
            Assert.Greater(maxScore, 0.1f, "4-px step pattern should produce block boundary score");
        }

        Object.DestroyImmediate(orig);
        Object.DestroyImmediate(cand);
        Object.DestroyImmediate(calib);
    }

    [Test]
    public void SmoothImage_LowScore()
    {
        var t = MakeGradient(128);
        var grid = UvTileGrid.Create(128, 128);
        MarkAllCovered(grid);
        var r = FullR(grid);
        var calib = DegradationCalibration.Default();

        using (var ctx = GpuTextureContext.FromTexture2D(t))
        {
            var metric = new BlockBoundaryMetric();
            var scores = new float[grid.Tiles.Length];
            metric.Evaluate(ctx.Original, ctx.Original, grid, r, calib, scores);

            float maxScore = 0f;
            foreach (var s in scores) if (s > maxScore) maxScore = s;
            Assert.Less(maxScore, 0.1f, "smooth gradient should not produce block boundary score");
        }

        Object.DestroyImmediate(t);
        Object.DestroyImmediate(calib);
    }

    [Test]
    public void NoCoverage_Zero()
    {
        var orig = MakeGradient(128);
        var cand = MakeGridSteps(128, period: 4);
        var grid = UvTileGrid.Create(128, 128);
        var r = new float[grid.Tiles.Length];
        var calib = DegradationCalibration.Default();

        using (var ctxO = GpuTextureContext.FromTexture2D(orig))
        using (var ctxC = GpuTextureContext.FromTexture2D(cand))
        {
            var metric = new BlockBoundaryMetric();
            var scores = new float[grid.Tiles.Length];
            metric.Evaluate(ctxO.Original, ctxC.Original, grid, r, calib, scores);
            foreach (var s in scores) Assert.AreEqual(0f, s, 0.001f);
        }

        Object.DestroyImmediate(orig);
        Object.DestroyImmediate(cand);
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

    static Texture2D MakeGridSteps(int n, int period)
    {
        // x が period 倍数の境界で値が大きく変わるパターン
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            int blk = x / period;
            float v = (blk & 1) == 0 ? 0.2f : 0.8f;
            px[y * n + x] = new Color(v, v, v, 1f);
        }
        t.SetPixels(px); t.Apply();
        return t;
    }

    static Texture2D MakeGradient(int n)
    {
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            float v = x / (float)(n - 1);
            px[y * n + x] = new Color(v, v, v, 1f);
        }
        t.SetPixels(px); t.Apply();
        return t;
    }
}
