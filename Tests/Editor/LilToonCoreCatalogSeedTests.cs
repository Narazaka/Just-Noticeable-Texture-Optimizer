using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Shared;
using Narazaka.VRChat.Jnto.Editor.Phase2.Compression;

public class LilToonCoreCatalogSeedTests
{
    // 従来 LilToonTextureCatalogTests と同じ key を新 API で引けることを確認する移行検証。
    // Shader 実体に依存するので lilToon が無ければ skip。

    static Shader GetLilToonShader()
    {
        return Shader.Find("lilToon");
    }

    [Test] public void MainTex_ColorAlpha()
    {
        var s = GetLilToonShader();
        if (s == null) Assert.Ignore();
        Assert.IsTrue(LilToonPropertyCatalog.TryGet(s, "_MainTex", out var i));
        Assert.AreEqual(ShaderUsage.Color, i.Usage);
        Assert.IsTrue(i.AlphaUsed);
    }

    [Test] public void BumpMap_NormalAlpha()
    {
        var s = GetLilToonShader();
        if (s == null) Assert.Ignore();
        Assert.IsTrue(LilToonPropertyCatalog.TryGet(s, "_BumpMap", out var i));
        Assert.AreEqual(ShaderUsage.Normal, i.Usage);
        Assert.IsTrue(i.AlphaUsed, "DXT5nm packs normal X into alpha");
    }

    [Test] public void OutlineVectorTex_NormalNoAlpha()
    {
        var s = GetLilToonShader();
        if (s == null) Assert.Ignore();
        Assert.IsTrue(LilToonPropertyCatalog.TryGet(s, "_OutlineVectorTex", out var i));
        Assert.AreEqual(ShaderUsage.Normal, i.Usage);
        Assert.IsFalse(i.AlphaUsed);
    }

    // _ShadowStrengthMask: SDF face shadow mode (_ShadowMaskType==2) reads .rgba all.
    // Runtime mode unknown at compression time → conservative union = Color+RGBA.
    // Seed was SingleChannel but hlsl audit corrected to Color+RGBA to prevent BC4 destroying G/B/A.
    [Test] public void ShadowStrengthMask_ColorAlpha_SDFShadowRegressionGuard()
    {
        var s = GetLilToonShader();
        if (s == null) Assert.Ignore();
        Assert.IsTrue(LilToonPropertyCatalog.TryGet(s, "_ShadowStrengthMask", out var i));
        Assert.AreEqual(ShaderUsage.Color, i.Usage);
        Assert.IsTrue(i.AlphaUsed);
    }

    [Test] public void OutlineTex_ColorNoAlpha()
    {
        var s = GetLilToonShader();
        if (s == null) Assert.Ignore();
        Assert.IsTrue(LilToonPropertyCatalog.TryGet(s, "_OutlineTex", out var i));
        Assert.AreEqual(ShaderUsage.Color, i.Usage);
        Assert.IsFalse(i.AlphaUsed);
    }

    [Test] public void EmissionBlendMask_ColorAlpha_RegressionGuard()
    {
        var s = GetLilToonShader();
        if (s == null) Assert.Ignore();
        Assert.IsTrue(LilToonPropertyCatalog.TryGet(s, "_EmissionBlendMask", out var i));
        Assert.AreEqual(ShaderUsage.Color, i.Usage);
        Assert.IsTrue(i.AlphaUsed);
    }

    [Test] public void UnknownProperty_ReturnsFalse()
    {
        var s = GetLilToonShader();
        if (s == null) Assert.Ignore();
        Assert.IsFalse(LilToonPropertyCatalog.TryGet(s, "_MadeUpTex", out _));
    }
}
