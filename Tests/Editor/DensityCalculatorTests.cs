using NUnit.Framework;
using Narazaka.VRChat.Jnto;
using Narazaka.VRChat.Jnto.Editor.Phase2;

public class DensityCalculatorTests
{
    [Test]
    public void TargetSize_Medium_30cm_Weight1_UV1m2_World1m2_Returns_512()
    {
        var stats = new MeshDensityStats { UvArea = 1.0f, WorldArea = 10000f, BoneWeightAverage = 1f };
        var s = DensityCalculator.ComputeTargetSize(stats, QualityPreset.Medium, viewDistanceCm: 30f, complexityFactor: 1f);
        Assert.AreEqual(512, s);
    }

    [Test]
    public void TargetSize_MinimumFloor_Is32()
    {
        var stats = new MeshDensityStats { UvArea = 1.0f, WorldArea = 1f, BoneWeightAverage = 0.3f };
        var s = DensityCalculator.ComputeTargetSize(stats, QualityPreset.Low, viewDistanceCm: 100f, complexityFactor: 0.5f);
        Assert.AreEqual(32, s);
    }
}
