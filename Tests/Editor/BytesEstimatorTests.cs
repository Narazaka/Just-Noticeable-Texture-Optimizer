using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.Compression;

public class BytesEstimatorTests
{
    [Test]
    public void BitsPerPixel_DXT1_Is4()
    {
        Assert.AreEqual(4, BytesEstimator.BitsPerPixel(TextureFormat.DXT1));
        Assert.AreEqual(4, BytesEstimator.BitsPerPixel(TextureFormat.DXT1Crunched));
        Assert.AreEqual(4, BytesEstimator.BitsPerPixel(TextureFormat.BC4));
    }

    [Test]
    public void BitsPerPixel_DXT5_BC5_BC7_Is8()
    {
        Assert.AreEqual(8, BytesEstimator.BitsPerPixel(TextureFormat.DXT5));
        Assert.AreEqual(8, BytesEstimator.BitsPerPixel(TextureFormat.DXT5Crunched));
        Assert.AreEqual(8, BytesEstimator.BitsPerPixel(TextureFormat.BC5));
        Assert.AreEqual(8, BytesEstimator.BitsPerPixel(TextureFormat.BC7));
        Assert.AreEqual(8, BytesEstimator.BitsPerPixel(TextureFormat.BC6H));
    }

    [Test]
    public void BitsPerPixel_R8_Alpha8_Is8()
    {
        Assert.AreEqual(8, BytesEstimator.BitsPerPixel(TextureFormat.R8));
        Assert.AreEqual(8, BytesEstimator.BitsPerPixel(TextureFormat.Alpha8));
    }

    [Test]
    public void BitsPerPixel_RG16_Is16()
    {
        Assert.AreEqual(16, BytesEstimator.BitsPerPixel(TextureFormat.RG16));
    }

    [Test]
    public void BitsPerPixel_RGB24_Is24()
    {
        Assert.AreEqual(24, BytesEstimator.BitsPerPixel(TextureFormat.RGB24));
    }

    [Test]
    public void BitsPerPixel_RGBA32_Is32()
    {
        Assert.AreEqual(32, BytesEstimator.BitsPerPixel(TextureFormat.RGBA32));
        Assert.AreEqual(32, BytesEstimator.BitsPerPixel(TextureFormat.ARGB32));
        Assert.AreEqual(32, BytesEstimator.BitsPerPixel(TextureFormat.BGRA32));
    }

    [Test]
    public void BaseBytes_1024x1024_BC7_Is1MiB()
    {
        // 1024*1024*8bpp/8 = 1048576 bytes
        Assert.AreEqual(1024 * 1024, BytesEstimator.BaseBytes(1024, 1024, TextureFormat.BC7));
    }

    [Test]
    public void BaseBytes_1024x1024_DXT1_Is512KiB()
    {
        // 1024*1024*4bpp/8 = 524288 bytes
        Assert.AreEqual(1024 * 1024 / 2, BytesEstimator.BaseBytes(1024, 1024, TextureFormat.DXT1));
    }

    [Test]
    public void WithMips_IsApproximately133PercentOfBase()
    {
        long baseBytes = BytesEstimator.BaseBytes(1024, 1024, TextureFormat.BC7);
        long mipped = BytesEstimator.WithMips(1024, 1024, TextureFormat.BC7);
        // 4/3 = 1.333...
        Assert.AreEqual(baseBytes * 4 / 3, mipped);
        // sanity: within 0.3 .. 0.4 over base
        Assert.Greater(mipped, baseBytes);
        Assert.Less(mipped, baseBytes * 2);
    }

    [Test]
    public void WithMips_ZeroSize_ReturnsZero()
    {
        Assert.AreEqual(0, BytesEstimator.WithMips(0, 0, TextureFormat.BC7));
        Assert.AreEqual(0, BytesEstimator.WithMips(0, 128, TextureFormat.BC7));
    }

    [Test]
    public void WithMips_RectangularTexture_ComputesCorrectly()
    {
        // 512x256 BC7 = 512*256*8/8 = 131072 bytes, *4/3 ≒ 174762
        long baseBytes = 512L * 256L;
        Assert.AreEqual(baseBytes, BytesEstimator.BaseBytes(512, 256, TextureFormat.BC7));
        Assert.AreEqual(baseBytes * 4 / 3, BytesEstimator.WithMips(512, 256, TextureFormat.BC7));
    }
}
