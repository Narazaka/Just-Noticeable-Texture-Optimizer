using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.Reporting;

public class JntoNdmfReportTests
{
    [SetUp]
    public void Setup() { DecisionLog.Clear(); }

    [TearDown]
    public void TearDown() { DecisionLog.Clear(); }

    [Test]
    public void Emit_EmptyLog_DoesNotThrow()
    {
        Assert.DoesNotThrow(() => JntoNdmfReport.Emit());
    }

    [Test]
    public void Emit_WithRecords_DoesNotThrow()
    {
        DecisionLog.Add(new DecisionRecord
        {
            OrigSize = 4096,
            FinalSize = 2048,
            OrigFormat = TextureFormat.RGBA32,
            FinalFormat = TextureFormat.DXT5,
            SavedBytes = 16 * 1024 * 1024,
            TextureScore = 0.5f,
            DominantMetric = "MSSL",
            ProcessingMs = 150f,
        });
        DecisionLog.Add(new DecisionRecord
        {
            OrigSize = 2048,
            FinalSize = 1024,
            OrigFormat = TextureFormat.DXT5,
            FinalFormat = TextureFormat.DXT5,
            SavedBytes = 4 * 1024 * 1024,
            CacheHit = true,
        });

        Assert.DoesNotThrow(() => JntoNdmfReport.Emit());
    }

    [Test]
    public void Emit_WithNoCurrentReport_FallsBackToDebugLog()
    {
        DecisionLog.Add(new DecisionRecord
        {
            OrigSize = 1024,
            FinalSize = 512,
            OrigFormat = TextureFormat.RGBA32,
            FinalFormat = TextureFormat.DXT5,
            SavedBytes = 8 * 1024 * 1024,
            ProcessingMs = 42f,
        });

        // ErrorReport.CurrentReport が null の通常経路: Debug.Log に summary が出るだけで例外は投げない
        Assert.DoesNotThrow(() => JntoNdmfReport.Emit());
    }
}
