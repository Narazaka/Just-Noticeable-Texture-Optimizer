using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Narazaka.VRChat.Jnto.Editor.Shared;

public class LilToonCoreCatalogQualityTests
{
    static IEnumerable AllEntries()
    {
        foreach (var kvp in LilToonCoreCatalogData.EnumerateAll())
            yield return new TestCaseData(kvp.Key.variantId, kvp.Key.propName, kvp.Value).SetName($"{kvp.Key.variantId ?? "*"} / {kvp.Key.propName}");
    }

    [TestCaseSource(nameof(AllEntries))]
    public void Entry_HasEvidenceRef(string variantId, string propName, LilToonPropertyInfo info)
    {
        Assert.IsFalse(string.IsNullOrEmpty(info.EvidenceRef), $"{variantId ?? "*"}/{propName} の EvidenceRef が空");
    }

    [TestCaseSource(nameof(AllEntries))]
    public void Entry_HasNonEmptyReadChannels(string variantId, string propName, LilToonPropertyInfo info)
    {
        Assert.AreNotEqual(ChannelMask.None, info.ReadChannels, $"{variantId ?? "*"}/{propName} の ReadChannels が None (サンプルされない prop は catalog から除外)");
    }

    [TestCaseSource(nameof(AllEntries))]
    public void Entry_UsageIsValidEnumValue(string variantId, string propName, LilToonPropertyInfo info)
    {
        Assert.IsTrue(System.Enum.IsDefined(typeof(Narazaka.VRChat.Jnto.Editor.Phase2.Compression.ShaderUsage), info.Usage));
    }

    // audit 進行中: seed entry が残っているものを検知。Phase 3 全部終了まで FAIL 想定、終了時に green 化。
    [TestCaseSource(nameof(AllEntries))]
    public void Entry_EvidenceRef_NotSeedPlaceholder(string variantId, string propName, LilToonPropertyInfo info)
    {
        Assert.AreNotEqual("(seed from prev catalog)", info.EvidenceRef,
            $"{variantId ?? "*"}/{propName} が seed placeholder のまま。audit が未完了。");
    }
}
