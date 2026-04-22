using NUnit.Framework;
using Narazaka.VRChat.Jnto;
using Narazaka.VRChat.Jnto.Editor.Phase2.Cache;

public class PersistentCacheTests
{
    [SetUp]
    public void Setup() { PersistentCache.ClearAll(); }

    [TearDown]
    public void Teardown() { PersistentCache.ClearAll(); }

    [Test]
    public void StoreAndLoad_FullMode_RoundTrip()
    {
        var key = new CacheKey { Value = 0x1234567890abcdefUL };
        var value = new CachedTextureResult
        {
            FinalSize = 2048,
            FinalFormatName = "DXT5",
            CompressedRawBytes = new byte[] { 1, 2, 3, 4, 5 },
        };
        PersistentCache.Store(key, value, CacheMode.Full);

        var loaded = PersistentCache.TryLoad(key, CacheMode.Full);
        Assert.IsNotNull(loaded);
        Assert.AreEqual(2048, loaded.FinalSize);
        Assert.AreEqual("DXT5", loaded.FinalFormatName);
        Assert.IsNotNull(loaded.CompressedRawBytes);
        Assert.AreEqual(5, loaded.CompressedRawBytes.Length);
        Assert.AreEqual(3, loaded.CompressedRawBytes[2]);
    }

    [Test]
    public void StoreAndLoad_CompactMode_NoBytes()
    {
        var key = new CacheKey { Value = 0xdeadbeefUL };
        var value = new CachedTextureResult
        {
            FinalSize = 1024,
            FinalFormatName = "BC7",
            CompressedRawBytes = new byte[] { 1, 2, 3 },
        };
        PersistentCache.Store(key, value, CacheMode.Compact);
        var loaded = PersistentCache.TryLoad(key, CacheMode.Compact);
        Assert.IsNotNull(loaded);
        Assert.AreEqual(1024, loaded.FinalSize);
        Assert.IsTrue(loaded.CompressedRawBytes == null || loaded.CompressedRawBytes.Length == 0,
            "compact mode should not retain raw bytes");
    }

    [Test]
    public void Disabled_DoesNotStore()
    {
        var key = new CacheKey { Value = 0xabcUL };
        var value = new CachedTextureResult { FinalSize = 512, FinalFormatName = "DXT1" };
        PersistentCache.Store(key, value, CacheMode.Disabled);
        Assert.IsNull(PersistentCache.TryLoad(key, CacheMode.Full));
    }

    [Test]
    public void TryLoad_Disabled_ReturnsNull()
    {
        var key = new CacheKey { Value = 0xabcUL };
        var value = new CachedTextureResult { FinalSize = 512, FinalFormatName = "DXT1" };
        PersistentCache.Store(key, value, CacheMode.Full);
        Assert.IsNull(PersistentCache.TryLoad(key, CacheMode.Disabled));
    }

    [Test]
    public void TryLoad_NonexistentKey_ReturnsNull()
    {
        var key = new CacheKey { Value = 0xfedcUL };
        Assert.IsNull(PersistentCache.TryLoad(key, CacheMode.Full));
    }

    [Test]
    public void Store_PreservesOriginalValueState()
    {
        // Store does not clobber the input value's CompressedRawBytes after returning
        var key = new CacheKey { Value = 1UL };
        var value = new CachedTextureResult
        {
            FinalSize = 256,
            FinalFormatName = "DXT5",
            CompressedRawBytes = new byte[] { 9, 8, 7 },
        };
        PersistentCache.Store(key, value, CacheMode.Full);
        Assert.IsNotNull(value.CompressedRawBytes,
            "Store must restore CompressedRawBytes on the input object after writing");
        Assert.AreEqual(3, value.CompressedRawBytes.Length);
    }
}
