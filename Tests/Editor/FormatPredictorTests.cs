using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto;
using Narazaka.VRChat.Jnto.Editor.Phase2.Compression;

public class FormatPredictorTests
{
    [Test]
    public void FlatBlocks_PredictDxt1_HighConfidence()
    {
        var stats = new BlockStats[64];
        for (int i = 0; i < stats.Length; i++)
            stats[i] = new BlockStats { Planarity = 0.02f, Nonlinearity = 0.01f };
        var p = FormatPredictor.PredictLightweight(stats, TextureRole.ColorOpaque, QualityPreset.Medium);
        Assert.AreEqual(TextureFormat.DXT1, p.Format);
        Assert.Greater(p.Confidence, 0.8f);
    }

    [Test]
    public void HighVarianceBlocks_PredictDxt1_LowConfidence()
    {
        var stats = new BlockStats[64];
        for (int i = 0; i < stats.Length; i++)
            stats[i] = new BlockStats { Planarity = 0.5f, Nonlinearity = 0.3f };
        var p = FormatPredictor.PredictLightweight(stats, TextureRole.ColorOpaque, QualityPreset.Medium);
        Assert.Less(p.Confidence, 0.3f);
    }

    [Test]
    public void ColorAlpha_ConsidersAlphaNonLinearity()
    {
        var stats = new BlockStats[64];
        for (int i = 0; i < stats.Length; i++)
            stats[i] = new BlockStats { Planarity = 0.02f, Nonlinearity = 0.01f, AlphaNonlinearity = 0.3f };
        var p = FormatPredictor.PredictLightweight(stats, TextureRole.ColorAlpha, QualityPreset.High);
        Assert.AreEqual(TextureFormat.DXT5, p.Format);
        Assert.Less(p.Confidence, 0.5f);
    }

    [Test]
    public void NormalMap_PredictBc5()
    {
        var stats = new BlockStats[64];
        for (int i = 0; i < stats.Length; i++)
            stats[i] = new BlockStats { Nonlinearity = 0.02f };
        var p = FormatPredictor.PredictLightweight(stats, TextureRole.NormalMap, QualityPreset.Medium);
        Assert.AreEqual(TextureFormat.BC5, p.Format);
        Assert.Greater(p.Confidence, 0.8f);
    }

    [Test]
    public void SingleChannel_AlwaysHighConfidenceBC4()
    {
        var stats = new BlockStats[16];
        var p = FormatPredictor.PredictLightweight(stats, TextureRole.SingleChannel, QualityPreset.Ultra);
        Assert.AreEqual(TextureFormat.BC4, p.Format);
        Assert.AreEqual(1f, p.Confidence);
    }

    [Test]
    public void HigherPreset_LowersConfidenceForSameStats()
    {
        var stats = new BlockStats[100];
        for (int i = 0; i < 15; i++) stats[i] = new BlockStats { Planarity = 0.5f };
        // Medium threshold 10% → 15% fail → confidence 0
        var pMedium = FormatPredictor.PredictLightweight(stats, TextureRole.ColorOpaque, QualityPreset.Medium);
        // Low threshold 20% → 15%/20% = 0.75 → confidence 0.25
        var pLow = FormatPredictor.PredictLightweight(stats, TextureRole.ColorOpaque, QualityPreset.Low);
        Assert.Greater(pLow.Confidence, pMedium.Confidence,
            "easier preset should produce higher confidence");
    }
}
