using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.Compression;

public class FormatCandidateSelectorTests
{
    // === ケース A: Normal-encoding 形式 ===

    [Test]
    public void CaseA_BC5_AnyUsage_ReturnsNormalCandidates()
    {
        AssertContainsExactly(
            FormatCandidateSelector.Select(TextureFormat.BC5, ShaderUsage.Color, false),
            TextureFormat.BC5, TextureFormat.BC7);
        AssertContainsExactly(
            FormatCandidateSelector.Select(TextureFormat.BC5, ShaderUsage.Normal, false),
            TextureFormat.BC5, TextureFormat.BC7);
    }

    [Test]
    public void CaseA_RG16_ReturnsBC5AndBC7()
    {
        var c = FormatCandidateSelector.Select(TextureFormat.RG16, ShaderUsage.Color, false);
        Assert.Contains(TextureFormat.RG16, c);
        Assert.Contains(TextureFormat.BC5, c);
        Assert.Contains(TextureFormat.BC7, c);
    }

    // === ケース B: Single-channel ===

    [Test]
    public void CaseB_BC4_ReturnsBC4AndBC7()
    {
        AssertContainsExactly(
            FormatCandidateSelector.Select(TextureFormat.BC4, ShaderUsage.Color, false),
            TextureFormat.BC4, TextureFormat.BC7);
    }

    [Test]
    public void CaseB_R8_ReturnsR8BC4BC7()
    {
        AssertContainsExactly(
            FormatCandidateSelector.Select(TextureFormat.R8, ShaderUsage.SingleChannel, false),
            TextureFormat.R8, TextureFormat.BC4, TextureFormat.BC7);
    }

    [Test]
    public void CaseB_Alpha8_ReturnsAlpha8BC4BC7()
    {
        AssertContainsExactly(
            FormatCandidateSelector.Select(TextureFormat.Alpha8, ShaderUsage.SingleChannel, false),
            TextureFormat.Alpha8, TextureFormat.BC4, TextureFormat.BC7);
    }

    // === ケース C: α 物理的無し RGB ===

    [Test]
    public void CaseC1_RGB24_Color_ReturnsRGB24DXT1BC7()
    {
        AssertContainsExactly(
            FormatCandidateSelector.Select(TextureFormat.RGB24, ShaderUsage.Color, false),
            TextureFormat.RGB24, TextureFormat.DXT1, TextureFormat.BC7);
    }

    [Test]
    public void CaseC2_RGB24_Normal_ReturnsOnlyRGB24()
    {
        AssertContainsExactly(
            FormatCandidateSelector.Select(TextureFormat.RGB24, ShaderUsage.Normal, false),
            TextureFormat.RGB24);
    }

    [Test]
    public void CaseC3_RGB24_SingleChannel_ReturnsOnlyRGB24()
    {
        AssertContainsExactly(
            FormatCandidateSelector.Select(TextureFormat.RGB24, ShaderUsage.SingleChannel, false),
            TextureFormat.RGB24);
    }

    // === ケース D: DXT1 ===

    [Test]
    public void CaseD1_DXT1_Color_ReturnsDXT1BC7()
    {
        AssertContainsExactly(
            FormatCandidateSelector.Select(TextureFormat.DXT1, ShaderUsage.Color, false),
            TextureFormat.DXT1, TextureFormat.BC7);
    }

    [Test]
    public void CaseD2_DXT1_Normal_ReturnsOnlyDXT1()
    {
        AssertContainsExactly(
            FormatCandidateSelector.Select(TextureFormat.DXT1, ShaderUsage.Normal, false),
            TextureFormat.DXT1);
    }

    [Test]
    public void CaseD3_DXT1_SingleChannel_ReturnsOnlyDXT1()
    {
        AssertContainsExactly(
            FormatCandidateSelector.Select(TextureFormat.DXT1, ShaderUsage.SingleChannel, false),
            TextureFormat.DXT1);
    }

    // === ケース E: α 持つ汎用 ===

    [Test]
    public void CaseE1_BC7_Color_AlphaUsed_ReturnsDXT5BC7()
    {
        AssertContainsExactly(
            FormatCandidateSelector.Select(TextureFormat.BC7, ShaderUsage.Color, alphaUsed: true),
            TextureFormat.DXT5, TextureFormat.BC7);
    }

    [Test]
    public void CaseE1_DXT5_Color_AlphaUsed_ReturnsDXT5BC7()
    {
        AssertContainsExactly(
            FormatCandidateSelector.Select(TextureFormat.DXT5, ShaderUsage.Color, alphaUsed: true),
            TextureFormat.DXT5, TextureFormat.BC7);
    }

    [Test]
    public void CaseE2_BC7_Color_AlphaNotUsed_ReturnsDXT1BC7()
    {
        // α 廃棄可、DXT5 は冗長で除外
        AssertContainsExactly(
            FormatCandidateSelector.Select(TextureFormat.BC7, ShaderUsage.Color, alphaUsed: false),
            TextureFormat.DXT1, TextureFormat.BC7);
    }

    [Test]
    public void CaseE2_DXT5_Color_AlphaNotUsed_ReturnsDXT1BC7()
    {
        AssertContainsExactly(
            FormatCandidateSelector.Select(TextureFormat.DXT5, ShaderUsage.Color, alphaUsed: false),
            TextureFormat.DXT1, TextureFormat.BC7);
    }

    [Test]
    public void CaseE3_BC7_Normal_ReturnsOrigBC5BC7()
    {
        AssertContainsExactly(
            FormatCandidateSelector.Select(TextureFormat.BC7, ShaderUsage.Normal, false),
            TextureFormat.BC7, TextureFormat.BC5);
        // BC7 が orig と BC7 fallback で重複 → distinct
    }

    [Test]
    public void CaseE3_DXT5_Normal_ReturnsDXT5BC5BC7()
    {
        AssertContainsExactly(
            FormatCandidateSelector.Select(TextureFormat.DXT5, ShaderUsage.Normal, false),
            TextureFormat.DXT5, TextureFormat.BC5, TextureFormat.BC7);
    }

    [Test]
    public void CaseE4_BC7_SingleChannel_ReturnsOrigBC4BC7()
    {
        AssertContainsExactly(
            FormatCandidateSelector.Select(TextureFormat.BC7, ShaderUsage.SingleChannel, false),
            TextureFormat.BC7, TextureFormat.BC4);
    }

    [Test]
    public void CaseE4_DXT5_SingleChannel_ReturnsDXT5BC4BC7()
    {
        AssertContainsExactly(
            FormatCandidateSelector.Select(TextureFormat.DXT5, ShaderUsage.SingleChannel, false),
            TextureFormat.DXT5, TextureFormat.BC4, TextureFormat.BC7);
    }

    // === Crunched 系 ===

    [Test]
    public void DXT1Crunched_Color_TreatedAsDXT1()
    {
        AssertContainsExactly(
            FormatCandidateSelector.Select(TextureFormat.DXT1Crunched, ShaderUsage.Color, false),
            TextureFormat.DXT1, TextureFormat.BC7);
        // 元 DXT1Crunched は出力候補に含めない (DXT1 で十分)
    }

    [Test]
    public void DXT5Crunched_Color_AlphaUsed_TreatedAsDXT5()
    {
        AssertContainsExactly(
            FormatCandidateSelector.Select(TextureFormat.DXT5Crunched, ShaderUsage.Color, true),
            TextureFormat.DXT5, TextureFormat.BC7);
    }

    // === ケース F: unknown ===

    [Test]
    public void CaseF_RGBAFloat_ReturnsOrigBC7()
    {
        AssertContainsExactly(
            FormatCandidateSelector.Select(TextureFormat.RGBAFloat, ShaderUsage.Color, true),
            TextureFormat.RGBAFloat, TextureFormat.BC7);
    }

    // === 一般プロパティ ===

    [Test]
    public void Result_ContainsOriginal_ForCasesABCAndDF()
    {
        // ケース A/B/C/F では元 fmt は常に no-op 候補として含まれる。
        // ケース D は DXT1Crunched→DXT1 への正規化があるため別途検証。
        // ケース E は α 廃棄/再エンコード判定により元 fmt が落ちる可能性がある
        // (例: RGBA32 Color → DXT5/BC7 のみ) ため本テストの対象外。
        var cases = new (TextureFormat fmt, ShaderUsage usage, bool alphaUsed)[]
        {
            // A: normal-encoding
            (TextureFormat.BC5, ShaderUsage.Color, false),
            (TextureFormat.RG16, ShaderUsage.Color, false),
            // B: single-channel
            (TextureFormat.BC4, ShaderUsage.Color, false),
            (TextureFormat.R8, ShaderUsage.SingleChannel, false),
            (TextureFormat.Alpha8, ShaderUsage.SingleChannel, false),
            // C: α 物理的無し RGB
            (TextureFormat.RGB24, ShaderUsage.Color, false),
            (TextureFormat.BC6H, ShaderUsage.Color, false),
            // F: unknown
            (TextureFormat.RGBAFloat, ShaderUsage.Color, true),
        };
        foreach (var (fmt, usage, alphaUsed) in cases)
        {
            var c = FormatCandidateSelector.Select(fmt, usage, alphaUsed);
            Assert.Contains(fmt, c, $"fmt {fmt} (usage={usage}, alpha={alphaUsed}) should be in candidate set (no-op)");
        }
    }

    [Test]
    public void Result_NoDuplicates()
    {
        var c = FormatCandidateSelector.Select(TextureFormat.BC7, ShaderUsage.Color, true);
        var set = new HashSet<TextureFormat>(c);
        Assert.AreEqual(set.Count, c.Count, "candidate list must be distinct");
    }

    static void AssertContainsExactly(List<TextureFormat> actual, params TextureFormat[] expected)
    {
        Assert.AreEqual(expected.Length, actual.Count,
            $"Expected exactly {expected.Length} candidates {string.Join(",", expected)}, got {actual.Count}: {string.Join(",", actual)}");
        foreach (var e in expected)
        {
            Assert.Contains(e, actual, $"Expected {e} in candidates");
        }
    }
}
