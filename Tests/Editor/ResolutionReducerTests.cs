using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.Compression;

public class ResolutionReducerTests
{
    [Test]
    public void Resize_SquareTexture_ReturnsExpectedSize()
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

    [Test]
    public void Resize_NonSquare_WideTexture_PreservesAspectRatio()
    {
        var src = new Texture2D(2048, 1024, TextureFormat.RGBA32, false);
        var dst = ResolutionReducer.Resize(src, 512);
        Assert.AreEqual(512, dst.width);
        Assert.AreEqual(256, dst.height);
    }

    [Test]
    public void Resize_NonSquare_TallTexture_PreservesAspectRatio()
    {
        var src = new Texture2D(512, 2048, TextureFormat.RGBA32, false);
        var dst = ResolutionReducer.Resize(src, 256);
        Assert.AreEqual(64, dst.width);
        Assert.AreEqual(256, dst.height);
    }

    [Test]
    public void Resize_ResultDimensions_AreMultipleOf4()
    {
        var src = new Texture2D(1000, 600, TextureFormat.RGBA32, false);
        var dst = ResolutionReducer.Resize(src, 256);
        Assert.AreEqual(0, dst.width % 4, "Width should be multiple of 4");
        Assert.AreEqual(0, dst.height % 4, "Height should be multiple of 4");
    }
}
