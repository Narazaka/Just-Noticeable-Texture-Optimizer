using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase1;

public class LilToonAlphaRulesTests
{
    static Shader L => Shader.Find("lilToon");

    static bool IsAlpha(string prop)
    {
        var s = L;
        if (s == null) Assert.Ignore("lilToon shader not available");
        return LilToonAlphaRules.IsAlphaUsed(s, prop);
    }

    // ノーマルマップ: DXT5nm (.ag) → alpha 使用 (strip禁止)
    [Test] public void BumpMap_UsesAlpha() => Assert.IsTrue(IsAlpha("_BumpMap"));
    [Test] public void Bump2ndMap_UsesAlpha() => Assert.IsTrue(IsAlpha("_Bump2ndMap"));

    [Test] public void MainTex_AlwaysUsesAlpha() => Assert.IsTrue(IsAlpha("_MainTex"));

    [Test] public void AlphaMask_RChannelOnly_NoAlpha() => Assert.IsFalse(IsAlpha("_AlphaMask"));
    // _ShadowStrengthMask: SDF face shadow mode reads .rgba → alpha used (hlsl audit correction)
    [Test] public void ShadowStrengthMask_UsesAlpha() => Assert.IsTrue(IsAlpha("_ShadowStrengthMask"));
    [Test] public void SmoothnessTex_NoAlpha() => Assert.IsFalse(IsAlpha("_SmoothnessTex"));
    [Test] public void OutlineWidthMask_NoAlpha() => Assert.IsFalse(IsAlpha("_OutlineWidthMask"));
    [Test] public void MainColorAdjustMask_NoAlpha() => Assert.IsFalse(IsAlpha("_MainColorAdjustMask"));

    // _OutlineTex: cutout/transparent outline variants use fd.col.a for alpha test → alpha IS used
    [Test] public void OutlineTex_UsesAlpha() => Assert.IsTrue(IsAlpha("_OutlineTex"));
    [Test] public void MatCapBlendMask_NoAlpha() => Assert.IsFalse(IsAlpha("_MatCapBlendMask"));
    // _GlitterShapeTex: shapeTex.rgb * shapeTex.a → alpha IS used as shape mask (hlsl audit correction)
    [Test] public void GlitterShapeTex_UsesAlpha() => Assert.IsTrue(IsAlpha("_GlitterShapeTex"));

    [Test] public void MatCapTex_UsesAlpha() => Assert.IsTrue(IsAlpha("_MatCapTex"));
    [Test] public void ShadowColorTex_UsesAlpha() => Assert.IsTrue(IsAlpha("_ShadowColorTex"));
    [Test] public void EmissionMap_UsesAlpha() => Assert.IsTrue(IsAlpha("_EmissionMap"));
    [Test] public void RimColorTex_UsesAlpha() => Assert.IsTrue(IsAlpha("_RimColorTex"));
    [Test] public void BacklightColorTex_UsesAlpha() => Assert.IsTrue(IsAlpha("_BacklightColorTex"));

    // 未知プロパティ: 安全側 true
    [Test] public void UnknownProperty_ConservativeTrue() => Assert.IsTrue(IsAlpha("_MadeUpTex"));
}
