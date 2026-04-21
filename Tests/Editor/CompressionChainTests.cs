using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.Compression;

public class CompressionChainTests
{
    [Test] public void NormalMap_FirstIsBC5() => Assert.AreEqual(TextureFormat.BC5, CompressionChain.For(TextureRole.NormalMap)[0]);
    [Test] public void ColorOpaque_ChainEndsBc7()
    {
        var c = CompressionChain.For(TextureRole.ColorOpaque);
        Assert.AreEqual(TextureFormat.BC7, c[c.Length - 1]);
    }
    [Test] public void ColorOpaque_NoBloat() => Assert.AreEqual(2, CompressionChain.For(TextureRole.ColorOpaque).Length);
    [Test] public void ColorAlpha_NoBloat() => Assert.AreEqual(2, CompressionChain.For(TextureRole.ColorAlpha).Length);
}
