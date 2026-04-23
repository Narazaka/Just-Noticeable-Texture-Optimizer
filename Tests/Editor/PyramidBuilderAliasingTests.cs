using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.GpuPipeline;

/// <summary>
/// Tier 2 検出: PyramidBuilder の Graphics.Blit による大幅縮小が aliasing を生んでいないか。
/// 4K のチェッカー (周期 2) を 1K に downsample して、期待 (テクセル平均 = 0.5) からの
/// RMS を測定。0.05 を超えれば aliasing あり (bilinear が 4 テクセル平均なので 4K→1K で
/// sample が「疎」になり moire が出る)。
/// </summary>
public class PyramidBuilderAliasingTests
{
    [Test]
    public void Downsample_4KCheckerboard_StaysNearHalfGray()
    {
        const int src = 4096;
        const int dst = 1024;
        var tex = MakeCheckerboard(src);
        RenderTexture rt = null;
        Texture2D read = null;
        try
        {
            rt = PyramidBuilder.CreatePyramid(tex, dst, dst, "jnto_alias_test");
            read = ReadRt(rt);

            float sumSq = 0f;
            int n = 0;
            var px = read.GetPixels();
            for (int i = 0; i < px.Length; i++)
            {
                float d = px[i].r - 0.5f;
                sumSq += d * d;
                n++;
            }
            float rms = Mathf.Sqrt(sumSq / n);
            UnityEngine.Debug.Log($"[JNTO/Alias] 4K→1K checker downsample RMS = {rms:F4}");
            Assert.Less(rms, 0.05f,
                "4K checker downsampled to 1K must converge to 0.5 gray; large RMS indicates aliasing");
        }
        finally
        {
            if (rt != null) { rt.Release(); Object.DestroyImmediate(rt); }
            if (read != null) Object.DestroyImmediate(read);
            Object.DestroyImmediate(tex);
        }
    }

    static Texture2D MakeCheckerboard(int n)
    {
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            float v = ((x ^ y) & 1) == 0 ? 0f : 1f;
            px[y * n + x] = new Color(v, v, v, 1f);
        }
        t.SetPixels(px);
        t.Apply();
        return t;
    }

    static Texture2D ReadRt(RenderTexture rt)
    {
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        return tex;
    }
}
