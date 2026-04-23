using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto;
using Narazaka.VRChat.Jnto.Editor.Phase2.Tiling;

public class EffectiveResolutionCalculatorTests
{
    const int TexW = 4096;
    const int TexH = 4096;

    [Test]
    public void NoCoverage_ReturnsZero()
    {
        var tile = new TileStats { HasCoverage = false };
        var r = EffectiveResolutionCalculator.ComputeR(tile, 64, TexW, TexH, 30f, 20f, QualityPreset.Medium);
        Assert.AreEqual(0f, r);
    }

    [Test]
    public void ZeroDensity_ReturnsZero()
    {
        var tile = new TileStats { HasCoverage = true, Density = 0f, BoneWeight = 1f };
        var r = EffectiveResolutionCalculator.ComputeR(tile, 64, TexW, TexH, 30f, 20f, QualityPreset.Medium);
        Assert.AreEqual(0f, r);
    }

    [Test]
    public void HighDensity_ClampsTo_TileSize()
    {
        // density = worldCm²/uvArea が大きい = UV が引き伸ばされている = テクセル密度が低い
        // → 高解像度が必要 → r は tileSize に clamp されるべき
        var tile = new TileStats { HasCoverage = true, Density = 1e9f, BoneWeight = 1f };
        var r = EffectiveResolutionCalculator.ComputeR(tile, 64, TexW, TexH, 30f, 20f, QualityPreset.Medium);
        Assert.AreEqual(64f, r, 0.001f,
            "high density (stretched UV) should clamp to tileSize (need full resolution)");
    }

    [Test]
    public void LowDensity_ClampsTo_RMin()
    {
        // density が小さい = UV が密 = テクセル密度が高い = 大幅縮小可能
        var tile = new TileStats { HasCoverage = true, Density = 1e-6f, BoneWeight = 1f };
        var r = EffectiveResolutionCalculator.ComputeR(tile, 64, TexW, TexH, 30f, 20f, QualityPreset.Medium);
        Assert.AreEqual(EffectiveResolutionCalculator.RMin, r, 0.001f,
            "very low density (tightly packed UV) should clamp to rMin (can aggressively downscale)");
    }

    [Test]
    public void HigherPreset_IncreasesR()
    {
        var tile = new TileStats { HasCoverage = true, Density = 10000f, BoneWeight = 1f };
        var rMed = EffectiveResolutionCalculator.ComputeR(tile, 64, TexW, TexH, 30f, 20f, QualityPreset.Medium);
        var rHigh = EffectiveResolutionCalculator.ComputeR(tile, 64, TexW, TexH, 30f, 20f, QualityPreset.High);
        Assert.Greater(rHigh, rMed);
    }

    [Test]
    public void HigherBoneWeight_IncreasesR()
    {
        var low = new TileStats { HasCoverage = true, Density = 10000f, BoneWeight = 0.3f };
        var high = new TileStats { HasCoverage = true, Density = 10000f, BoneWeight = 1.0f };
        var rLow = EffectiveResolutionCalculator.ComputeR(low, 64, TexW, TexH, 30f, 20f, QualityPreset.Medium);
        var rHigh = EffectiveResolutionCalculator.ComputeR(high, 64, TexW, TexH, 30f, 20f, QualityPreset.Medium);
        Assert.Greater(rHigh, rLow);
    }

    [Test]
    public void FurtherViewDistance_DecreasesR()
    {
        var tile = new TileStats { HasCoverage = true, Density = 10000f, BoneWeight = 1f };
        var rNear = EffectiveResolutionCalculator.ComputeR(tile, 64, TexW, TexH, 30f, 20f, QualityPreset.Medium);
        var rFar = EffectiveResolutionCalculator.ComputeR(tile, 64, TexW, TexH, 100f, 20f, QualityPreset.Medium);
        Assert.Greater(rNear, rFar);
    }

    [Test]
    public void HigherDensity_IncreasesR()
    {
        // density が大きい = テクスチャが引き伸ばされている → より高い解像度が必要
        var lowD = new TileStats { HasCoverage = true, Density = 1000f, BoneWeight = 1f };
        var highD = new TileStats { HasCoverage = true, Density = 100000f, BoneWeight = 1f };
        var rLow = EffectiveResolutionCalculator.ComputeR(lowD, 64, TexW, TexH, 30f, 20f, QualityPreset.Medium);
        var rHigh = EffectiveResolutionCalculator.ComputeR(highD, 64, TexW, TexH, 30f, 20f, QualityPreset.Medium);
        Assert.Greater(rHigh, rLow, "higher density (stretched UV) should require higher effective resolution");
    }

    [Test]
    public void SmallerTexture_IncreasesR()
    {
        // 同じ density でもテクスチャが小さいと現テクセル密度が低い → r が上がるべき
        var tile = new TileStats { HasCoverage = true, Density = 5000f, BoneWeight = 1f };
        var rBig = EffectiveResolutionCalculator.ComputeR(tile, 64, 4096, 4096, 30f, 20f, QualityPreset.Medium);
        var rSmall = EffectiveResolutionCalculator.ComputeR(tile, 64, 256, 256, 30f, 20f, QualityPreset.Medium);
        Assert.Greater(rSmall, rBig, "smaller texture has lower texel density, needs higher r");
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
