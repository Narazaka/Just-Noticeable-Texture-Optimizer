using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.GpuPipeline;

/// <summary>
/// バグ#2/#3 回帰防止: NormalMap/SingleChannel role を linear RT で扱い、
/// trilinear filterMode を指定する。
/// </summary>
public class GpuTextureContextLinearTests
{
    [Test]
    public void FromTexture2D_DefaultSRGB_PreservesColorTexture()
    {
        var t = new Texture2D(8, 8, TextureFormat.RGBA32, false);
        var px = new Color[64];
        for (int i = 0; i < px.Length; i++) px[i] = new Color(0.5f, 0.5f, 0.5f, 1f);
        t.SetPixels(px); t.Apply();

        try
        {
            using (var ctx = GpuTextureContext.FromTexture2D(t))
            {
                Assert.IsTrue(ctx.Original.sRGB, "color texture should use sRGB RT");
                Assert.AreEqual(FilterMode.Trilinear, ctx.Original.filterMode,
                    "RT must use trilinear filtering for mipmap blending");
            }
        }
        finally { Object.DestroyImmediate(t); }
    }

    [Test]
    public void FromTexture2D_LinearMode_DoesNotApplyGammaTransform()
    {
        // linear mode で 0.5 を入れた texture を RT に Blit して読み戻すと、
        // sRGB 変換が走らないため元の 0.5 のまま (許容 ±0.02)
        var t = new Texture2D(8, 8, TextureFormat.RGBA32, false);
        var px = new Color[64];
        for (int i = 0; i < px.Length; i++) px[i] = new Color(0.5f, 0.5f, 0.5f, 1f);
        t.SetPixels(px); t.Apply();

        try
        {
            using (var ctx = GpuTextureContext.FromTexture2D(t, isLinear: true))
            {
                Assert.IsFalse(ctx.Original.sRGB, "linear-mode RT must NOT be sRGB");
                Assert.AreEqual(FilterMode.Trilinear, ctx.Original.filterMode);
            }
        }
        finally { Object.DestroyImmediate(t); }
    }

    [Test]
    public void FromTexture2D_SrgbMode_IsSrgb()
    {
        var t = new Texture2D(8, 8, TextureFormat.RGBA32, false);
        var px = new Color[64];
        for (int i = 0; i < px.Length; i++) px[i] = new Color(0.5f, 0.5f, 0.5f, 1f);
        t.SetPixels(px); t.Apply();

        try
        {
            using (var ctx = GpuTextureContext.FromTexture2D(t, isLinear: false))
            {
                Assert.IsTrue(ctx.Original.sRGB, "default mode is sRGB");
                Assert.AreEqual(FilterMode.Trilinear, ctx.Original.filterMode);
            }
        }
        finally { Object.DestroyImmediate(t); }
    }
}
