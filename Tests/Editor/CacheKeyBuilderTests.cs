using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;
using Narazaka.VRChat.Jnto;
using Narazaka.VRChat.Jnto.Editor.Shared;
using Narazaka.VRChat.Jnto.Editor.Phase2.Cache;
using Narazaka.VRChat.Jnto.Editor.Phase2.Compression;
using Narazaka.VRChat.Jnto.Editor.Resolution;

public class CacheKeyBuilderTests
{
    [Test]
    public void IdenticalInputs_SameKey()
    {
        var t = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        try
        {
            var refs = new List<TextureReference>();
            var s = new ResolvedSettings();

            var k1 = CacheKeyBuilder.Build(t, TextureRole.ColorOpaque, refs, s);
            var k2 = CacheKeyBuilder.Build(t, TextureRole.ColorOpaque, refs, s);
            Assert.AreEqual(k1.Value, k2.Value);
        }
        finally { Object.DestroyImmediate(t); }
    }

    [Test]
    public void DifferentPreset_DifferentKey()
    {
        var t = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        try
        {
            var refs = new List<TextureReference>();
            var s1 = new ResolvedSettings { Preset = QualityPreset.Medium };
            var s2 = new ResolvedSettings { Preset = QualityPreset.High };

            var k1 = CacheKeyBuilder.Build(t, TextureRole.ColorOpaque, refs, s1);
            var k2 = CacheKeyBuilder.Build(t, TextureRole.ColorOpaque, refs, s2);
            Assert.AreNotEqual(k1.Value, k2.Value);
        }
        finally { Object.DestroyImmediate(t); }
    }

    [Test]
    public void DifferentRole_DifferentKey()
    {
        var t = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        try
        {
            var s = new ResolvedSettings();
            var k1 = CacheKeyBuilder.Build(t, TextureRole.ColorOpaque, null, s);
            var k2 = CacheKeyBuilder.Build(t, TextureRole.NormalMap, null, s);
            Assert.AreNotEqual(k1.Value, k2.Value);
        }
        finally { Object.DestroyImmediate(t); }
    }

    [Test]
    public void DifferentViewDistance_DifferentKey()
    {
        var t = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        try
        {
            var s1 = new ResolvedSettings { ViewDistanceCm = 30f };
            var s2 = new ResolvedSettings { ViewDistanceCm = 100f };
            var k1 = CacheKeyBuilder.Build(t, TextureRole.ColorOpaque, null, s1);
            var k2 = CacheKeyBuilder.Build(t, TextureRole.ColorOpaque, null, s2);
            Assert.AreNotEqual(k1.Value, k2.Value);
        }
        finally { Object.DestroyImmediate(t); }
    }

    [Test]
    public void NullSettings_DoesNotThrow()
    {
        var t = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        try
        {
            var k = CacheKeyBuilder.Build(t, TextureRole.ColorOpaque, null, null);
            Assert.AreNotEqual(0UL, k.Value);
        }
        finally { Object.DestroyImmediate(t); }
    }

    [Test]
    public void NullTexture_DoesNotThrow()
    {
        var s = new ResolvedSettings();
        var k = CacheKeyBuilder.Build(null, TextureRole.ColorOpaque, null, s);
        Assert.AreNotEqual(0UL, k.Value);
    }
}
