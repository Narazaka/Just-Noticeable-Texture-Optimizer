using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.Degradation;

public class HighFrequencyMetricTests
{
    [Test]
    public void Identical_ReturnsZero()
    {
        var t = MakeCheckerboard(64);
        var m = new HighFrequencyMetric();
        Assert.AreEqual(0f, m.Evaluate(t, t), 0.001f);
    }

    [Test]
    public void Blurred_ReturnsHigh()
    {
        var a = MakeCheckerboard(64);
        var b = Blur(a);
        var m = new HighFrequencyMetric();
        float s = m.Evaluate(a, b);
        Assert.Greater(s, 0.1f);
    }

    [Test]
    public void SlightNoise_OnDetailedTexture_ReturnsSmall()
    {
        var a = MakeCheckerboard(64);
        var b = AddNoise(a, 0.01f);
        var m = new HighFrequencyMetric();
        float s = m.Evaluate(a, b);
        Assert.Less(s, 0.5f, "Small noise on high-detail texture should not score high");
    }

    static Texture2D MakeCheckerboard(int n)
    {
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
        var px = new Color[n * n];
        for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            px[y * n + x] = (((x >> 1) + (y >> 1)) & 1) == 0 ? Color.black : Color.white;
        t.SetPixels(px); t.Apply(); return t;
    }

    static Texture2D MakeGradient(int n)
    {
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
        var px = new Color[n * n];
        for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            px[y * n + x] = new Color(x / (float)n, y / (float)n, 0.5f, 1f);
        t.SetPixels(px); t.Apply(); return t;
    }

    static Texture2D Blur(Texture2D src)
    {
        int w = src.width, h = src.height;
        var p = src.GetPixels();
        var q = new Color[p.Length];
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
        {
            Color sum = Color.black; int n = 0;
            for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
            {
                int xx = x + dx, yy = y + dy;
                if (xx < 0 || yy < 0 || xx >= w || yy >= h) continue;
                sum += p[yy * w + xx]; n++;
            }
            q[y * w + x] = sum / n;
        }
        var dst = new Texture2D(w, h, TextureFormat.RGBA32, false);
        dst.SetPixels(q); dst.Apply(); return dst;
    }

    static Texture2D AddNoise(Texture2D src, float amount)
    {
        var px = src.GetPixels();
        for (int i = 0; i < px.Length; i++)
            px[i] = new Color(
                Mathf.Clamp01(px[i].r + Random.Range(-amount, amount)),
                Mathf.Clamp01(px[i].g + Random.Range(-amount, amount)),
                Mathf.Clamp01(px[i].b + Random.Range(-amount, amount)), 1f);
        var t = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
        t.SetPixels(px); t.Apply(); return t;
    }
}
