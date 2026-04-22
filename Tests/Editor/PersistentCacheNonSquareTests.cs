using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto;
using Narazaka.VRChat.Jnto.Editor.Phase2.Cache;

/// <summary>
/// バグ#11 回帰防止: cache に非正方形テクスチャが保存・復元できる。
/// </summary>
public class PersistentCacheNonSquareTests
{
    [SetUp]
    public void Setup() { PersistentCache.ClearAll(); }

    [TearDown]
    public void TearDown() { PersistentCache.ClearAll(); }

    [Test]
    public void Store_RetainsWidthAndHeight_NonSquare()
    {
        var key = new CacheKey { Value = 0xABC123UL };
        var value = new CachedTextureResult
        {
            FinalSize = 1024,    // 旧 API
            FinalWidth = 1024,   // 新 API (バグ 11 修正用に追加されるはず)
            FinalHeight = 512,
            FinalFormatName = "DXT5",
            CompressedRawBytes = new byte[] { 1, 2, 3, 4 },
        };
        PersistentCache.Store(key, value, CacheMode.Full);

        var loaded = PersistentCache.TryLoad(key, CacheMode.Full);
        Assert.IsNotNull(loaded);
        Assert.AreEqual(1024, loaded.FinalWidth);
        Assert.AreEqual(512, loaded.FinalHeight);
        Assert.AreEqual("DXT5", loaded.FinalFormatName);
    }

    [Test]
    public void Square_BackwardCompatible()
    {
        // 旧 FinalSize のみ書かれた cache でも復元可能 (after migration)
        var key = new CacheKey { Value = 0xDEF456UL };
        var value = new CachedTextureResult
        {
            FinalSize = 256,
            FinalWidth = 256,
            FinalHeight = 256,
            FinalFormatName = "BC7",
            CompressedRawBytes = null,
        };
        PersistentCache.Store(key, value, CacheMode.Compact);
        var loaded = PersistentCache.TryLoad(key, CacheMode.Compact);
        Assert.IsNotNull(loaded);
        Assert.AreEqual(256, loaded.FinalWidth);
        Assert.AreEqual(256, loaded.FinalHeight);
    }
}
