using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto;
using Narazaka.VRChat.Jnto.Editor.Phase2.Gate;
using Narazaka.VRChat.Jnto.Editor.Phase2.Tiling;

public class PerceptualGateTests
{
    class FakeMetric : IMetric
    {
        public string Name { get; set; }
        public MetricContext Context => MetricContext.Both;
        public float[] Output;
        public void Evaluate(RenderTexture o, RenderTexture c, UvTileGrid g, float[] r,
                             DegradationCalibration calib, float[] scores)
        {
            System.Array.Copy(Output, scores, scores.Length);
        }
    }

    [Test]
    public void MaxCombination_LargestScoreWins()
    {
        var grid = UvTileGrid.Create(128, 128);
        for (int i = 0; i < grid.Tiles.Length; i++)
            grid.Tiles[i] = new TileStats { HasCoverage = true };

        var a = new FakeMetric { Name = "A", Output = Filled(grid.Tiles.Length, 0.3f) };
        var b = new FakeMetric { Name = "B", Output = Filled(grid.Tiles.Length, 0.8f) };

        var calib = DegradationCalibration.Default();
        try
        {
            var gate = new PerceptualGate(calib);
            var verdict = gate.Evaluate(null, null, grid, new float[grid.Tiles.Length],
                QualityPreset.Medium, new IMetric[] { a, b });

            Assert.AreEqual(0.8f, verdict.TextureScore, 0.001f);
            Assert.AreEqual("B", verdict.DominantMetric);
            // Medium 既定 1.0 より小さければ pass
            Assert.IsTrue(verdict.Pass == (0.8f < calib.ThresholdMedium));
        }
        finally { Object.DestroyImmediate(calib); }
    }

    [Test]
    public void NoCoverage_TilesIgnored()
    {
        var grid = UvTileGrid.Create(128, 128);
        // Only first tile is covered
        grid.Tiles[0] = new TileStats { HasCoverage = true };

        var huge = new FakeMetric
        {
            Name = "X",
            Output = Filled(grid.Tiles.Length, 10f),
        };
        huge.Output[0] = 0.1f;

        var calib = DegradationCalibration.Default();
        try
        {
            var gate = new PerceptualGate(calib);
            var verdict = gate.Evaluate(null, null, grid, new float[grid.Tiles.Length],
                QualityPreset.Medium, new IMetric[] { huge });

            Assert.AreEqual(0.1f, verdict.TextureScore, 0.001f);
            Assert.AreEqual(0, verdict.WorstTileIndex);
            Assert.AreEqual("X", verdict.DominantMetric);
        }
        finally { Object.DestroyImmediate(calib); }
    }

    [Test]
    public void AllZero_PassWithNullMetric()
    {
        var grid = UvTileGrid.Create(128, 128);
        for (int i = 0; i < grid.Tiles.Length; i++)
            grid.Tiles[i] = new TileStats { HasCoverage = true };

        var zero = new FakeMetric { Name = "Z", Output = Filled(grid.Tiles.Length, 0f) };

        var calib = DegradationCalibration.Default();
        try
        {
            var gate = new PerceptualGate(calib);
            var verdict = gate.Evaluate(null, null, grid, new float[grid.Tiles.Length],
                QualityPreset.Medium, new IMetric[] { zero });

            Assert.AreEqual(0f, verdict.TextureScore);
            Assert.IsTrue(verdict.Pass);
            Assert.AreEqual(-1, verdict.WorstTileIndex,
                "no covered tile has positive score → worstIdx should be -1");
            Assert.IsNull(verdict.DominantMetric);
        }
        finally { Object.DestroyImmediate(calib); }
    }

    [Test]
    public void HigherPreset_StricterThreshold()
    {
        var grid = UvTileGrid.Create(128, 128);
        for (int i = 0; i < grid.Tiles.Length; i++)
            grid.Tiles[i] = new TileStats { HasCoverage = true };

        var middling = new FakeMetric { Name = "M", Output = Filled(grid.Tiles.Length, 0.6f) };
        var calib = DegradationCalibration.Default();
        try
        {
            var gate = new PerceptualGate(calib);
            var medium = gate.Evaluate(null, null, grid, new float[grid.Tiles.Length],
                QualityPreset.Medium, new IMetric[] { middling });
            var ultra = gate.Evaluate(null, null, grid, new float[grid.Tiles.Length],
                QualityPreset.Ultra, new IMetric[] { middling });

            // Both should report same score
            Assert.AreEqual(medium.TextureScore, ultra.TextureScore, 0.001f);
            // But Ultra 0.5 < 0.6, so fails; Medium 1.0 > 0.6 passes
            Assert.IsTrue(medium.Pass);
            Assert.IsFalse(ultra.Pass);
        }
        finally { Object.DestroyImmediate(calib); }
    }

    static float[] Filled(int n, float v)
    {
        var r = new float[n];
        for (int i = 0; i < n; i++) r[i] = v;
        return r;
    }
}
