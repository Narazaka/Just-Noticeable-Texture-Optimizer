using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.Gate;
using Narazaka.VRChat.Jnto.Editor.Phase2.GpuPipeline;
using Narazaka.VRChat.Jnto.Editor.Phase2.Tiling;

/// <summary>
/// Tier 2 検出: Banding.compute の bin が [-0.5, 0.5] の範囲外で clamp されて両端に飽和するか。
/// 「半分 0.1 / 半分 0.9」の sharp edge を含む画像を orig=candidate で渡す (差分なし)。
/// 正しく動作するなら orig/candidate のヒストグラムが一致して extra=0 → score=0。
/// B4 が実在すれば orig のエッジで d2 が [-0.5, 0.5] 外に出て両端 bin に集積、
/// candidate の該当 bin もすべて同じ分布になるはずだが、仮に片側だけ飽和ロジックが
/// 異常作動していれば score > 0 になる。
/// 同時に完全一致 (orig==cand) のスコアを非常に低く保つことも検証。
/// </summary>
public class BandingBinClampTests
{
    [Test]
    public void SharpEdge_Identical_ScoreNearZero()
    {
        var tex = MakeSharpEdge(128, low: 0.1f, high: 0.9f);
        var grid = UvTileGrid.Create(128, 128);
        MarkAllCovered(grid);
        var r = FullR(grid);
        var calib = DegradationCalibration.Default();

        using (var ctx = GpuTextureContext.FromTexture2D(tex))
        {
            var metric = new BandingMetric();
            var scores = new float[grid.Tiles.Length];
            metric.Evaluate(ctx.Original, ctx.Original, grid, r, calib, scores);

            float maxScore = 0f;
            foreach (var s in scores) if (s > maxScore) maxScore = s;
            UnityEngine.Debug.Log($"[JNTO/BandingBinClamp] identical sharp-edge max score = {maxScore:F4}");
            Assert.Less(maxScore, 0.05f,
                "identical sharp-edge texture must produce ~zero banding score (bin clamp must not cause asymmetric inflation)");
        }

        Object.DestroyImmediate(tex);
        Object.DestroyImmediate(calib);
    }

    static Texture2D MakeSharpEdge(int n, float low, float high)
    {
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            float v = x < n / 2 ? low : high;
            px[y * n + x] = new Color(v, v, v, 1f);
        }
        t.SetPixels(px);
        t.Apply();
        return t;
    }

    static void MarkAllCovered(UvTileGrid g)
    {
        for (int i = 0; i < g.Tiles.Length; i++)
            g.Tiles[i] = new TileStats { HasCoverage = true, Density = 100f, BoneWeight = 1f };
    }

    static float[] FullR(UvTileGrid g)
    {
        var r = new float[g.Tiles.Length];
        for (int i = 0; i < r.Length; i++) r[i] = g.TileSize;
        return r;
    }
}
