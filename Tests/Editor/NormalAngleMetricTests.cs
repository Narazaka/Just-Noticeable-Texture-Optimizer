using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.Gate;
using Narazaka.VRChat.Jnto.Editor.Phase2.GpuPipeline;
using Narazaka.VRChat.Jnto.Editor.Phase2.Tiling;

public class NormalAngleMetricTests
{
    [Test]
    public void DifferentNormals_DetectsAngleDiff()
    {
        // a: flat (0,0,1) | b: tilted (0.5, 0.5, ...)
        var a = MakeFlatNormal(128);
        var b = MakeTiltedNormal(128, tx: 0.5f, ty: 0.5f);

        var grid = UvTileGrid.Create(128, 128);
        MarkAllCovered(grid);
        var r = FullR(grid);
        var calib = DegradationCalibration.Default();

        using (var ctxA = GpuTextureContext.FromTexture2D(a))
        using (var ctxB = GpuTextureContext.FromTexture2D(b))
        {
            var metric = new NormalAngleMetric();
            var scores = new float[grid.Tiles.Length];
            metric.Evaluate(ctxA.Original, ctxB.Original, grid, r, calib, scores);

            float maxScore = 0f;
            foreach (var s in scores) if (s > maxScore) maxScore = s;
            Assert.Greater(maxScore, 0.5f, "tilted normal vs flat should produce score");
        }

        Object.DestroyImmediate(a);
        Object.DestroyImmediate(b);
        Object.DestroyImmediate(calib);
    }

    [Test]
    public void Identical_NearZero()
    {
        var t = MakeFlatNormal(128);
        var grid = UvTileGrid.Create(128, 128);
        MarkAllCovered(grid);
        var r = FullR(grid);
        var calib = DegradationCalibration.Default();

        using (var ctx = GpuTextureContext.FromTexture2D(t))
        {
            var metric = new NormalAngleMetric();
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
        var a = MakeFlatNormal(128);
        var b = MakeTiltedNormal(128, 0.5f, 0.5f);
        var grid = UvTileGrid.Create(128, 128);
        var r = new float[grid.Tiles.Length];
        var calib = DegradationCalibration.Default();

        using (var ctxA = GpuTextureContext.FromTexture2D(a))
        using (var ctxB = GpuTextureContext.FromTexture2D(b))
        {
            var metric = new NormalAngleMetric();
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

    static Texture2D MakeFlatNormal(int n)
    {
        // (0,0,1) → encode: r=0.5, g=0.5, b=1.0 (b は使われない)
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
        var px = new Color[n * n];
        for (int i = 0; i < px.Length; i++) px[i] = new Color(0.5f, 0.5f, 1f, 1f);
        t.SetPixels(px);
        t.Apply();
        return t;
    }

    static Texture2D MakeTiltedNormal(int n, float tx, float ty)
    {
        // 任意の (tx, ty) で encode
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
        var px = new Color[n * n];
        for (int i = 0; i < px.Length; i++)
            px[i] = new Color(tx * 0.5f + 0.5f, ty * 0.5f + 0.5f, 1f, 1f);
        t.SetPixels(px);
        t.Apply();
        return t;
    }
}
