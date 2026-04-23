using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto;
using Narazaka.VRChat.Jnto.Editor.Resolution;

public class SettingsResolverTests
{
    GameObject _root;

    [TearDown] public void Cleanup() { if (_root) Object.DestroyImmediate(_root); }

    [Test]
    public void RootOnly_UsesRootValues()
    {
        _root = new GameObject("root");
        var root = _root.AddComponent<TextureOptimizer>();
        root.Mode = TextureOptimizerMode.Root;
        root.Preset = new QualityPresetOverride { HasValue = true, Value = QualityPreset.High };
        root.ViewDistanceCm = new FloatOverride { HasValue = true, Value = 20f };
        var child = new GameObject("child"); child.transform.SetParent(_root.transform);

        var r = SettingsResolver.Resolve(child.transform);
        Assert.AreEqual(QualityPreset.High, r.Preset);
        Assert.AreEqual(20f, r.ViewDistanceCm);
    }

    [Test]
    public void Override_OverridesAncestorForDescendants()
    {
        _root = new GameObject("root");
        var rootComp = _root.AddComponent<TextureOptimizer>();
        rootComp.Preset = new QualityPresetOverride { HasValue = true, Value = QualityPreset.Low };
        rootComp.ViewDistanceCm = new FloatOverride { HasValue = true, Value = 30f };

        var mid = new GameObject("mid"); mid.transform.SetParent(_root.transform);
        var ov = mid.AddComponent<TextureOptimizer>();
        ov.Mode = TextureOptimizerMode.Override;
        ov.Preset = new QualityPresetOverride { HasValue = true, Value = QualityPreset.Ultra };
        ov.ViewDistanceCm = new FloatOverride { HasValue = false };

        var leaf = new GameObject("leaf"); leaf.transform.SetParent(mid.transform);

        var r = SettingsResolver.Resolve(leaf.transform);
        Assert.AreEqual(QualityPreset.Ultra, r.Preset);
        Assert.AreEqual(30f, r.ViewDistanceCm);
    }

    [Test]
    public void NoAncestorOptimizer_ReturnsNull()
    {
        _root = new GameObject("root");
        var r = SettingsResolver.Resolve(_root.transform);
        Assert.IsNull(r);
    }

    [Test]
    public void Resolve_OverridesNewFields()
    {
        _root = new GameObject("root");
        var opt = _root.AddComponent<TextureOptimizer>();
        opt.HMDPixelsPerDegree = new FloatOverride { HasValue = true, Value = 25f };
        opt.EncodePolicy = new EncodePolicyOverride { HasValue = true, Value = EncodePolicy.Fast };
        opt.Cache = new CacheModeOverride { HasValue = true, Value = CacheMode.Compact };
        opt.DebugDumpPath = "Library/JntoDebug/";

        var r = SettingsResolver.Resolve(_root.transform);

        Assert.AreEqual(25f, r.HMDPixelsPerDegree);
        Assert.AreEqual(EncodePolicy.Fast, r.EncodePolicy);
        Assert.AreEqual(CacheMode.Compact, r.CacheMode);
        Assert.AreEqual("Library/JntoDebug/", r.DebugDumpPath);
    }

    [Test]
    public void Resolve_NewFieldsDefault_WhenNoOverride()
    {
        _root = new GameObject("root");
        _root.AddComponent<TextureOptimizer>();

        var r = SettingsResolver.Resolve(_root.transform);

        Assert.AreEqual(20f, r.HMDPixelsPerDegree);
        Assert.AreEqual(EncodePolicy.Safe, r.EncodePolicy);
        Assert.AreEqual(CacheMode.Full, r.CacheMode);
        Assert.AreEqual("", r.DebugDumpPath);
    }

    [Test]
    public void Resolve_ChildOverridesRoot_ForNewFields()
    {
        _root = new GameObject("root");
        var rootOpt = _root.AddComponent<TextureOptimizer>();
        rootOpt.HMDPixelsPerDegree = new FloatOverride { HasValue = true, Value = 20f };
        rootOpt.EncodePolicy = new EncodePolicyOverride { HasValue = true, Value = EncodePolicy.Safe };

        var child = new GameObject("child");
        child.transform.parent = _root.transform;
        var childOpt = child.AddComponent<TextureOptimizer>();
        childOpt.HMDPixelsPerDegree = new FloatOverride { HasValue = true, Value = 30f };
        childOpt.EncodePolicy = new EncodePolicyOverride { HasValue = true, Value = EncodePolicy.Fast };

        var r = SettingsResolver.Resolve(child.transform);

        Assert.AreEqual(30f, r.HMDPixelsPerDegree);
        Assert.AreEqual(EncodePolicy.Fast, r.EncodePolicy);
    }

    [Test]
    public void Resolve_EnableChromaDrift_DefaultTrue()
    {
        _root = new GameObject("root");
        _root.AddComponent<TextureOptimizer>();

        var r = SettingsResolver.Resolve(_root.transform);
        Assert.IsTrue(r.EnableChromaDrift);
    }

    [Test]
    public void Resolve_EnableChromaDrift_OverrideToFalse()
    {
        _root = new GameObject("root");
        var opt = _root.AddComponent<TextureOptimizer>();
        opt.EnableChromaDrift = new BoolOverride { HasValue = true, Value = false };

        var r = SettingsResolver.Resolve(_root.transform);
        Assert.IsFalse(r.EnableChromaDrift);
    }

    [Test]
    public void Resolve_EnableChromaDrift_ChildOverridesParent()
    {
        _root = new GameObject("root");
        var rootOpt = _root.AddComponent<TextureOptimizer>();
        rootOpt.EnableChromaDrift = new BoolOverride { HasValue = true, Value = true };

        var child = new GameObject("child");
        child.transform.parent = _root.transform;
        var childOpt = child.AddComponent<TextureOptimizer>();
        childOpt.EnableChromaDrift = new BoolOverride { HasValue = true, Value = false };

        var r = SettingsResolver.Resolve(child.transform);
        Assert.IsFalse(r.EnableChromaDrift);
    }
}
