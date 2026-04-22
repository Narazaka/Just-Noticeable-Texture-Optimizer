using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.Degradation;

public class ChromaDriftMetricTests
{
    [Test]
    public void Identical_ReturnsZero()
    {
        var t = MakeSolid(64, new Color(0.5f, 0.3f, 0.7f));
        var m = new ChromaDriftMetric();
        Assert.AreEqual(0f, m.Evaluate(t, t), 0.001f);
    }

    [Test]
    public void ColorShifted_ReturnsPositive()
    {
        var a = MakeSolid(64, new Color(0.5f, 0.3f, 0.2f));
        var b = MakeSolid(64, new Color(0.8f, 0.3f, 0.2f));
        var m = new ChromaDriftMetric();
        float s = m.Evaluate(a, b);
        Assert.Greater(s, 0.01f);
    }

    [Test]
    public void SingleOutlierPixel_DoesNotDominate()
    {
        int n = 64;
        var px = new Color[n * n];
        for (int i = 0; i < px.Length; i++) px[i] = new Color(0.5f, 0.5f, 0.5f);
        var a = new Texture2D(n, n, TextureFormat.RGBA32, false);
        a.SetPixels(px); a.Apply();

        var bPx = (Color[])px.Clone();
        bPx[0] = new Color(1f, 0f, 0f);
        var b = new Texture2D(n, n, TextureFormat.RGBA32, false);
        b.SetPixels(bPx); b.Apply();

        var m = new ChromaDriftMetric();
        float s = m.Evaluate(a, b);
        Assert.Less(s, 0.05f, "99th percentile should not be dominated by single outlier");
    }

    static Texture2D MakeSolid(int n, Color c)
    {
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
        var px = new Color[n * n];
        for (int i = 0; i < px.Length; i++) px[i] = c;
        t.SetPixels(px); t.Apply(); return t;
    }
}
