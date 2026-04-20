using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase1;

public class LilToonAlphaRulesTests
{
    [Test]
    public void BumpMap_NeverUsesAlpha()
    {
        var mat = CreateLilToon();
        Assert.IsFalse(LilToonAlphaRules.IsAlphaUsed(mat, "_BumpMap"));
    }

    [Test]
    public void AlphaMask_AlwaysUsesAlpha()
    {
        var mat = CreateLilToon();
        Assert.IsTrue(LilToonAlphaRules.IsAlphaUsed(mat, "_AlphaMask"));
    }

    [Test]
    public void MainTex_OpaqueMode_DoesNotUseAlpha()
    {
        var mat = CreateLilToon();
        mat.SetFloat("_TransparentMode", 0f);
        mat.renderQueue = 2000;
        Assert.IsFalse(LilToonAlphaRules.IsAlphaUsed(mat, "_MainTex"));
    }

    [Test]
    public void MainTex_CutoutMode_UsesAlpha()
    {
        var mat = CreateLilToon();
        mat.SetFloat("_TransparentMode", 1f);
        Assert.IsTrue(LilToonAlphaRules.IsAlphaUsed(mat, "_MainTex"));
    }

    [Test]
    public void MatCap_DoesNotUseAlpha()
    {
        var mat = CreateLilToon();
        Assert.IsFalse(LilToonAlphaRules.IsAlphaUsed(mat, "_MatCapTex"));
    }

    [Test]
    public void UnknownProperty_ConservativeTrue()
    {
        var mat = CreateLilToon();
        Assert.IsTrue(LilToonAlphaRules.IsAlphaUsed(mat, "_MadeUpTex"));
    }

    static Material CreateLilToon()
    {
        var sh = Shader.Find("lilToon");
        if (sh == null) Assert.Ignore("lilToon not installed");
        return new Material(sh);
    }
}
