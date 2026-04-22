using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.GpuPipeline;

public class GpuTextureContextTests
{
    [Test]
    public void FromTexture2D_CreatesRtWithMipmaps()
    {
        var src = new Texture2D(64, 64, TextureFormat.RGBA32, false);
        var px = new Color[64 * 64];
        for (int i = 0; i < px.Length; i++) px[i] = Color.red;
        src.SetPixels(px);
        src.Apply();

        using (var ctx = GpuTextureContext.FromTexture2D(src))
        {
            Assert.IsNotNull(ctx.Original);
            Assert.AreEqual(64, ctx.Width);
            Assert.AreEqual(64, ctx.Height);
            Assert.IsTrue(ctx.Original.useMipMap);
            Assert.IsTrue(ctx.Original.IsCreated());
        }

        Object.DestroyImmediate(src);
    }

    [Test]
    public void Dispose_ReleasesRT()
    {
        var src = new Texture2D(32, 32, TextureFormat.RGBA32, false);
        src.SetPixel(0, 0, Color.green);
        src.Apply();

        var ctx = GpuTextureContext.FromTexture2D(src);
        var rt = ctx.Original;
        Assert.IsNotNull(rt);

        ctx.Dispose();
        Assert.IsNull(ctx.Original);

        Object.DestroyImmediate(src);
    }

    [Test]
    public void FromTexture2D_NullSrc_Throws()
    {
        Assert.Throws<System.ArgumentNullException>(() =>
        {
            GpuTextureContext.FromTexture2D(null);
        });
    }

    [Test]
    public void RtSize_MatchesSource()
    {
        var src = new Texture2D(128, 64, TextureFormat.RGBA32, false);
        src.Apply();
        using (var ctx = GpuTextureContext.FromTexture2D(src))
        {
            Assert.AreEqual(128, ctx.Original.width);
            Assert.AreEqual(64, ctx.Original.height);
        }
        Object.DestroyImmediate(src);
    }
}
