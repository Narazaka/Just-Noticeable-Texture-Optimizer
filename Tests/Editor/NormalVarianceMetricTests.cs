using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.Degradation;

public class NormalVarianceMetricTests
{
    [Test]
    public void Identical_ReturnsZero()
    {
        var t = MakeNoisyNormal(64);
        var m = new NormalVarianceMetric();
        Assert.AreEqual(0f, m.Evaluate(t, t), 0.001f);
    }

    [Test]
    public void Smoothed_ReturnsPositive()
    {
        var a = MakeNoisyNormal(64);
        var b = MakeUpNormal(64);
        var m = new NormalVarianceMetric();
        float s = m.Evaluate(a, b);
        Assert.Greater(s, 0.01f, "Smoothed normals should show variance loss");
    }

    [Test]
    public void EvaluatePerPixel_ReturnsCorrectSize()
    {
        var a = MakeNoisyNormal(32);
        var b = MakeUpNormal(32);
        var m = new NormalVarianceMetric();
        float[] pp = m.EvaluatePerPixel(a, b);
        Assert.IsNotNull(pp);
        Assert.AreEqual(32 * 32, pp.Length);
    }

    static Texture2D MakeNoisyNormal(int n)
    {
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
        var px = new Color[n * n];
        var rng = new System.Random(42);
        for (int i = 0; i < px.Length; i++)
        {
            float nx = 0.5f + (float)(rng.NextDouble() - 0.5) * 0.3f;
            float ny = 0.5f + (float)(rng.NextDouble() - 0.5) * 0.3f;
            px[i] = new Color(nx, ny, 0.9f, 1f);
        }
        t.SetPixels(px); t.Apply(); return t;
    }

    static Texture2D MakeUpNormal(int n)
    {
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
        var px = new Color[n * n];
        for (int i = 0; i < px.Length; i++) px[i] = new Color(0.5f, 0.5f, 1f, 1f);
        t.SetPixels(px); t.Apply(); return t;
    }
}
