using System;
using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Shared;

public class LilToonPropertyCatalogTests
{
    // Shader 実体を組み立てずに Matches / TryClassify を直接叩ける stub
    sealed class StubExt : ICustomPropertyCatalogExtension
    {
        public Func<Shader, bool> OnMatches;
        public TryClassifyFn OnClassify;
        public delegate bool TryClassifyFn(Shader s, string variant, string prop, out LilToonPropertyInfo info);

        public bool Matches(Shader shader) => OnMatches?.Invoke(shader) ?? false;
        public bool TryClassify(Shader shader, string variantId, string propName, out LilToonPropertyInfo info)
        {
            info = default;
            return OnClassify != null && OnClassify(shader, variantId, propName, out info);
        }
    }

    [TearDown] public void TearDown() => LilToonPropertyCatalog.ResetExtensionsForTests();

    [Test]
    public void RegisterExtension_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => LilToonPropertyCatalog.RegisterExtension(null));
    }

    [Test]
    public void RegisterExtension_IsIdempotent_ForSameInstance()
    {
        var ext = new StubExt();
        LilToonPropertyCatalog.RegisterExtension(ext);
        LilToonPropertyCatalog.RegisterExtension(ext);
        Assert.IsTrue(LilToonPropertyCatalog.UnregisterExtension(ext));
        Assert.IsFalse(LilToonPropertyCatalog.UnregisterExtension(ext));
    }

    [Test]
    public void UnregisterExtension_NotRegistered_ReturnsFalse()
    {
        Assert.IsFalse(LilToonPropertyCatalog.UnregisterExtension(new StubExt()));
    }

    [Test]
    public void TryGet_NullShader_ReturnsFalse()
    {
        Assert.IsFalse(LilToonPropertyCatalog.TryGet(null, "_MainTex", out _));
    }

    [Test]
    public void TryGet_ExtensionClassifies_CoreIsSkipped()
    {
        var expected = new LilToonPropertyInfo(
            Narazaka.VRChat.Jnto.Editor.Phase2.Compression.ShaderUsage.SingleChannel,
            ChannelMask.R,
            "stub/evidence:1");
        var ext = new StubExt {
            OnMatches = _ => true,
            OnClassify = (Shader s, string v, string p, out LilToonPropertyInfo info) => { info = expected; return true; }
        };
        LilToonPropertyCatalog.RegisterExtension(ext);

        var shader = Shader.Find("lilToon");
        if (shader == null) Assert.Ignore("lilToon shader not available");

        Assert.IsTrue(LilToonPropertyCatalog.TryGet(shader, "_AnyProp", out var info));
        Assert.AreEqual(expected.Usage, info.Usage);
        Assert.AreEqual(expected.ReadChannels, info.ReadChannels);
        Assert.AreEqual(expected.EvidenceRef, info.EvidenceRef);
    }

    [Test]
    public void TryGet_ExtensionMatchesButTryClassifyFalse_FallsBackToCoreWithoutTryingOtherExt()
    {
        int extBCalled = 0;
        var extA = new StubExt { OnMatches = _ => true, OnClassify = (Shader s, string v, string p, out LilToonPropertyInfo i) => { i = default; return false; } };
        var extB = new StubExt {
            OnMatches = _ => { extBCalled++; return true; },
            OnClassify = (Shader s, string v, string p, out LilToonPropertyInfo i) => {
                i = new LilToonPropertyInfo(Narazaka.VRChat.Jnto.Editor.Phase2.Compression.ShaderUsage.Color, ChannelMask.RGBA, "B");
                return true;
            }
        };
        LilToonPropertyCatalog.RegisterExtension(extA);
        LilToonPropertyCatalog.RegisterExtension(extB);

        var shader = Shader.Find("lilToon");
        if (shader == null) Assert.Ignore("lilToon shader not available");

        // core catalog が空 (Task 4 段階) なので最終的に false が返る
        Assert.IsFalse(LilToonPropertyCatalog.TryGet(shader, "_AnyProp", out _));
        Assert.AreEqual(0, extBCalled, "extA が Matches=true かつ classify=false なら extB は呼ばれない");
    }

    [Test]
    public void TryGet_ExtensionMatchesFalse_TriesNextExtension()
    {
        var calledB = 0;
        var extA = new StubExt { OnMatches = _ => false };
        var extB = new StubExt {
            OnMatches = _ => { calledB++; return true; },
            OnClassify = (Shader s, string v, string p, out LilToonPropertyInfo i) => {
                i = new LilToonPropertyInfo(Narazaka.VRChat.Jnto.Editor.Phase2.Compression.ShaderUsage.Color, ChannelMask.RGBA, "B");
                return true;
            }
        };
        LilToonPropertyCatalog.RegisterExtension(extA);
        LilToonPropertyCatalog.RegisterExtension(extB);

        var shader = Shader.Find("lilToon");
        if (shader == null) Assert.Ignore("lilToon shader not available");

        Assert.IsTrue(LilToonPropertyCatalog.TryGet(shader, "_AnyProp", out var info));
        Assert.AreEqual("B", info.EvidenceRef);
        Assert.AreEqual(1, calledB);
    }
}
