using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto;
using Narazaka.VRChat.Jnto.Editor.Phase2.Gate;

public class DegradationCalibrationTests
{
    [Test]
    public void Default_ThresholdsAreMonotonic()
    {
        var c = DegradationCalibration.Default();
        try
        {
            Assert.Greater(c.ThresholdLow, c.ThresholdMedium);
            Assert.Greater(c.ThresholdMedium, c.ThresholdHigh);
            Assert.Greater(c.ThresholdHigh, c.ThresholdUltra);
        }
        finally { Object.DestroyImmediate(c); }
    }

    [Test]
    public void GetThreshold_ReturnsExpectedValuePerPreset()
    {
        var c = DegradationCalibration.Default();
        try
        {
            Assert.AreEqual(c.ThresholdLow, c.GetThreshold(QualityPreset.Low));
            Assert.AreEqual(c.ThresholdMedium, c.GetThreshold(QualityPreset.Medium));
            Assert.AreEqual(c.ThresholdHigh, c.GetThreshold(QualityPreset.High));
            Assert.AreEqual(c.ThresholdUltra, c.GetThreshold(QualityPreset.Ultra));
        }
        finally { Object.DestroyImmediate(c); }
    }

    [Test]
    public void Default_HasPositiveScales()
    {
        var c = DegradationCalibration.Default();
        try
        {
            Assert.Greater(c.MsslBandEnergyScale, 0f);
            Assert.Greater(c.MsslStructureScale, 0f);
            Assert.Greater(c.RidgeScale, 0f);
            Assert.Greater(c.BandingScale, 0f);
            Assert.Greater(c.BlockBoundaryScale, 0f);
            Assert.Greater(c.AlphaQuantScale, 0f);
            Assert.Greater(c.NormalAngleScale, 0f);
        }
        finally { Object.DestroyImmediate(c); }
    }
}
