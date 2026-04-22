using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto;
using Narazaka.VRChat.Jnto.Editor.Phase2.Tiling;

public class EffectiveResolutionCalculatorTests
{
    [Test]
    public void NoCoverage_ReturnsZero()
    {
        var tile = new TileStats { HasCoverage = false };
        var r = EffectiveResolutionCalculator.ComputeR(tile, 64, 30f, 20f, QualityPreset.Medium);
        Assert.AreEqual(0f, r);
    }

    [Test]
    public void ZeroDensity_ReturnsZero()
    {
        var tile = new TileStats { HasCoverage = true, Density = 0f, BoneWeight = 1f };
        var r = EffectiveResolutionCalculator.ComputeR(tile, 64, 30f, 20f, QualityPreset.Medium);
        Assert.AreEqual(0f, r);
    }

    [Test]
    public void HighDensity_ClampsTo_RMin()
    {
        var tile = new TileStats { HasCoverage = true, Density = 1e9f, BoneWeight = 1f };
        var r = EffectiveResolutionCalculator.ComputeR(tile, 64, 30f, 20f, QualityPreset.Medium);
        Assert.AreEqual(EffectiveResolutionCalculator.RMin, r, 0.001f,
            "extreme density should clamp to rMin (visible texels far exceed available resolution)");
    }

    [Test]
    public void LowDensity_ClampsTo_TileSize()
    {
        var tile = new TileStats { HasCoverage = true, Density = 1e-6f, BoneWeight = 1f };
        var r = EffectiveResolutionCalculator.ComputeR(tile, 64, 30f, 20f, QualityPreset.Medium);
        Assert.AreEqual(64f, r, 0.001f, "very low density should clamp to tileSize (all texels visible)");
    }

    [Test]
    public void HigherPreset_IncreasesR()
    {
        // Density を大きめにして r が tileSize に clamp されない領域で比較する
        var tile = new TileStats { HasCoverage = true, Density = 10000f, BoneWeight = 1f };
        var rMed = EffectiveResolutionCalculator.ComputeR(tile, 64, 30f, 20f, QualityPreset.Medium);
        var rHigh = EffectiveResolutionCalculator.ComputeR(tile, 64, 30f, 20f, QualityPreset.High);
        Assert.Greater(rHigh, rMed);
    }

    [Test]
    public void HigherBoneWeight_IncreasesR()
    {
        var low = new TileStats { HasCoverage = true, Density = 10000f, BoneWeight = 0.3f };
        var high = new TileStats { HasCoverage = true, Density = 10000f, BoneWeight = 1.0f };
        var rLow = EffectiveResolutionCalculator.ComputeR(low, 64, 30f, 20f, QualityPreset.Medium);
        var rHigh = EffectiveResolutionCalculator.ComputeR(high, 64, 30f, 20f, QualityPreset.Medium);
        Assert.Greater(rHigh, rLow);
    }

    [Test]
    public void FurtherViewDistance_DecreasesR()
    {
        var tile = new TileStats { HasCoverage = true, Density = 10000f, BoneWeight = 1f };
        var rNear = EffectiveResolutionCalculator.ComputeR(tile, 64, 30f, 20f, QualityPreset.Medium);
        var rFar = EffectiveResolutionCalculator.ComputeR(tile, 64, 100f, 20f, QualityPreset.Medium);
        Assert.Greater(rNear, rFar);
    }

    [Test]
    public void LevelFromR_Monotonic()
    {
        Assert.AreEqual(0, EffectiveResolutionCalculator.LevelFromR(64f, 64));
        Assert.AreEqual(1, EffectiveResolutionCalculator.LevelFromR(32f, 64));
        Assert.AreEqual(2, EffectiveResolutionCalculator.LevelFromR(16f, 64));
        Assert.AreEqual(6, EffectiveResolutionCalculator.LevelFromR(1f, 64));
    }

    [Test]
    public void LevelFromR_ZeroR_ReturnsZero()
    {
        Assert.AreEqual(0, EffectiveResolutionCalculator.LevelFromR(0f, 64));
    }

    [Test]
    public void Oversampling_Monotonic()
    {
        Assert.Less(EffectiveResolutionCalculator.Oversampling(QualityPreset.Low),
                    EffectiveResolutionCalculator.Oversampling(QualityPreset.Medium));
        Assert.Less(EffectiveResolutionCalculator.Oversampling(QualityPreset.Medium),
                    EffectiveResolutionCalculator.Oversampling(QualityPreset.High));
        Assert.Less(EffectiveResolutionCalculator.Oversampling(QualityPreset.High),
                    EffectiveResolutionCalculator.Oversampling(QualityPreset.Ultra));
    }
}
