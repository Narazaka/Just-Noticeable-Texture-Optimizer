using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.Reporting;

public class DecisionLogTests
{
    [SetUp]
    public void Setup() { DecisionLog.Clear(); }

    [TearDown]
    public void TearDown() { DecisionLog.Clear(); }

    [Test]
    public void Empty_AfterClear()
    {
        Assert.AreEqual(0, DecisionLog.All.Count);
    }

    [Test]
    public void Add_AppendsRecord()
    {
        DecisionLog.Add(new DecisionRecord
        {
            OrigSize = 4096,
            FinalSize = 2048,
            OrigFormat = TextureFormat.RGBA32,
            FinalFormat = TextureFormat.DXT5,
            TextureScore = 0.5f,
            DominantMetric = "MSSL",
        });
        Assert.AreEqual(1, DecisionLog.All.Count);
        Assert.AreEqual(2048, DecisionLog.All[0].FinalSize);
    }

    [Test]
    public void Format_IncludesAllKeyFields()
    {
        var r = new DecisionRecord
        {
            OrigSize = 4096,
            FinalSize = 2048,
            OrigFormat = TextureFormat.RGBA32,
            FinalFormat = TextureFormat.DXT5,
            SavedBytes = 16 * 1024 * 1024,
            TextureScore = 0.82f,
            DominantMetric = "MSSL",
            DominantMipLevel = 2,
            WorstTileIndex = 342,
            CacheHit = false,
        };
        var line = DecisionLog.Format(r);
        StringAssert.Contains("4096→2048", line);
        StringAssert.Contains("DXT5", line);
        StringAssert.Contains("16.0MB", line);
        StringAssert.Contains("MSSL", line);
        StringAssert.Contains("L2", line);
        StringAssert.Contains("t(342)", line);
    }

    [Test]
    public void Format_CacheHit_AppendsLabel()
    {
        var r = new DecisionRecord
        {
            OrigSize = 1024,
            FinalSize = 1024,
            OrigFormat = TextureFormat.DXT5,
            FinalFormat = TextureFormat.DXT5,
            CacheHit = true,
        };
        var line = DecisionLog.Format(r);
        StringAssert.Contains("(cache hit)", line);
    }

    [Test]
    public void Format_HandlesNullDominantMetric()
    {
        var r = new DecisionRecord
        {
            OrigSize = 256, FinalSize = 256,
            OrigFormat = TextureFormat.RGBA32, FinalFormat = TextureFormat.RGBA32,
            DominantMetric = null,
        };
        var line = DecisionLog.Format(r);
        StringAssert.Contains("dominant=-", line);
    }

    [Test]
    public void Clear_RemovesAll()
    {
        DecisionLog.Add(new DecisionRecord { OrigSize = 1, FinalSize = 1 });
        DecisionLog.Add(new DecisionRecord { OrigSize = 2, FinalSize = 2 });
        Assert.AreEqual(2, DecisionLog.All.Count);
        DecisionLog.Clear();
        Assert.AreEqual(0, DecisionLog.All.Count);
    }
}
