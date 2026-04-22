using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.GpuPipeline;

/// <summary>
/// バグ#2 回帰防止: PyramidBuilder が NormalMap/SingleChannel role 向けに
/// linear RT を生成できること。sRGB flag は isLinear=true で false になる。
/// </summary>
public class PyramidBuilderLinearTests
{
    [Test]
    public void CreatePyramid_DefaultSRGB()
    {
        var src = MakeSolid(8, 8, new Color(0.5f, 0.5f, 0.5f, 1f));
        var rt = PyramidBuilder.CreatePyramid(src, 8, 8, "test_default");
        try
        {
            Assert.IsTrue(rt.sRGB, "default should be sRGB");
            Assert.AreEqual(FilterMode.Trilinear, rt.filterMode);
        }
        finally
        {
            rt.Release();
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(src);
        }
    }

    [Test]
    public void CreatePyramid_LinearMode_DoesNotApplyGamma()
    {
        var src = MakeSolid(8, 8, new Color(0.5f, 0.5f, 0.5f, 1f));
        var rt = PyramidBuilder.CreatePyramid(src, 8, 8, "test_linear", isLinear: true);
        try
        {
            Assert.IsFalse(rt.sRGB, "linear RT must NOT be sRGB");
            Assert.AreEqual(FilterMode.Trilinear, rt.filterMode);
        }
        finally
        {
            rt.Release();
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(src);
        }
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
