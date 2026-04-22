using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.Degradation;

public class FlipMetricTests
{
    [Test]
    public void Identical_ReturnsZero()
    {
        var t = MakeGradient(64);
        var m = new FlipMetric();
        Assert.AreEqual(0f, m.Evaluate(t, t), 0.001f);
    }

    [Test]
    public void ColorShifted_ReturnsPositive()
    {
        var a = MakeGradient(64);
        var b = ShiftRed(a);
        var m = new FlipMetric();
        float s = m.Evaluate(a, b);
        Assert.Greater(s, 0.01f);
    }

    [Test]
    public void EvaluatePerPixel_ReturnsArrayOfCorrectSize()
    {
        var a = MakeGradient(32);
        var b = ShiftRed(a);
        var m = new FlipMetric();
        float[] pp = m.EvaluatePerPixel(a, b);
        Assert.IsNotNull(pp);
        Assert.AreEqual(32 * 32, pp.Length);
    }

    [Test]
    public void EvaluatePerPixel_IdenticalReturnsAllZero()
    {
        var t = MakeGradient(32);
        var m = new FlipMetric();
        float[] pp = m.EvaluatePerPixel(t, t);
        Assert.IsNotNull(pp);
        float max = 0;
        foreach (var v in pp) if (v > max) max = v;
        Assert.AreEqual(0f, max, 0.001f);
    }

    static Texture2D MakeGradient(int n)
    {
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
        var px = new Color[n * n];
        for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            px[y * n + x] = new Color(x / (float)n, y / (float)n, 0.5f, 1f);
        t.SetPixels(px); t.Apply(); return t;
    }

    static Texture2D ShiftRed(Texture2D src)
    {
        var px = src.GetPixels();
        for (int i = 0; i < px.Length; i++)
            px[i] = new Color(Mathf.Clamp01(px[i].r + 0.2f), px[i].g, px[i].b, 1f);
        var t = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
        t.SetPixels(px); t.Apply(); return t;
    }
}
