using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2;

public class ResolutionReducerTests
{
    [Test]
    public void Resize_To128_ReturnsExpectedSize()
    {
        var src = new Texture2D(512, 512, TextureFormat.RGBA32, true);
        var dst = ResolutionReducer.Resize(src, 128);
        Assert.AreEqual(128, dst.width);
        Assert.AreEqual(128, dst.height);
    }

    [Test]
    public void Resize_SameSize_ReturnsCopyNotSame()
    {
        var src = new Texture2D(256, 256);
        var dst = ResolutionReducer.Resize(src, 256);
        Assert.AreNotSame(src, dst);
        Assert.AreEqual(256, dst.width);
    }
}
