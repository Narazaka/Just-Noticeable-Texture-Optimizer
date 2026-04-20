using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase1;

public class AlphaStripperTests
{
    [Test]
    public void StripAlpha_ReturnsOpaqueRgbCopy()
    {
        var src = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        src.SetPixels(new [] { new Color(1,0,0,0.3f), new Color(0,1,0,0.5f),
                               new Color(0,0,1,0.1f), new Color(1,1,1,0.9f) });
        src.Apply();

        var dst = AlphaStripper.StripAlpha(src);

        Assert.AreEqual(src.width, dst.width);
        Assert.AreEqual(src.height, dst.height);
        var px = dst.GetPixels();
        foreach (var p in px) Assert.AreEqual(1f, p.a, 0.001f);
        Assert.AreEqual(new Color(1,0,0,1f), px[0]);
    }
}
