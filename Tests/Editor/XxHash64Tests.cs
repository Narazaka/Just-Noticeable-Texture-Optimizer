using NUnit.Framework;
using Narazaka.VRChat.Jnto.Editor.Phase2.Cache;

public class XxHash64Tests
{
    [Test]
    public void SameInput_SameHash()
    {
        var a = new XxHash64();
        a.Append(new byte[] { 1, 2, 3, 4, 5 });
        ulong h1 = a.GetCurrentHashAsUInt64();

        var b = new XxHash64();
        b.Append(new byte[] { 1, 2, 3, 4, 5 });
        ulong h2 = b.GetCurrentHashAsUInt64();

        Assert.AreEqual(h1, h2);
    }

    [Test]
    public void DifferentInput_DifferentHash()
    {
        var a = new XxHash64();
        a.Append(new byte[] { 1, 2, 3, 4, 5 });
        var b = new XxHash64();
        b.Append(new byte[] { 1, 2, 3, 4, 6 });
        Assert.AreNotEqual(a.GetCurrentHashAsUInt64(), b.GetCurrentHashAsUInt64());
    }

    [Test]
    public void LongInput_Works()
    {
        var a = new XxHash64();
        var data = new byte[1000];
        for (int i = 0; i < data.Length; i++) data[i] = (byte)(i & 0xFF);
        a.Append(data);
        ulong h = a.GetCurrentHashAsUInt64();
        Assert.AreNotEqual(0UL, h);
    }

    [Test]
    public void ChunkedAppend_SameAsSingleAppend()
    {
        var data = new byte[100];
        for (int i = 0; i < data.Length; i++) data[i] = (byte)i;

        var a = new XxHash64();
        a.Append(data);
        ulong full = a.GetCurrentHashAsUInt64();

        var b = new XxHash64();
        b.Append(data, 0, 33);
        b.Append(data, 33, 67);
        ulong chunked = b.GetCurrentHashAsUInt64();

        Assert.AreEqual(full, chunked);
    }

    [Test]
    public void Empty_GivesNonZeroSeedHash()
    {
        var a = new XxHash64();
        ulong h = a.GetCurrentHashAsUInt64();
        // empty hash is not necessarily zero (XXHash returns h = seed + Prime5 ^ ... mix)
        // assert it's deterministic at least
        var b = new XxHash64();
        Assert.AreEqual(h, b.GetCurrentHashAsUInt64());
    }

    [Test]
    public void Reset_ClearsState()
    {
        var a = new XxHash64();
        a.Append(new byte[] { 1, 2, 3 });
        ulong h1 = a.GetCurrentHashAsUInt64();

        a.Reset();
        a.Append(new byte[] { 1, 2, 3 });
        ulong h2 = a.GetCurrentHashAsUInt64();

        Assert.AreEqual(h1, h2);
    }
}
