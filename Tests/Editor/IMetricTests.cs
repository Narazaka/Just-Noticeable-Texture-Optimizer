using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.Gate;
using Narazaka.VRChat.Jnto.Editor.Phase2.Tiling;

public class IMetricTests
{
    class FakeMetric : IMetric
    {
        public string Name => "Fake";
        public MetricContext Context => MetricContext.Both;
        public void Evaluate(RenderTexture o, RenderTexture c, UvTileGrid g, float[] r, DegradationCalibration calib, float[] scores)
        {
            for (int i = 0; i < scores.Length; i++) scores[i] = 0.5f;
        }
    }

    [Test]
    public void IMetric_CanBeImplemented()
    {
        IMetric m = new FakeMetric();
        Assert.AreEqual("Fake", m.Name);
        Assert.AreEqual(MetricContext.Both, m.Context);
    }

    [Test]
    public void IMetric_Evaluate_FillsScoresArray()
    {
        IMetric m = new FakeMetric();
        var grid = UvTileGrid.Create(64, 64);
        var r = new float[grid.Tiles.Length];
        var scores = new float[grid.Tiles.Length];
        var calib = DegradationCalibration.Default();
        try
        {
            m.Evaluate(null, null, grid, r, calib, scores);
            Assert.AreEqual(0.5f, scores[0]);
            Assert.AreEqual(0.5f, scores[scores.Length - 1]);
        }
        finally { Object.DestroyImmediate(calib); }
    }

    [Test]
    public void MetricContext_HasExpectedValues()
    {
        Assert.AreEqual(0, (int)MetricContext.Downscale);
        Assert.AreEqual(1, (int)MetricContext.Compression);
        Assert.AreEqual(2, (int)MetricContext.Both);
    }
}
