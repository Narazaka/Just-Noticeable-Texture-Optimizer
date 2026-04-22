using NUnit.Framework;
using Narazaka.VRChat.Jnto.Editor.Phase2.Compression;

public class BinarySearchStrategyTests
{
    [Test]
    public void FindsBoundary_At_512()
    {
        int calls = 0;
        bool Probe(int size) { calls++; return size >= 512; }

        int result = BinarySearchStrategy.FindMinPassSize(4096, 32, Probe);
        Assert.AreEqual(512, result);
        Assert.LessOrEqual(calls, 4, "should use <= log2(8 candidates) calls");
    }

    [Test]
    public void AllFail_ReturnsOrigSize()
    {
        int result = BinarySearchStrategy.FindMinPassSize(4096, 32, _ => false);
        Assert.AreEqual(4096, result);
    }

    [Test]
    public void AllPass_ReturnsMinSize()
    {
        int result = BinarySearchStrategy.FindMinPassSize(4096, 32, _ => true);
        Assert.AreEqual(32, result);
    }

    [Test]
    public void OnlyOrigPasses_ReturnsOrigSize()
    {
        int result = BinarySearchStrategy.FindMinPassSize(2048, 32, size => size == 2048);
        Assert.AreEqual(2048, result);
    }

    [Test]
    public void FindsMinSize_When_OnlyMinPasses()
    {
        int result = BinarySearchStrategy.FindMinPassSize(4096, 32, size => size == 32);
        // bestPass は最小 pass = 32
        Assert.AreEqual(32, result);
    }

    [Test]
    public void SmallRange_Works()
    {
        // origSize と minSize が同じ
        int calls = 0;
        bool Probe(int s) { calls++; return true; }
        int result = BinarySearchStrategy.FindMinPassSize(64, 64, Probe);
        Assert.AreEqual(64, result);
        Assert.AreEqual(1, calls, "single candidate → 1 probe");
    }
}
