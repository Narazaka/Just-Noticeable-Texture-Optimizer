using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Complexity;

public class ComplexitySamplerTests
{
    [Test]
    public void Sample_FullUvRect_ReturnsAllPixels()
    {
        var t = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        var px = new Color[16];
        for (int i = 0; i < 16; i++) px[i] = Color.white;
        t.SetPixels(px); t.Apply();
        var (colors, w, h) = ComplexitySampler.Sample(t, new Rect(0, 0, 1, 1));
        Assert.AreEqual(4, w);
        Assert.AreEqual(4, h);
        Assert.AreEqual(16, colors.Length);
    }

    [Test]
    public void Sample_HalfRect_Returns4Pixels()
    {
        var t = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        var px = new Color[16];
        for (int i = 0; i < 16; i++) px[i] = Color.white;
        t.SetPixels(px); t.Apply();
        var (colors, w, h) = ComplexitySampler.Sample(t, new Rect(0, 0, 0.5f, 0.5f));
        Assert.AreEqual(2, w);
        Assert.AreEqual(2, h);
    }
}
