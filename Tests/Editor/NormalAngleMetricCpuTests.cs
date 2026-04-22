using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.Degradation;

public class NormalAngleMetricCpuTests
{
    [Test]
    public void Identical_ReturnsZero()
    {
        var t = MakeUpNormal(64);
        var m = new NormalAngleMetric();
        Assert.AreEqual(0f, m.Evaluate(t, t), 0.001f);
    }

    [Test]
    public void TiltedNormals_ReturnsPositive()
    {
        var a = MakeUpNormal(64);
        var b = MakeTiltedNormal(64, 0.3f);
        var m = new NormalAngleMetric();
        float s = m.Evaluate(a, b);
        Assert.Greater(s, 0.01f);
    }

    [Test]
    public void EvaluatePerPixel_ReturnsCorrectSize()
    {
        var a = MakeUpNormal(32);
        var b = MakeTiltedNormal(32, 0.2f);
        var m = new NormalAngleMetric();
        float[] pp = m.EvaluatePerPixel(a, b);
        Assert.IsNotNull(pp);
        Assert.AreEqual(32 * 32, pp.Length);
    }

    [Test]
    public void PerpendicularNormals_ReturnsHigh()
    {
        var a = MakeUpNormal(64);
        var b = MakeFlatNormal(64);
        var m = new NormalAngleMetric();
        float s = m.Evaluate(a, b);
        Assert.Greater(s, 0.5f, "90-degree deviation should score high");
    }

    static Texture2D MakeUpNormal(int n)
    {
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
        var px = new Color[n * n];
        for (int i = 0; i < px.Length; i++) px[i] = new Color(0.5f, 0.5f, 1f, 1f);
        t.SetPixels(px); t.Apply(); return t;
    }

    static Texture2D MakeTiltedNormal(int n, float tilt)
    {
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
        var px = new Color[n * n];
        float encoded = 0.5f + tilt * 0.5f;
        for (int i = 0; i < px.Length; i++) px[i] = new Color(encoded, 0.5f, 0.8f, 1f);
        t.SetPixels(px); t.Apply(); return t;
    }

    static Texture2D MakeFlatNormal(int n)
    {
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
        var px = new Color[n * n];
        for (int i = 0; i < px.Length; i++) px[i] = new Color(1f, 0.5f, 0.5f, 1f);
        t.SetPixels(px); t.Apply(); return t;
    }
}
