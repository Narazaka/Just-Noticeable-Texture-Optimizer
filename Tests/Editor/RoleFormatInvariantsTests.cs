using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.Compression;

/// <summary>
/// Critical 不変条件: 元 texture の本質的特性を保持する。
/// - 元 fmt が α 無し → 新 fmt も α 無し
/// - 元 fmt が Normal-encoding → 新 fmt も Normal 系 (BC5/BC7)
/// - 元 fmt が Single-channel → 新 fmt も Single 系 (BC4/BC7)
/// - DXT1 → DXT5 等への α 昇格を禁止
///
/// R-D4-2 で FormatCandidateSelector + CompressionCandidateEnumerator の
/// 出力のみが pipeline の候補となるため、この両者で不変条件を検証する。
/// </summary>
public class RoleFormatInvariantsTests
{
    // === DXT1 origin は DXT5/BC7 以外の α 昇格を拒否する (α 使用なしなら DXT5 にもならない) ===

    [Test]
    public void DXT1_Color_AlphaNotUsed_NeverPromotesToAlphaFormat()
    {
        var fmts = FormatCandidateSelector.Select(TextureFormat.DXT1, ShaderUsage.Color, alphaUsed: false);
        Assert.IsFalse(fmts.Contains(TextureFormat.DXT5),
            "DXT1 origin + alphaUsed=false must never upgrade to DXT5");
        Assert.IsFalse(fmts.Contains(TextureFormat.RGBA32),
            "DXT1 origin + alphaUsed=false must never upgrade to RGBA32");
    }

    [Test]
    public void DXT1_Normal_OnlyDXT1()
    {
        // 再エンコ先が意味ない → DXT1 のまま (size 縮小のみ)
        var fmts = FormatCandidateSelector.Select(TextureFormat.DXT1, ShaderUsage.Normal, alphaUsed: false);
        Assert.AreEqual(1, fmts.Count);
        Assert.AreEqual(TextureFormat.DXT1, fmts[0]);
    }

    // === BC5 origin (Normal) は BC5/BC7 以外を拒否する ===

    [Test]
    public void BC5_Normal_OnlyBC5BC7()
    {
        var fmts = FormatCandidateSelector.Select(TextureFormat.BC5, ShaderUsage.Normal, false);
        foreach (var f in fmts)
        {
            Assert.IsTrue(f == TextureFormat.BC5 || f == TextureFormat.BC7,
                $"BC5 origin Normal must only pick BC5 or BC7, got {f}");
        }
        Assert.IsFalse(fmts.Contains(TextureFormat.DXT5),
            "BC5 Normal must never pick DXT5");
    }

    // === BC4 origin (SingleChannel) は BC4/BC7 以外を拒否する ===

    [Test]
    public void BC4_SingleChannel_OnlyBC4BC7()
    {
        var fmts = FormatCandidateSelector.Select(TextureFormat.BC4, ShaderUsage.SingleChannel, false);
        foreach (var f in fmts)
        {
            Assert.IsTrue(f == TextureFormat.BC4 || f == TextureFormat.BC7,
                $"BC4 origin SingleChannel must only pick BC4 or BC7, got {f}");
        }
    }

    // === α 物理的無し fmt (RGB24/BC6H) + Color は DXT1/BC7 に制約 ===

    [Test]
    public void RGB24_Color_NeverPicksAlphaFormat()
    {
        var fmts = FormatCandidateSelector.Select(TextureFormat.RGB24, ShaderUsage.Color, false);
        Assert.IsFalse(fmts.Contains(TextureFormat.DXT5));
        Assert.IsFalse(fmts.Contains(TextureFormat.RGBA32));
    }

    // === Enumerator: no-op 含むこと / bytes <= orig 制約 ===

    [Test]
    public void Enumerator_DXT1_Color_AlphaNotUsed_HasDXT1AndBC7OnlyCandidates()
    {
        var fmts = FormatCandidateSelector.Select(TextureFormat.DXT1, ShaderUsage.Color, false);
        var list = CompressionCandidateEnumerator.Enumerate(1024, 1024, TextureFormat.DXT1, fmts, 32);
        foreach (var c in list)
        {
            Assert.IsTrue(c.Format == TextureFormat.DXT1 || c.Format == TextureFormat.BC7,
                $"DXT1 Color alphaNotUsed candidate set must only contain DXT1/BC7, got {c.Format}");
        }
        // no-op (DXT1@1024x1024) が必ず含まれる
        Assert.IsTrue(list.Any(c => c.IsNoOp && c.Format == TextureFormat.DXT1 && c.Width == 1024));
    }

    [Test]
    public void Enumerator_BC5_Normal_NeverProducesDXT5()
    {
        var fmts = FormatCandidateSelector.Select(TextureFormat.BC5, ShaderUsage.Normal, false);
        var list = CompressionCandidateEnumerator.Enumerate(1024, 1024, TextureFormat.BC5, fmts, 32);
        Assert.IsFalse(list.Any(c => c.Format == TextureFormat.DXT5),
            "BC5 Normal pipeline must never generate DXT5 candidates");
    }
}
