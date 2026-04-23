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

public class NoCoverageKeepsOriginalTests
{
    [Test]
    public void ZeroCoverage_ReturnsPassTrueWithOriginal()
    {
        var t = TestTextureFactory.MakeSolid(64, 64, Color.gray);
        var grid = UvTileGrid.Create(64, 64);
        // do NOT mark any tile as covered — all zero
        var r = new float[grid.Tiles.Length]; // all zeros

        var calib = DegradationCalibration.Default();
        try
        {
            using (var ctx = GpuTextureContext.FromTexture2D(t))
            {
                var pipeline = new NewPhase2Pipeline(calib, ShaderUsage.Color, alphaUsed: false);
                var result = pipeline.Find(t, ctx, grid, r, new ResolvedSettings { Preset = QualityPreset.Medium });

                Assert.IsTrue(result.FinalVerdict.Pass,
                    "zero-coverage must return Pass=true (keep original as no-op)");
                Assert.AreSame(t, result.Final, "Final must be the original texture");
                Assert.AreEqual(0f, result.FinalVerdict.TextureScore, 0.001f);
                StringAssert.Contains("kept original", result.DecisionReason);
            }
        }
        finally
        {
            Object.DestroyImmediate(calib);
            Object.DestroyImmediate(t);
        }
    }
}
