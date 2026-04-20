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
}
