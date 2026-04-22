using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto;
using Narazaka.VRChat.Jnto.Editor.Phase2.Compression;

/// <summary>
/// Critical 不変条件: 元 texture の本質的特性を保持する。
/// - 元 fmt が α 無し → 新 fmt も α 無し
/// - 元 fmt が NormalMap → 新 fmt も NormalMap 系
/// - 元 fmt が SingleChannel → 新 fmt も SingleChannel 系
/// </summary>
public class RoleFormatInvariantsTests
{
    // === Role 判定 ===

    [Test]
    public void Classify_OriginalDxt1Texture_ReturnsColorOpaqueRegardlessOfAlphaFlag()
    {
        // 元 fmt が DXT1 (α 1bit、実質 α 無し) なら、material が α 必要と言っても ColorOpaque
        var t = new Texture2D(4, 4, TextureFormat.DXT1, false);
        try
        {
            // alphaRequired=true でも、元が DXT1 なら α は実質情報ゼロ → ColorOpaque
            var role = TextureTypeClassifier.Classify(null, null, t, alphaRequired: true);
            Assert.AreEqual(TextureRole.ColorOpaque, role,
                "DXT1 input has no usable alpha; classifier must not promote to ColorAlpha");
        }
        finally { Object.DestroyImmediate(t); }
    }

    [Test]
    public void Classify_OriginalBC5Texture_ReturnsNormalMap()
    {
        var t = new Texture2D(4, 4, TextureFormat.BC5, false);
        try
        {
            var role = TextureTypeClassifier.Classify(null, null, t, alphaRequired: false);
            Assert.AreEqual(TextureRole.NormalMap, role,
                "BC5 is a normal-map format; classifier must reflect this");
        }
        finally { Object.DestroyImmediate(t); }
    }

    [Test]
    public void Classify_OriginalBC4Texture_ReturnsSingleChannel()
    {
        var t = new Texture2D(4, 4, TextureFormat.BC4, false);
        try
        {
            var role = TextureTypeClassifier.Classify(null, null, t, alphaRequired: false);
            Assert.AreEqual(TextureRole.SingleChannel, role,
                "BC4 is a single-channel format");
        }
        finally { Object.DestroyImmediate(t); }
    }

    [Test]
    public void Classify_OriginalDxt5Texture_DefaultsToColorAlpha()
    {
        var t = new Texture2D(4, 4, TextureFormat.DXT5, false);
        try
        {
            var role = TextureTypeClassifier.Classify(null, null, t, alphaRequired: false);
            // DXT5 は α 持つ → ColorAlpha
            Assert.AreEqual(TextureRole.ColorAlpha, role);
        }
        finally { Object.DestroyImmediate(t); }
    }

    // === Format 選択不変条件 ===

    [Test]
    public void Lightweight_ColorOpaque_NeverPicksAlphaFormat()
    {
        // ColorOpaque role からは α 形式は出てこない
        var stats = new BlockStats[16];
        for (int i = 0; i < stats.Length; i++)
            stats[i] = new BlockStats { Planarity = 0.05f };
        var p = FormatPredictor.PredictLightweight(stats, TextureRole.ColorOpaque, QualityPreset.Medium);
        Assert.AreNotEqual(TextureFormat.DXT5, p.Format,
            "ColorOpaque must never pick DXT5 (alpha format)");
        Assert.AreNotEqual(TextureFormat.BC7, p.Format,
            "ColorOpaque lightweight should be DXT1, not BC7");
    }

    [Test]
    public void Lightweight_NormalMap_PicksBC5()
    {
        var stats = new BlockStats[16];
        var p = FormatPredictor.PredictLightweight(stats, TextureRole.NormalMap, QualityPreset.Medium);
        Assert.AreEqual(TextureFormat.BC5, p.Format);
    }

    // === Pipeline-level fallback 不変条件 ===

    [Test]
    public void ChooseFormat_OriginalAlphaFree_FallbackNeverPicksAlphaFormat()
    {
        // BC7Fallback は private static なので reflection で呼び出し、
        // ColorOpaque role が DXT5 に化けないことを確認する。
        var pipelineType = typeof(NewPhase2Pipeline);
        var method = pipelineType.GetMethod("BC7Fallback",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.IsNotNull(method, "BC7Fallback method must exist on NewPhase2Pipeline");
        var fmt = (TextureFormat)method.Invoke(null, new object[] { TextureRole.ColorOpaque });
        Assert.AreNotEqual(TextureFormat.DXT5, fmt,
            "ColorOpaque BC7Fallback must not pick DXT5");
    }
}
