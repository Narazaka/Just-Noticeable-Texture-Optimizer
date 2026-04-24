using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.Gate;
using Narazaka.VRChat.Jnto.Editor.Phase2.GpuPipeline;
using Narazaka.VRChat.Jnto.Editor.Phase2.Tiling;

public class ChromaDriftGateMetricTests
{
    [Test]
    public void DifferentHue_ProducesScore()
    {
        var orig = MakeSolid(128, new Color(0.8f, 0.2f, 0.2f, 1f));
        var shifted = MakeSolid(128, new Color(0.2f, 0.2f, 0.8f, 1f));
        var grid = UvTileGrid.Create(128, 128);
        MarkAllCovered(grid);
        var r = FullR(grid);
        var calib = DegradationCalibration.Default();

        using (var ctxO = GpuTextureContext.FromTexture2D(orig))
        using (var ctxC = GpuTextureContext.FromTexture2D(shifted))
        {
            var metric = new ChromaDriftMetric();
            var scores = new float[grid.Tiles.Length];
            metric.Evaluate(ctxO.Original, ctxC.Original, grid, r, calib, scores);

            float maxScore = 0f;
            foreach (var s in scores) if (s > maxScore) maxScore = s;
            Assert.Greater(maxScore, 0.5f,
                "large hue shift (red to blue) should produce significant chroma drift score");
        }

        Object.DestroyImmediate(orig);
        Object.DestroyImmediate(shifted);
        Object.DestroyImmediate(calib);
    }

    [Test]
    public void Identical_NearZero()
    {
        var t = MakeSolid(128, new Color(0.5f, 0.3f, 0.7f, 1f));
        var grid = UvTileGrid.Create(128, 128);
        MarkAllCovered(grid);
        var r = FullR(grid);
        var calib = DegradationCalibration.Default();

        using (var ctx = GpuTextureContext.FromTexture2D(t))
        {
            var metric = new ChromaDriftMetric();
            var scores = new float[grid.Tiles.Length];
            metric.Evaluate(ctx.Original, ctx.Original, grid, r, calib, scores);

            foreach (var s in scores)
                Assert.Less(s, 0.05f, "identical textures should have near-zero chroma drift");
        }

        Object.DestroyImmediate(t);
        Object.DestroyImmediate(calib);
    }

    [Test]
    public void NoCoverage_Zero()
    {
        var orig = MakeSolid(128, Color.red);
        var shifted = MakeSolid(128, Color.blue);
        var grid = UvTileGrid.Create(128, 128);
        var r = new float[grid.Tiles.Length];
        var calib = DegradationCalibration.Default();

        using (var ctxO = GpuTextureContext.FromTexture2D(orig))
        using (var ctxC = GpuTextureContext.FromTexture2D(shifted))
        {
            var metric = new ChromaDriftMetric();
            var scores = new float[grid.Tiles.Length];
            metric.Evaluate(ctxO.Original, ctxC.Original, grid, r, calib, scores);
            foreach (var s in scores) Assert.AreEqual(0f, s, 0.001f);
        }

        Object.DestroyImmediate(orig);
        Object.DestroyImmediate(shifted);
        Object.DestroyImmediate(calib);
    }

    [Test]
    public void SubtleShift_StaysBelowJndUpperBound()
    {
        // History: this test originally asserted Assert.Greater(maxScore, 0f) under the
        // buggy ChromaDrift path (ΔE76 + double-linearized samples), which inflated tiny
        // ΔE values. After Tasks 3+4 fixed both bugs, a 0.003 RGB shift can collapse to
        // maxScore=0 because RGBA32 byte quantization (Round(0.503*255) ≈ Round(0.5*255))
        // erases the difference before it ever reaches the metric.
        //
        // The remaining invariant — "subtle shift must not produce a huge score" — is still
        // meaningful and is what we assert here.
        var orig = MakeSolid(128, new Color(0.5f, 0.5f, 0.5f, 1f));
        var shifted = MakeSolid(128, new Color(0.503f, 0.498f, 0.500f, 1f));
        var grid = UvTileGrid.Create(128, 128);
        MarkAllCovered(grid);
        var r = FullR(grid);
        var calib = DegradationCalibration.Default();

        using (var ctxO = GpuTextureContext.FromTexture2D(orig))
        using (var ctxC = GpuTextureContext.FromTexture2D(shifted))
        {
            var metric = new ChromaDriftMetric();
            var scores = new float[grid.Tiles.Length];
            metric.Evaluate(ctxO.Original, ctxC.Original, grid, r, calib, scores);

            float maxScore = 0f;
            foreach (var s in scores) if (s > maxScore) maxScore = s;
            Assert.Less(maxScore, 1.0f, "subtle shift must not produce a large score (sanity bound)");
        }

        Object.DestroyImmediate(orig);
        Object.DestroyImmediate(shifted);
        Object.DestroyImmediate(calib);
    }

    [Test]
    public void MidGray_NoDoubleLinearization_StaysBelowJnd()
    {
        // Bug B1: toLinear() was applied to already-linearized RT samples. For middle-gray (0.5)
        // this inflated "delta" even for identical textures. After fix, score must stay below
        // a tight JND bound (< 0.15).
        var t = MakeSolid(128, new Color(0.5f, 0.5f, 0.5f, 1f));
        var grid = UvTileGrid.Create(128, 128);
        MarkAllCovered(grid);
        var r = FullR(grid);
        var calib = DegradationCalibration.Default();

        using (var ctx = GpuTextureContext.FromTexture2D(t))
        {
            var metric = new ChromaDriftMetric();
            var scores = new float[grid.Tiles.Length];
            metric.Evaluate(ctx.Original, ctx.Original, grid, r, calib, scores);

            float maxScore = 0f;
            foreach (var s in scores) if (s > maxScore) maxScore = s;
            Assert.Less(maxScore, 0.15f,
                "identical mid-gray textures must produce near-zero ΔE (not inflated by double-linearization)");
        }

        Object.DestroyImmediate(t);
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

    static Texture2D MakeSolid(int n, Color c)
    {
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
        var px = new Color[n * n];
        for (int i = 0; i < px.Length; i++) px[i] = c;
        t.SetPixels(px); t.Apply();
        return t;
    }
}
