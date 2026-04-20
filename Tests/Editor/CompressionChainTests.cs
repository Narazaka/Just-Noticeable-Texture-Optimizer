using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.Compression;

public class CompressionChainTests
{
    [Test] public void NormalMap_FirstIsBC5() => Assert.AreEqual(TextureFormat.BC5, CompressionChain.For(TextureRole.NormalMap)[0]);
    [Test] public void ColorOpaque_ChainEndsUncompressed()
    {
        var c = CompressionChain.For(TextureRole.ColorOpaque);
        Assert.AreEqual(TextureFormat.RGB24, c[c.Length - 1]);
    }
}
