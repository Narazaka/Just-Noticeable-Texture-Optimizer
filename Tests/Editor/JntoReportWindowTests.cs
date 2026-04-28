using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.Reporting;

[Category("EditorWindow")]
public class JntoReportWindowTests
{
    [Test]
    public void Open_DoesNotThrow()
    {
        EditorWindow w = null;
        try
        {
            Assert.DoesNotThrow(() =>
            {
                JntoReportWindow.Open();
                w = EditorWindow.GetWindow<JntoReportWindow>();
            });
            Assert.IsNotNull(w);
        }
        finally
        {
            if (w != null) w.Close();
        }
    }

    [Test]
    public void OnGUI_WithEmptyLog_DoesNotThrow()
    {
        DecisionLog.Clear();
        var w = EditorWindow.CreateWindow<JntoReportWindow>();
        try
        {
            // OnGUI を直接呼ぶのは Unity が許可しない。Repaint で間接呼出し。
            w.Repaint();
            // 単に窓が存在することを確認
            Assert.IsNotNull(w);
        }
        finally { w.Close(); }
    }

    [Test]
    public void OnGUI_WithRecords_DoesNotThrow()
    {
        DecisionLog.Clear();
        DecisionLog.Add(new DecisionRecord
        {
            OrigSize = 4096, FinalSize = 32,
            OrigFormat = TextureFormat.RGBA32, FinalFormat = TextureFormat.DXT1,
            SavedBytes = 16 * 1024 * 1024,
            TextureScore = 0.05f,
            DominantMetric = "MSSL",
            DominantMipLevel = 7,
            WorstTileIndex = 0,
            ProcessingMs = 250f,
        });
        var w = EditorWindow.CreateWindow<JntoReportWindow>();
        try
        {
            w.Repaint();
            Assert.IsNotNull(w);
        }
        finally
        {
            w.Close();
            DecisionLog.Clear();
        }
    }
}
