using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.Gate;
using Narazaka.VRChat.Jnto.Editor.Phase2.GpuPipeline;
using Narazaka.VRChat.Jnto.Editor.Phase2.Tiling;
using Narazaka.VRChat.Jnto.Editor.Tests.Fixtures;

/// <summary>
/// Critical 回帰防止: 実テクスチャ近似フィクスチャで metric が saturate しない。
/// 実アバターベイクで JND≈2.5 saturate 21/22 が起きた問題への TDD ガード。
/// 根本原因:
///   - MSSL Structure: spec の c(x,y)·s(x,y) でなく abs(lum_diff) 実装 → 縮小で 0.66 級に張り付く。
///   - Banding: candidate のみ 2 次微分 histogram → orig=cand でも非ゼロ。
///   - BlockBoundary: candidate のみ → orig=cand でも非ゼロ (grid tex では saturate)。
/// 本テストは修正後に Pass することを期待する (修正前は fail)。
/// </summary>
[Category("GPU")]
public class MetricSaturationTests
{
    // ---- MSSL ----

    [Test]
    public void Mssl_RealisticDownsample_DoesNotSaturate()
    {
        var orig = TestTextureFactory.MakeCheckerboard(256, 256, period: 4);
        var cand = MakeDownsampledThenUpsampled(orig, 128);
        try
        {
            var score = EvalMaxScore(new MsslMetric(), orig, cand);
            // MsslStructureScale=5.0, MsslBandEnergyScale=3.3 なので「saturate しない」
            // 閾値は max(3.3, 5.0) の半分程度 = 2.5 未満を許容 (実運用で Medium=1.0 を超える
            // 程度の差がつく)。飽和時は 3.3〜5.0 まで張り付く。
            Assert.Less(score, 2.5f,
                $"MSSL score {score} saturated; should not pin at calibration ceiling for 2x downsample");
        }
        finally
        {
            Object.DestroyImmediate(orig);
            Object.DestroyImmediate(cand);
        }
    }

    [Test]
    public void Mssl_IdenticalGradient_IsNearZero()
    {
        var orig = TestTextureFactory.MakeGradient(256, 256, quantize: 0);
        try
        {
            var score = EvalMaxScore(new MsslMetric(), orig, orig);
            Assert.Less(score, 0.05f, $"MSSL orig=cand should be near 0 (got {score})");
        }
        finally { Object.DestroyImmediate(orig); }
    }

    // ---- Banding ----

    [Test]
    public void Banding_OrigVsOrig_IsZero()
    {
        var orig = TestTextureFactory.MakeGradient(256, 256, quantize: 0);
        try
        {
            var score = EvalMaxScore(new BandingMetric(), orig, orig);
            Assert.Less(score, 0.1f, $"Banding orig=cand should be near 0 (got {score})");
        }
        finally { Object.DestroyImmediate(orig); }
    }

    [Test]
    public void Banding_NaturalGradient_DoesNotSaturate()
    {
        // sRGB gradient (連続) を近距離サンプルする実運用 texture。
        // quantize 段差が無い = Banding は本来 0 付近。現行 shader では orig 無視で
        // candidate の 2 次微分ピークが収束し scale*1.0 で saturate する。
        var orig = TestTextureFactory.MakeGradient(256, 256, quantize: 0);
        var cand = MakeDownsampledThenUpsampled(orig, 128);
        try
        {
            var score = EvalMaxScore(new BandingMetric(), orig, cand);
            // BandingScale=2.5 で saturate すると 2.5。0.5 以下ならば差分ベースが機能している。
            Assert.Less(score, 1.0f,
                $"Banding on smooth-gradient downsample should not saturate (got {score})");
        }
        finally
        {
            Object.DestroyImmediate(orig);
            Object.DestroyImmediate(cand);
        }
    }

    // ---- BlockBoundary ----

    [Test]
    public void BlockBoundary_OrigVsOrig_OnSmoothGradient_IsZero()
    {
        var orig = TestTextureFactory.MakeGradient(256, 256, quantize: 0);
        try
        {
            var score = EvalMaxScore(new BlockBoundaryMetric(), orig, orig);
            Assert.Less(score, 0.1f,
                $"BlockBoundary orig=cand on smooth gradient should be 0 (got {score})");
        }
        finally { Object.DestroyImmediate(orig); }
    }

    [Test]
    public void BlockBoundary_OrigVsOrig_OnStepPattern_IsZero()
    {
        // step pattern (4 px 周期) を orig/cand 両方に与えると、
        // 「圧縮による block 顕在化」ではないので本来 score=0 であるべき。
        // 現行 shader は candidate のみ参照で orig=cand でも saturate する。
        var t = MakeGridSteps(256, period: 4);
        try
        {
            var score = EvalMaxScore(new BlockBoundaryMetric(), t, t);
            Assert.Less(score, 0.1f,
                $"BlockBoundary orig=cand on step pattern should be 0 (extra signal only; got {score})");
        }
        finally { Object.DestroyImmediate(t); }
    }

    // ---- helpers ----

    /// <summary>
    /// box-filter downsample → bilinear-style upsample。
    /// 実際の texture 圧縮パイプラインに近い「緩やかな縮小 → 拡大」を再現する。
    /// nearest-neighbor でやると artificial step artifact が出て Banding が正当に立つ
    /// ので、それを避ける。
    /// </summary>
    static Texture2D MakeDownsampledThenUpsampled(Texture2D src, int midSize)
    {
        int w = src.width, h = src.height;
        var srcPx = src.GetPixels();
        // Box filter downsample: midSize x midSize に平均値を集める
        int block = Mathf.Max(1, w / midSize);
        var midPx = new Color[midSize * midSize];
        for (int my = 0; my < midSize; my++)
        for (int mx = 0; mx < midSize; mx++)
        {
            Color sum = Color.black;
            int n = 0;
            int sx0 = mx * w / midSize;
            int sy0 = my * h / midSize;
            int sx1 = Mathf.Min(w, (mx + 1) * w / midSize);
            int sy1 = Mathf.Min(h, (my + 1) * h / midSize);
            for (int yy = sy0; yy < sy1; yy++)
            for (int xx = sx0; xx < sx1; xx++)
            {
                sum += srcPx[yy * w + xx]; n++;
            }
            midPx[my * midSize + mx] = n > 0 ? sum / n : Color.black;
        }
        // Bilinear upsample: midSize → w×h 線形補間
        var dstPx = new Color[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float fx = (x + 0.5f) * midSize / w - 0.5f;
            float fy = (y + 0.5f) * midSize / h - 0.5f;
            int x0 = Mathf.Clamp(Mathf.FloorToInt(fx), 0, midSize - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(fy), 0, midSize - 1);
            int x1 = Mathf.Clamp(x0 + 1, 0, midSize - 1);
            int y1 = Mathf.Clamp(y0 + 1, 0, midSize - 1);
            float tx = Mathf.Clamp01(fx - x0);
            float ty = Mathf.Clamp01(fy - y0);
            Color c00 = midPx[y0 * midSize + x0];
            Color c10 = midPx[y0 * midSize + x1];
            Color c01 = midPx[y1 * midSize + x0];
            Color c11 = midPx[y1 * midSize + x1];
            Color c0 = Color.Lerp(c00, c10, tx);
            Color c1 = Color.Lerp(c01, c11, tx);
            dstPx[y * w + x] = Color.Lerp(c0, c1, ty);
        }
        var dst = new Texture2D(w, h, TextureFormat.RGBA32, false);
        dst.SetPixels(dstPx);
        dst.Apply();
        return dst;
    }

    static Texture2D MakeGridSteps(int n, int period)
    {
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            int blk = x / period;
            float v = (blk & 1) == 0 ? 0.2f : 0.8f;
            px[y * n + x] = new Color(v, v, v, 1f);
        }
        t.SetPixels(px); t.Apply();
        return t;
    }

    static float EvalMaxScore(IMetric metric, Texture2D orig, Texture2D cand)
    {
        var grid = TestGridFactory.AllCovered(orig.width, orig.height);
        var r = TestGridFactory.FullR(grid);
        var calib = DegradationCalibration.Default();
        try
        {
            using (var ctxA = GpuTextureContext.FromTexture2D(orig))
            using (var ctxB = GpuTextureContext.FromTexture2D(cand))
            {
                var scores = new float[grid.Tiles.Length];
                metric.Evaluate(ctxA.Original, ctxB.Original, grid, r, calib, scores);
                float max = 0f;
                foreach (var s in scores) if (s > max) max = s;
                return max;
            }
        }
        finally { Object.DestroyImmediate(calib); }
    }
}
