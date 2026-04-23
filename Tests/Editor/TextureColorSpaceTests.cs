using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.Compression;
using Narazaka.VRChat.Jnto.Editor.Phase2.GpuPipeline;

public class TextureColorSpaceTests
{
    [Test]
    public void RuntimeTexture_RGBAHalf_IsLinear()
    {
        // RGBAHalf is physically linear; no importer attached
        var tex = new Texture2D(8, 8, TextureFormat.RGBAHalf, false);
        try { Assert.IsTrue(TextureColorSpace.IsLinear(tex, ShaderUsage.Color)); }
        finally { Object.DestroyImmediate(tex); }
    }

    [Test]
    public void RuntimeTexture_RGBA32_ColorUsage_IsSrgb()
    {
        var tex = new Texture2D(8, 8, TextureFormat.RGBA32, false);
        try { Assert.IsFalse(TextureColorSpace.IsLinear(tex, ShaderUsage.Color)); }
        finally { Object.DestroyImmediate(tex); }
    }

    [Test]
    public void RuntimeTexture_RGBA32_NormalUsage_IsLinear()
    {
        var tex = new Texture2D(8, 8, TextureFormat.RGBA32, false);
        try { Assert.IsTrue(TextureColorSpace.IsLinear(tex, ShaderUsage.Normal)); }
        finally { Object.DestroyImmediate(tex); }
    }

    [Test]
    public void RuntimeTexture_RGBA32_SingleChannelUsage_IsLinear()
    {
        var tex = new Texture2D(8, 8, TextureFormat.RGBA32, false);
        try { Assert.IsTrue(TextureColorSpace.IsLinear(tex, ShaderUsage.SingleChannel)); }
        finally { Object.DestroyImmediate(tex); }
    }

    [Test]
    public void RuntimeTexture_BC6H_IsLinear()
    {
        // BC6H is HDR, physically linear
        var tex = new Texture2D(8, 8, TextureFormat.BC6H, false);
        try { Assert.IsTrue(TextureColorSpace.IsLinear(tex, ShaderUsage.Color)); }
        finally { Object.DestroyImmediate(tex); }
    }

    [Test]
    public void NullTexture_FallsBackToUsage()
    {
        Assert.IsTrue(TextureColorSpace.IsLinear(null, ShaderUsage.Normal));
        Assert.IsFalse(TextureColorSpace.IsLinear(null, ShaderUsage.Color));
    }
}
