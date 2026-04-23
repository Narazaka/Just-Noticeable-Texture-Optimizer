using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2;

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
        // 1500 × 1000, target=750 → (750, 500). Both multiples of 4 (750 rounds up to 752).
        var r = AspectSizeCalculator.Compute(1500, 1000, 750);
        Assert.AreEqual(750, r.width,   "target dimension is used as-is (caller controls POT)");
        Assert.AreEqual(500, r.height,  "shorter dimension rounded to multiple of 4");
        Assert.AreEqual(0, r.width  % 2, "width divisible by 2 (caller-provided)");
        Assert.AreEqual(0, r.height % 4, "height rounded to multiple of 4");
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
