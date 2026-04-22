using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.Degradation;

public class SsimMetricTests
{
    [Test]
    public void Identical_ReturnsZero()
    {
        var t = MakeGradient(64);
        var m = new SsimMetric();
        Assert.AreEqual(0f, m.Evaluate(t, t), 0.01f);
    }

    [Test]
    public void Blurred_ReturnsPositiveDegradation()
    {
        var a = MakeCheckerboard(64);
        var b = Blur(a);
        var m = new SsimMetric();
        float s = m.Evaluate(a, b);
        Assert.Greater(s, 0.001f, "Blurred image should show degradation");
        Assert.Less(s, 1f);
    }

    [Test]
    public void Inverted_ReturnsHighDegradation()
    {
        var a = MakeGradient(64);
        var b = Invert(a);
        var m = new SsimMetric();
        float s = m.Evaluate(a, b);
        Assert.Greater(s, 0.3f, "Inverted image should show high degradation");
    }

    static Texture2D MakeGradient(int n)
    {
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
        var px = new Color[n * n];
        for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            px[y * n + x] = new Color(x / (float)n, y / (float)n, 0.5f, 1f);
        t.SetPixels(px); t.Apply(); return t;
    }

    static Texture2D MakeCheckerboard(int n)
    {
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
        var px = new Color[n * n];
        for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            px[y * n + x] = (((x >> 1) + (y >> 1)) & 1) == 0 ? Color.black : Color.white;
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

    static Texture2D Invert(Texture2D src)
    {
        var px = src.GetPixels();
        for (int i = 0; i < px.Length; i++)
            px[i] = new Color(1f - px[i].r, 1f - px[i].g, 1f - px[i].b, 1f);
        var t = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
        t.SetPixels(px); t.Apply(); return t;
    }
}
