using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.GpuPipeline;

public class PyramidBuilderTests
{
    [Test]
    public void CreatePyramid_RtHasMipmapsAndCorrectSize()
    {
        var src = MakeSolid(64, 64, Color.cyan);
        var rt = PyramidBuilder.CreatePyramid(src, 32, 32, "test");
        try
        {
            Assert.IsNotNull(rt);
            Assert.AreEqual(32, rt.width);
            Assert.AreEqual(32, rt.height);
            Assert.IsTrue(rt.useMipMap);
            Assert.AreEqual(FilterMode.Trilinear, rt.filterMode);
            Assert.IsTrue(rt.IsCreated());
        }
        finally
        {
            rt.Release();
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(src);
        }
    }

    [Test]
    public void CreatePyramid_AcceptsRectangle()
    {
        var src = MakeSolid(128, 64, Color.magenta);
        var rt = PyramidBuilder.CreatePyramid(src, 64, 32, "rect");
        try
        {
            Assert.AreEqual(64, rt.width);
            Assert.AreEqual(32, rt.height);
        }
        finally
        {
            rt.Release();
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(src);
        }
    }

    [Test]
    public void MipLevelCount_PowerOfTwo()
    {
        Assert.AreEqual(7, PyramidBuilder.MipLevelCount(64, 64));   // 64,32,16,8,4,2,1 → 7
        Assert.AreEqual(8, PyramidBuilder.MipLevelCount(128, 64));  // max 128 → 8
        Assert.AreEqual(13, PyramidBuilder.MipLevelCount(4096, 4096));
    }

    [Test]
    public void MipLevelCount_NonPowerOfTwo()
    {
        // 100 → 100, 50, 25, 12, 6, 3, 1, 0 → 7 stages until reaching 0
        Assert.AreEqual(7, PyramidBuilder.MipLevelCount(100, 50));
    }

    static Texture2D MakeSolid(int w, int h, Color c)
    {
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var px = new Color[w * h];
        for (int i = 0; i < px.Length; i++) px[i] = c;
        t.SetPixels(px);
        t.Apply();
        return t;
    }
}
