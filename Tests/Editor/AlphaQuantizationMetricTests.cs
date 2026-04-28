using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.Gate;
using Narazaka.VRChat.Jnto.Editor.Phase2.GpuPipeline;
using Narazaka.VRChat.Jnto.Editor.Phase2.Tiling;

[Category("GPU")]
public class AlphaQuantizationMetricTests
{
    [Test]
    public void SmoothAlpha_VsQuantized_DetectsLoss()
    {
        var smooth = MakeAlphaGradient(128, quantize: 0);
        var quant = MakeAlphaGradient(128, quantize: 4);  // 4 levels: 強い量子化

        var grid = UvTileGrid.Create(128, 128);
        MarkAllCovered(grid);
        var r = FullR(grid);
        var calib = DegradationCalibration.Default();

        using (var ctxA = GpuTextureContext.FromTexture2D(smooth))
        using (var ctxB = GpuTextureContext.FromTexture2D(quant))
        {
            var metric = new AlphaQuantizationMetric();
            var scores = new float[grid.Tiles.Length];
            metric.Evaluate(ctxA.Original, ctxB.Original, grid, r, calib, scores);

            float maxScore = 0f;
            foreach (var s in scores) if (s > maxScore) maxScore = s;
            Assert.Greater(maxScore, 0.1f, "alpha quantization should be detected");
        }

        Object.DestroyImmediate(smooth);
        Object.DestroyImmediate(quant);
        Object.DestroyImmediate(calib);
    }

    [Test]
    public void Identical_NearZero()
    {
        var t = MakeAlphaGradient(128, quantize: 0);
        var grid = UvTileGrid.Create(128, 128);
        MarkAllCovered(grid);
        var r = FullR(grid);
        var calib = DegradationCalibration.Default();

        using (var ctx = GpuTextureContext.FromTexture2D(t))
        {
            var metric = new AlphaQuantizationMetric();
            var scores = new float[grid.Tiles.Length];
            metric.Evaluate(ctx.Original, ctx.Original, grid, r, calib, scores);

            foreach (var s in scores) Assert.Less(s, 0.05f);
        }

        Object.DestroyImmediate(t);
        Object.DestroyImmediate(calib);
    }

    [Test]
    public void OpaqueAlphaConstant_Skipped()
    {
        // α が全部 1.0 → 範囲 < 0.02 → score 0
        var smooth = MakeSolidAlpha(128, 1f);
        var quant = MakeSolidAlpha(128, 1f);
        var grid = UvTileGrid.Create(128, 128);
        MarkAllCovered(grid);
        var r = FullR(grid);
        var calib = DegradationCalibration.Default();

        using (var ctxA = GpuTextureContext.FromTexture2D(smooth))
        using (var ctxB = GpuTextureContext.FromTexture2D(quant))
        {
            var metric = new AlphaQuantizationMetric();
            var scores = new float[grid.Tiles.Length];
            metric.Evaluate(ctxA.Original, ctxB.Original, grid, r, calib, scores);
            foreach (var s in scores) Assert.AreEqual(0f, s, 0.001f);
        }

        Object.DestroyImmediate(smooth);
        Object.DestroyImmediate(quant);
        Object.DestroyImmediate(calib);
    }

    [Test]
    public void NoCoverage_Zero()
    {
        var smooth = MakeAlphaGradient(128, quantize: 0);
        var quant = MakeAlphaGradient(128, quantize: 4);
        var grid = UvTileGrid.Create(128, 128);
        var r = new float[grid.Tiles.Length];
        var calib = DegradationCalibration.Default();

        using (var ctxA = GpuTextureContext.FromTexture2D(smooth))
        using (var ctxB = GpuTextureContext.FromTexture2D(quant))
        {
            var metric = new AlphaQuantizationMetric();
            var scores = new float[grid.Tiles.Length];
            metric.Evaluate(ctxA.Original, ctxB.Original, grid, r, calib, scores);
            foreach (var s in scores) Assert.AreEqual(0f, s, 0.001f);
        }

        Object.DestroyImmediate(smooth);
        Object.DestroyImmediate(quant);
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

    static Texture2D MakeAlphaGradient(int n, int quantize)
    {
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            float a = x / (float)(n - 1);
            if (quantize > 0)
            {
                a = Mathf.Round(a * (quantize - 1)) / (quantize - 1);
            }
            px[y * n + x] = new Color(0.5f, 0.5f, 0.5f, a);
        }
        t.SetPixels(px);
        t.Apply();
        return t;
    }

    static Texture2D MakeSolidAlpha(int n, float a)
    {
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
        var px = new Color[n * n];
        for (int i = 0; i < px.Length; i++) px[i] = new Color(0.5f, 0.5f, 0.5f, a);
        t.SetPixels(px);
        t.Apply();
        return t;
    }
}
