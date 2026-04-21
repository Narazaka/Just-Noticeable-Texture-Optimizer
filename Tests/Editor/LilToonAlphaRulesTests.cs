using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase1;

public class LilToonAlphaRulesTests
{
    // ノーマルマップ: DXT5nm でアルファに法線X成分 → alpha使用 (strip禁止)
    [Test] public void BumpMap_UsesAlpha() => Assert.IsTrue(LilToonAlphaRules.IsAlphaUsed("_BumpMap"));
    [Test] public void Bump2ndMap_UsesAlpha() => Assert.IsTrue(LilToonAlphaRules.IsAlphaUsed("_Bump2ndMap"));

    // MainTex: 常にアルファ使用
    [Test] public void MainTex_AlwaysUsesAlpha() => Assert.IsTrue(LilToonAlphaRules.IsAlphaUsed("_MainTex"));

    // Rチャネルのみマスク: alpha不使用
    [Test] public void AlphaMask_RChannelOnly_NoAlpha() => Assert.IsFalse(LilToonAlphaRules.IsAlphaUsed("_AlphaMask"));
    [Test] public void ShadowStrengthMask_NoAlpha() => Assert.IsFalse(LilToonAlphaRules.IsAlphaUsed("_ShadowStrengthMask"));
    [Test] public void SmoothnessTex_NoAlpha() => Assert.IsFalse(LilToonAlphaRules.IsAlphaUsed("_SmoothnessTex"));
    [Test] public void OutlineWidthMask_NoAlpha() => Assert.IsFalse(LilToonAlphaRules.IsAlphaUsed("_OutlineWidthMask"));
    [Test] public void MainColorAdjustMask_NoAlpha() => Assert.IsFalse(LilToonAlphaRules.IsAlphaUsed("_MainColorAdjustMask"));

    // RGBのみ使用 (alpha無関係)
    [Test] public void OutlineTex_NoAlpha() => Assert.IsFalse(LilToonAlphaRules.IsAlphaUsed("_OutlineTex"));
    [Test] public void MatCapBlendMask_NoAlpha() => Assert.IsFalse(LilToonAlphaRules.IsAlphaUsed("_MatCapBlendMask"));
    [Test] public void GlitterShapeTex_NoAlpha() => Assert.IsFalse(LilToonAlphaRules.IsAlphaUsed("_GlitterShapeTex"));

    // RGBA全体使用 (alpha = ブレンド係数等)
    [Test] public void MatCapTex_UsesAlpha() => Assert.IsTrue(LilToonAlphaRules.IsAlphaUsed("_MatCapTex"));
    [Test] public void ShadowColorTex_UsesAlpha() => Assert.IsTrue(LilToonAlphaRules.IsAlphaUsed("_ShadowColorTex"));
    [Test] public void EmissionMap_UsesAlpha() => Assert.IsTrue(LilToonAlphaRules.IsAlphaUsed("_EmissionMap"));
    [Test] public void RimColorTex_UsesAlpha() => Assert.IsTrue(LilToonAlphaRules.IsAlphaUsed("_RimColorTex"));
    [Test] public void BacklightColorTex_UsesAlpha() => Assert.IsTrue(LilToonAlphaRules.IsAlphaUsed("_BacklightColorTex"));

    // 未知プロパティ: 安全側で alpha使用
    [Test] public void UnknownProperty_ConservativeTrue() => Assert.IsTrue(LilToonAlphaRules.IsAlphaUsed("_MadeUpTex"));

}
