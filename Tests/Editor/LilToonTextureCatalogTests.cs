using NUnit.Framework;
using Narazaka.VRChat.Jnto.Editor.Phase2.Compression;
using Narazaka.VRChat.Jnto.Editor.Shared;

public class LilToonTextureCatalogTests
{
    [Test]
    public void MainTex_ColorWithAlpha()
    {
        Assert.IsTrue(LilToonTextureCatalog.TryGet("_MainTex", out var info));
        Assert.AreEqual(ShaderUsage.Color, info.Usage);
        Assert.IsTrue(info.AlphaUsed);
    }

    [Test]
    public void BumpMap_NormalWithAlpha_DXT5nm()
    {
        Assert.IsTrue(LilToonTextureCatalog.TryGet("_BumpMap", out var info));
        Assert.AreEqual(ShaderUsage.Normal, info.Usage);
        Assert.IsTrue(info.AlphaUsed, "DXT5nm packs normal X into alpha");
    }

    [Test]
    public void OutlineVectorTex_NormalWithoutAlpha_RGBOnly()
    {
        Assert.IsTrue(LilToonTextureCatalog.TryGet("_OutlineVectorTex", out var info));
        Assert.AreEqual(ShaderUsage.Normal, info.Usage);
        Assert.IsFalse(info.AlphaUsed);
    }

    [Test]
    public void ShadowStrengthMask_SingleChannelWithoutAlpha()
    {
        Assert.IsTrue(LilToonTextureCatalog.TryGet("_ShadowStrengthMask", out var info));
        Assert.AreEqual(ShaderUsage.SingleChannel, info.Usage);
        Assert.IsFalse(info.AlphaUsed);
    }

    [Test]
    public void OutlineTex_ColorWithoutAlpha_RGBOnly()
    {
        Assert.IsTrue(LilToonTextureCatalog.TryGet("_OutlineTex", out var info));
        Assert.AreEqual(ShaderUsage.Color, info.Usage);
        Assert.IsFalse(info.AlphaUsed);
    }

    [Test]
    public void EmissionBlendMask_SingleChannelButAlphaUsed_PreservesBothLegacyBehaviors()
    {
        // Legacy: LilToonAlphaRules said alphaUsed=true, ShaderUsageInferrer said SingleChannel.
        // Catalog records both bits to preserve behavior in callers.
        Assert.IsTrue(LilToonTextureCatalog.TryGet("_EmissionBlendMask", out var info));
        Assert.AreEqual(ShaderUsage.SingleChannel, info.Usage);
        Assert.IsTrue(info.AlphaUsed);
    }

    [Test]
    public void UnknownProperty_ReturnsFalse()
    {
        Assert.IsFalse(LilToonTextureCatalog.TryGet("_MadeUpTex", out _));
    }

    [Test]
    public void NullProperty_DoesNotThrow()
    {
        Assert.IsFalse(LilToonTextureCatalog.TryGet(null, out _));
    }
}
