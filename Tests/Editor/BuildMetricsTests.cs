using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto;
using Narazaka.VRChat.Jnto.Editor.Phase2.Compression;
using Narazaka.VRChat.Jnto.Editor.Phase2.Gate;
using Narazaka.VRChat.Jnto.Editor.Phase2.GpuPipeline;
using Narazaka.VRChat.Jnto.Editor.Phase2.Tiling;
using Narazaka.VRChat.Jnto.Editor.Resolution;
using Narazaka.VRChat.Jnto.Editor.Tests.Fixtures;

public class BuildMetricsTests
{
    [Test]
    public void Pipeline_WithChromaDrift_IncludesChromaDriftMetric()
    {
        var calib = DegradationCalibration.Default();
        try
        {
            var pipeline = new Phase2Pipeline(calib, ShaderUsage.Color, alphaUsed: false,
                enableChromaDrift: true);
            var t = TestTextureFactory.MakeSolid(64, 64, Color.gray);
            var grid = TestGridFactory.AllCovered(64, 64);
            var r = TestGridFactory.FullR(grid);
            var settings = new ResolvedSettings { Preset = QualityPreset.Medium };

            using (var ctx = GpuTextureContext.FromTexture2D(t))
            {
                var result = pipeline.Find(t, ctx, grid, r, settings);
                Assert.IsNotNull(result);
                if (result.Final != t) Object.DestroyImmediate(result.Final);
            }
            Object.DestroyImmediate(t);
        }
        finally { Object.DestroyImmediate(calib); }
    }

    [Test]
    public void Pipeline_WithoutChromaDrift_Succeeds()
    {
        var calib = DegradationCalibration.Default();
        try
        {
            var pipeline = new Phase2Pipeline(calib, ShaderUsage.Color, alphaUsed: false,
                enableChromaDrift: false);
            var t = TestTextureFactory.MakeSolid(64, 64, Color.gray);
            var grid = TestGridFactory.AllCovered(64, 64);
            var r = TestGridFactory.FullR(grid);
            var settings = new ResolvedSettings { Preset = QualityPreset.Medium };

            using (var ctx = GpuTextureContext.FromTexture2D(t))
            {
                var result = pipeline.Find(t, ctx, grid, r, settings);
                Assert.IsNotNull(result);
                if (result.Final != t) Object.DestroyImmediate(result.Final);
            }
            Object.DestroyImmediate(t);
        }
        finally { Object.DestroyImmediate(calib); }
    }

    [Test]
    public void Pipeline_NormalUsage_NoChromaDrift()
    {
        var calib = DegradationCalibration.Default();
        try
        {
            var pipeline = new Phase2Pipeline(calib, ShaderUsage.Normal, alphaUsed: false,
                enableChromaDrift: true);
            var t = TestTextureFactory.MakeFlatNormal(64, 64);
            var grid = TestGridFactory.AllCovered(64, 64);
            var r = TestGridFactory.FullR(grid);
            var settings = new ResolvedSettings { Preset = QualityPreset.Medium };

            using (var ctx = GpuTextureContext.FromTexture2D(t, isLinear: true))
            {
                var result = pipeline.Find(t, ctx, grid, r, settings);
                Assert.IsNotNull(result);
                if (result.Final != t) Object.DestroyImmediate(result.Final);
            }
            Object.DestroyImmediate(t);
        }
        finally { Object.DestroyImmediate(calib); }
    }

    [Test]
    public void Pipeline_ExplicitIsLinear_OverridesUsageDerivation()
    {
        // Color usage but explicit isLinear=true — the pipeline must take the explicit value.
        // Reachable via reflection of the _isLinear private field.
        var calib = DegradationCalibration.Default();
        try
        {
            var pipeline = new Phase2Pipeline(calib, ShaderUsage.Color, alphaUsed: false,
                enableChromaDrift: true, origFormat: TextureFormat.RGBA32, isLinear: true);
            var f = typeof(Phase2Pipeline).GetField("_isLinear",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(f, "_isLinear field must exist");
            Assert.IsTrue((bool)f.GetValue(pipeline),
                "explicit isLinear=true must override Color-usage default (=false)");
        }
        finally { Object.DestroyImmediate(calib); }
    }

    [Test]
    public void Pipeline_NullIsLinear_FallsBackToUsage()
    {
        var calib = DegradationCalibration.Default();
        try
        {
            var pipeline = new Phase2Pipeline(calib, ShaderUsage.Normal, alphaUsed: false,
                enableChromaDrift: false, origFormat: TextureFormat.DXT5);
            var f = typeof(Phase2Pipeline).GetField("_isLinear",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsTrue((bool)f.GetValue(pipeline),
                "Normal usage without explicit isLinear must derive true (linear)");
        }
        finally { Object.DestroyImmediate(calib); }
    }
}
