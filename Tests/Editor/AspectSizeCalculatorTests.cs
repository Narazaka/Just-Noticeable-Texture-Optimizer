using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.Compression;

public class AspectSizeCalculatorTests
{
    [Test]
    public void Square_PreservesAspect()
    {
        var r = AspectSizeCalculator.Compute(512, 512, 256);
        Assert.AreEqual(256, r.width);
        Assert.AreEqual(256, r.height);
    }

    [Test]
    public void Landscape_PreservesAspect()
    {
        var r = AspectSizeCalculator.Compute(1024, 512, 512);
        Assert.AreEqual(512, r.width);
        Assert.AreEqual(256, r.height);
    }

    [Test]
    public void Portrait_PreservesAspect()
    {
        var r = AspectSizeCalculator.Compute(512, 1024, 512);
        Assert.AreEqual(256, r.width);
        Assert.AreEqual(512, r.height);
    }

    [Test]
    public void NonPowerOfTwo_RoundsToMultipleOf4()
    {
        // 1000 × 700, target=500 → exact min-dim = round(500 * 700 / 1000) = 350.
        // 350 is not a multiple of 4 → rounds up to 352.
        // Verifies RoundToMultipleOf4 actually runs (would fail if removed: exact = 350).
        var r = AspectSizeCalculator.Compute(1000, 700, 500);
        Assert.AreEqual(500, r.width, "target dimension is used as-is");
        Assert.AreEqual(352, r.height,
            "350 must round up to 352 (next multiple of 4)");
        Assert.AreEqual(0, r.height % 4, "height is multiple of 4");
    }

    [Test]
    public void Tiny_ClampsToFour()
    {
        // extremely skewed aspect, min side should clamp at 4
        var r = AspectSizeCalculator.Compute(8192, 16, 256);
        Assert.AreEqual(256, r.width);
        Assert.AreEqual(4, r.height, "min side clamps at 4 (compressed-format minimum)");
    }

    [Test]
    public void Enumerate_And_Encode_ProduceSameSize_NonSquare()
    {
        // 1200 × 800 non-square, target=600 → enumerator (600, 400). Encode must produce the same.
        const int origW = 1200, origH = 800, targetMaxDim = 600;
        var (expW, expH) = AspectSizeCalculator.Compute(origW, origH, targetMaxDim);

        var src = new Texture2D(origW, origH, TextureFormat.RGBA32, false);
        try
        {
            var resized = ResolutionReducer.ResizeToSize(src, expW, expH, false);
            try
            {
                Assert.AreEqual(expW, resized.width,
                    "Enumerator-computed width must equal ResizeToSize output width");
                Assert.AreEqual(expH, resized.height,
                    "Enumerator-computed height must equal ResizeToSize output height");
            }
            finally { Object.DestroyImmediate(resized); }
        }
        finally { Object.DestroyImmediate(src); }
    }
}
