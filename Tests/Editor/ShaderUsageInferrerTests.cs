using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.Compression;

public class ShaderUsageInferrerTests
{
    [Test]
    public void Infer_NullMaterial_NullTex_ReturnsColor()
    {
        Assert.AreEqual(ShaderUsage.Color, ShaderUsageInferrer.Infer(null, null, null));
    }

    [Test]
    public void Infer_NullMaterial_NullTex_ReturnsColorEvenForBumpMapName()
    {
        // material null なら lilToon 判定が走らない → Color
        Assert.AreEqual(ShaderUsage.Color, ShaderUsageInferrer.Infer(null, "_BumpMap", null));
    }

    // 注: 実 lilToon material を作るのはテストの範疇外。Infer の本体動作は
    // 実 material 経由の bake テスト or integration test で確認する。
    // ここでは null 安全性とデフォルト動作のみを確認。
}
