using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto;
using Narazaka.VRChat.Jnto.Editor.Phase2;
using Narazaka.VRChat.Jnto.Editor.Phase2.Compression;
using Narazaka.VRChat.Jnto.Editor.Phase2.Gate;
using Narazaka.VRChat.Jnto.Editor.Phase2.GpuPipeline;
using Narazaka.VRChat.Jnto.Editor.Phase2.Tiling;
using Narazaka.VRChat.Jnto.Editor.Resolution;
using Narazaka.VRChat.Jnto.Editor.Tests.Fixtures;

public class Phase2PipelineTests
{
    [Test]
    public void Find_WithCheckerboard_ReturnsValidResult()
    {
        var t = MakeCheckerboard(256);
        try
        {
            var grid = UvTileGrid.Create(256, 256);
            for (int i = 0; i < grid.Tiles.Length; i++)
                grid.Tiles[i] = new TileStats { HasCoverage = true, Density = 100f, BoneWeight = 1f };
            var r = new float[grid.Tiles.Length];
            for (int i = 0; i < r.Length; i++) r[i] = grid.TileSize;

            var calib = DegradationCalibration.Default();
            var settings = new ResolvedSettings
            {
                Preset = QualityPreset.Medium,
                EncodePolicy = EncodePolicy.Safe,
                CacheMode = CacheMode.Full,
            };

            using (var ctx = GpuTextureContext.FromTexture2D(t))
            {
                var pipeline = new Phase2Pipeline(calib, ShaderUsage.Color, alphaUsed: false);
                var result = pipeline.Find(t, ctx, grid, r, settings);

                Assert.IsNotNull(result);
                Assert.IsNotNull(result.Final);
                Assert.GreaterOrEqual(result.Size, DensityCalculator.MinSize);
                Assert.LessOrEqual(result.Size, 256);
                Assert.IsTrue(result.ProcessingMs > 0f);
                Assert.IsNotNull(result.DecisionReason);

                if (result.Final != t) Object.DestroyImmediate(result.Final);
            }
            Object.DestroyImmediate(calib);
        }
        finally
        {
            Object.DestroyImmediate(t);
        }
    }

    [Test]
    public void Find_WithEmptyTileCoverage_ReturnsOriginal()
    {
        var t = TestTextureFactory.MakeCheckerboard(256, 256);
        try
        {
            var grid = TestGridFactory.Empty(256, 256);
            var r = TestGridFactory.ZeroR(grid);

            var calib = DegradationCalibration.Default();
            var settings = new ResolvedSettings
            {
                Preset = QualityPreset.Medium,
                EncodePolicy = EncodePolicy.Safe,
                CacheMode = CacheMode.Full,
            };

            using (var ctx = GpuTextureContext.FromTexture2D(t))
            {
                var pipeline = new Phase2Pipeline(calib, ShaderUsage.Color, alphaUsed: false);
                var result = pipeline.Find(t, ctx, grid, r, settings);

                Assert.IsNotNull(result);
                // 評価不能 → 縮小しない (orig 維持)
                Assert.AreEqual(256, result.Size,
                    "no tile coverage → must keep original size, never shrink");
                if (result.Final != t) Object.DestroyImmediate(result.Final);
            }
            Object.DestroyImmediate(calib);
        }
        finally { Object.DestroyImmediate(t); }
    }

    static Texture2D MakeCheckerboard(int n)
    {
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
            px[y * n + x] = (((x >> 1) + (y >> 1)) & 1) == 0 ? Color.black : Color.white;
        t.SetPixels(px); t.Apply();
        return t;
    }
}
