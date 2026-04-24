using NUnit.Framework;
using Narazaka.VRChat.Jnto.Editor.Shared;
using Narazaka.VRChat.Jnto.Editor.Shared.Extensions;

public class UzumoreTextureCatalogExtensionTests
{
    // Matches は path を用いた helper (static method) で試験可能にする想定
    [TestCase("Packages/jp.sigmal00.uzumore-shader/Runtime/Shaders/lts.lilcontainer", true)]
    [TestCase("Packages/jp.sigmal00.uzumore-shader/Runtime/Shaders/lts_fur.lilcontainer", true)]
    [TestCase("Packages/jp.lilxyzw.liltoon/Shader/lts.shader", false)]
    [TestCase("Packages/com.example.other/foo.shader", false)]
    [TestCase("", false)]
    [TestCase(null, false)]
    public void MatchesPath(string path, bool expected)
    {
        Assert.AreEqual(expected, UzumoreTextureCatalogExtension.MatchesPath(path));
    }

    [Test]
    public void TryClassify_UzumoreMask_ReturnsValidInfo()
    {
        var ext = new UzumoreTextureCatalogExtension();
        Assert.IsTrue(ext.TryClassify(shader: null, variantId: "lts", propName: "_UzumoreMask", out var info));
        Assert.AreNotEqual(ChannelMask.None, info.ReadChannels);
        Assert.IsFalse(string.IsNullOrEmpty(info.EvidenceRef));
    }

    [Test]
    public void TryClassify_UnknownProperty_ReturnsFalse()
    {
        var ext = new UzumoreTextureCatalogExtension();
        Assert.IsFalse(ext.TryClassify(shader: null, variantId: "lts", propName: "_MainTex", out _));
    }
}
