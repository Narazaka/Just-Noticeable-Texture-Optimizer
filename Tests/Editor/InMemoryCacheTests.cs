using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.Cache;
using Narazaka.VRChat.Jnto.Editor.Phase2.GpuPipeline;

public class InMemoryCacheTests
{
    [Test]
    public void NewCache_IsEmpty()
    {
        using (var c = new InMemoryCache())
        {
            Assert.AreEqual(0, c.Contexts.Count);
        }
    }

    [Test]
    public void CanStoreAndRetrieve_GpuTextureContext()
    {
        var t = new Texture2D(16, 16, TextureFormat.RGBA32, false);
        try
        {
            using (var c = new InMemoryCache())
            {
                var ctx = GpuTextureContext.FromTexture2D(t);
                c.Contexts[t] = ctx;
                Assert.IsTrue(c.Contexts.ContainsKey(t));
                Assert.AreSame(ctx, c.Contexts[t]);
            }
        }
        finally { Object.DestroyImmediate(t); }
    }

    [Test]
    public void Dispose_ReleasesAllContexts()
    {
        var t1 = new Texture2D(16, 16, TextureFormat.RGBA32, false);
        var t2 = new Texture2D(16, 16, TextureFormat.RGBA32, false);
        try
        {
            var ctx1 = GpuTextureContext.FromTexture2D(t1);
            var ctx2 = GpuTextureContext.FromTexture2D(t2);
            var c = new InMemoryCache();
            c.Contexts[t1] = ctx1;
            c.Contexts[t2] = ctx2;
            Assert.IsNotNull(ctx1.Original);
            Assert.IsNotNull(ctx2.Original);

            c.Dispose();

            Assert.IsNull(ctx1.Original, "ctx1 should be released");
            Assert.IsNull(ctx2.Original, "ctx2 should be released");
            Assert.AreEqual(0, c.Contexts.Count);
        }
        finally
        {
            Object.DestroyImmediate(t1);
            Object.DestroyImmediate(t2);
        }
    }
}
