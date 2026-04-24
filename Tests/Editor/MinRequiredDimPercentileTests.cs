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

/// <summary>
/// Tier 2 検出: minRequiredDim の算出が r98 percentile で、1% の高密度タイル (顔/文字等) を
/// 取りこぼす疑い。Finer size 候補が 1% タイルで fail するはずなのに、それより先に
/// 小さい候補が r98 を満たしているだけで pass 判定されていないかを確認。
/// </summary>
public class MinRequiredDimPercentileTests
{
    [Test]
    public void OnePercentHighDensity_NotSilentlyDiscarded()
    {
        // 128 × 128 の tile grid。1 タイルだけ高密度にして高 r を要求する。
        // minRequiredDim が r98 で 1% を除外すると、高密度タイルが gate 評価に到達しない。
        var tex = TestTextureFactory.MakeSolid(128, 128, Color.gray);
        var grid = UvTileGrid.Create(128, 128);

        // 1 tile: high-density; rest: low-density
        for (int i = 0; i < grid.Tiles.Length; i++)
            grid.Tiles[i] = new TileStats { HasCoverage = true, Density = 1f, BoneWeight = 1f };
        grid.Tiles[0] = new TileStats { HasCoverage = true, Density = 10000f, BoneWeight = 1f };

        // rPerTile: 1 tile demands near-full (=tileSize), others demand near-minimum
        var r = new float[grid.Tiles.Length];
        for (int i = 0; i < r.Length; i++) r[i] = 4f;            // "barely visible"
        r[0] = grid.TileSize;                                     // "needs full res"

        var calib = DegradationCalibration.Default();
        try
        {
            using (var ctx = GpuTextureContext.FromTexture2D(tex))
            {
                var pipeline = new Phase2Pipeline(calib, ShaderUsage.Color, alphaUsed: false);
                var result = pipeline.Find(tex, ctx, grid, r, new ResolvedSettings { Preset = QualityPreset.Medium });

                // If r98 is used and the high-density tile (1/4 = 2.5% of 16 tiles) slips
                // below the percentile, the pipeline will accept a very small size.
                // For 128x128 input (16 tiles, 1 high-density), r98 = the second-highest r
                // which is 4 — so minRequiredDim would be ~4, letting small candidates pass.
                // Assert at least that the adopted size is larger than the base minimum
                // (DensityCalculator.MinSize = 32 typically).
                UnityEngine.Debug.Log($"[JNTO/MinReqDim] result size = {result.Size}, final fmt = {result.Format}");
                Assert.GreaterOrEqual(result.Size, 64,
                    "a single high-r tile demanding full res must not be silently skipped by r98");
                if (result.Final != tex) Object.DestroyImmediate(result.Final);
            }
        }
        finally
        {
            Object.DestroyImmediate(calib);
            Object.DestroyImmediate(tex);
        }
    }
}
