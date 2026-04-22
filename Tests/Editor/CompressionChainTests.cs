using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.Compression;

public class CompressionChainTests
{
    [Test] public void NormalMap_DefaultFirstIsBC5() => Assert.AreEqual(TextureFormat.BC5, CompressionChain.For(TextureRole.NormalMap)[0]);
    [Test] public void ColorOpaque_ChainEndsBc7()
    {
        var c = CompressionChain.For(TextureRole.ColorOpaque);
        Assert.AreEqual(TextureFormat.BC7, c[c.Length - 1]);
    }
    [Test] public void ColorOpaque_DefaultLength() => Assert.AreEqual(2, CompressionChain.For(TextureRole.ColorOpaque).Length);
    [Test] public void ColorAlpha_DefaultLength() => Assert.AreEqual(2, CompressionChain.For(TextureRole.ColorAlpha).Length);

    [Test] public void OriginalFormat_PrependedWhenNotInChain()
    {
        var c = CompressionChain.For(TextureRole.ColorOpaque, TextureFormat.DXT5);
        Assert.AreEqual(TextureFormat.DXT5, c[0], "Original format should be first when not in default chain");
        Assert.AreEqual(3, c.Length);
    }

    [Test] public void OriginalFormat_NotDuplicated()
    {
        var c = CompressionChain.For(TextureRole.ColorOpaque, TextureFormat.DXT1);
        Assert.AreEqual(2, c.Length, "Original format already in chain should not be duplicated");
    }
}
