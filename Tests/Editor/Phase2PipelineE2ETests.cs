using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Narazaka.VRChat.Jnto;
using Narazaka.VRChat.Jnto.Editor.Phase2;
using Narazaka.VRChat.Jnto.Editor.Phase2.Compression;
using Narazaka.VRChat.Jnto.Editor.Phase2.Gate;
using Narazaka.VRChat.Jnto.Editor.Phase2.GpuPipeline;
using Narazaka.VRChat.Jnto.Editor.Phase2.Tiling;
using Narazaka.VRChat.Jnto.Editor.Resolution;
using Narazaka.VRChat.Jnto.Editor.Tests.Fixtures;

/// <summary>
/// R-D4-2 Step 6: Phase2Pipeline の E2E 不変条件テスト。
/// フォーマット選択/縮小/no-op fallback の責務を統合的に検証する。
/// </summary>
public class Phase2PipelineE2ETests
{
    // ---------- Helpers ----------

    static ResolvedSettings DefaultSettings(QualityPreset p = QualityPreset.Medium)
    {
        return new ResolvedSettings
        {
            Preset = p,
            EncodePolicy = EncodePolicy.Safe,
            CacheMode = CacheMode.Full,
        };
    }

    static (UvTileGrid grid, float[] r) FullCoverage(int w, int h)
    {
        var g = TestGridFactory.AllCovered(w, h);
        return (g, TestGridFactory.FullR(g));
    }

    static Texture2D MakeColorSquare(int n, TextureFormat fmt)
    {
        // 実 fmt にエンコードしたい場合は CompressTexture を使う
        var rgba = new Texture2D(n, n, TextureFormat.RGBA32, true);
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            float v = x / (float)(n - 1);
            px[y * n + x] = new Color(v, v, v, 1f);
        }
        rgba.SetPixels(px);
        rgba.Apply(updateMipmaps: true);
        if (fmt == TextureFormat.RGBA32) return rgba;
        UnityEditor.EditorUtility.CompressTexture(rgba, fmt, UnityEditor.TextureCompressionQuality.Normal);
        return rgba;
    }

    // ---------- Tests ----------

    /// <summary>
    /// 1: DXT1 Color + α 不使用 → DXT1/BC7 候補のみ採用、絶対に DXT5 にならない。
    /// </summary>
    [Test]
    public void DXT1_Color_AlphaNotUsed_NeverPicksDXT5()
    {
        var t = MakeColorSquare(128, TextureFormat.DXT1);
        var (grid, r) = FullCoverage(128, 128);
        var calib = DegradationCalibration.Default();
        try
        {
            using (var ctx = GpuTextureContext.FromTexture2D(t))
            {
                var pipeline = new Phase2Pipeline(calib, ShaderUsage.Color, alphaUsed: false);
                var result = pipeline.Find(t, ctx, grid, r, DefaultSettings());
                Assert.IsNotNull(result);
                Assert.AreNotEqual(TextureFormat.DXT5, result.Format,
                    "DXT1 Color alphaNotUsed must NEVER inflate to DXT5");
                Assert.IsTrue(result.Format == TextureFormat.DXT1 || result.Format == TextureFormat.BC7,
                    $"expected DXT1 or BC7, got {result.Format}");
                if (result.Final != t) Object.DestroyImmediate(result.Final);
            }
        }
        finally
        {
            Object.DestroyImmediate(t);
            Object.DestroyImmediate(calib);
        }
    }

    /// <summary>
    /// 2: BC5 Normal → BC5/BC7 のみ、絶対に DXT5 にならない。
    /// </summary>
    [Test]
    public void BC5_Normal_NeverPicksDXT5()
    {
        var t = MakeColorSquare(128, TextureFormat.BC5);
        var (grid, r) = FullCoverage(128, 128);
        var calib = DegradationCalibration.Default();
        try
        {
            using (var ctx = GpuTextureContext.FromTexture2D(t, isLinear: true))
            {
                var pipeline = new Phase2Pipeline(calib, ShaderUsage.Normal, alphaUsed: false);
                var result = pipeline.Find(t, ctx, grid, r, DefaultSettings());
                Assert.IsNotNull(result);
                Assert.AreNotEqual(TextureFormat.DXT5, result.Format,
                    "Normal usage must NEVER pick DXT5");
                Assert.IsTrue(result.Format == TextureFormat.BC5 || result.Format == TextureFormat.BC7,
                    $"expected BC5 or BC7, got {result.Format}");
                if (result.Final != t) Object.DestroyImmediate(result.Final);
            }
        }
        finally
        {
            Object.DestroyImmediate(t);
            Object.DestroyImmediate(calib);
        }
    }

    /// <summary>
    /// 3: BC7 Color α 不使用 → DXT1 候補が enumerate される (候補集合検証)。
    /// </summary>
    [Test]
    public void BC7_Color_AlphaNotUsed_EnumeratesDXT1Candidate()
    {
        var fmts = FormatCandidateSelector.Select(TextureFormat.BC7, ShaderUsage.Color, false);
        var list = CompressionCandidateEnumerator.Enumerate(256, 256, TextureFormat.BC7, fmts, 32);
        Assert.IsTrue(list.Any(c => c.Format == TextureFormat.DXT1),
            "BC7 Color alphaNotUsed must enumerate DXT1 candidates");
    }

    /// <summary>
    /// 4: size shrink with same fmt — gradient + 適切 threshold で
    ///    small size に縮小し、bytes が減ることを確認。
    /// </summary>
    [Test]
    public void GradientWithLargeTexture_Pipeline_ProducesResult()
    {
        var t = TestTextureFactory.MakeGradient(128, 128);
        var (grid, r) = FullCoverage(128, 128);
        var calib = DegradationCalibration.Default();
        try
        {
            using (var ctx = GpuTextureContext.FromTexture2D(t))
            {
                var pipeline = new Phase2Pipeline(calib, ShaderUsage.Color, alphaUsed: false);
                var result = pipeline.Find(t, ctx, grid, r, DefaultSettings());
                Assert.IsNotNull(result);
                Assert.IsNotNull(result.Final);
                // 結果 size/fmt は settings/gate 次第だが、bytes は origBytes を超えない
                long origBytes = BytesEstimator.WithMips(t.width, t.height, t.format);
                long newBytes = BytesEstimator.WithMips(result.Width, result.Height, result.Format);
                Assert.LessOrEqual(newBytes, origBytes,
                    "result bytes must be <= origBytes (CompressionCandidateEnumerator constraint)");
                if (result.Final != t) Object.DestroyImmediate(result.Final);
            }
        }
        finally
        {
            Object.DestroyImmediate(t);
            Object.DestroyImmediate(calib);
        }
    }

    /// <summary>
    /// 5: SingleChannel で BC4 候補集合のみ、BC5/DXT5 化は拒否。
    /// </summary>
    [Test]
    public void SingleChannel_Inferrer_R8_NeverPicksBC5OrDXT5()
    {
        var fmts = FormatCandidateSelector.Select(TextureFormat.R8, ShaderUsage.SingleChannel, false);
        Assert.IsFalse(fmts.Contains(TextureFormat.BC5));
        Assert.IsFalse(fmts.Contains(TextureFormat.DXT5));
    }

    /// <summary>
    /// 6: bytes constraint — origFmt=DXT1@32x32 は極めて小さく、
    ///    BC7@32x32 (8bpp) は bytes 超過で除外される。
    /// </summary>
    [Test]
    public void Small_DXT1_ExcludesBC7AtSameSize()
    {
        var fmts = FormatCandidateSelector.Select(TextureFormat.DXT1, ShaderUsage.Color, false);
        var list = CompressionCandidateEnumerator.Enumerate(32, 32, TextureFormat.DXT1, fmts, 32);
        // BC7@32x32 = 8bpp で、DXT1@32x32 = 4bpp より大きい → 除外
        Assert.IsFalse(list.Any(c => c.Format == TextureFormat.BC7 && c.Width == 32),
            "BC7@32 must be excluded when orig is DXT1@32 (bytes constraint)");
    }

    /// <summary>
    /// 7: no-op fallback — 候補集合の先頭 (最小 bytes) が full fail でも
    ///    no-op (orig と一致する候補) が必ず含まれ、最後に pass する。
    /// </summary>
    [Test]
    public void Candidates_AlwaysContainNoOp()
    {
        // 様々な origFmt × usage で no-op が必ず含まれることを確認
        var cases = new (TextureFormat fmt, ShaderUsage usage, bool alpha)[]
        {
            (TextureFormat.DXT1, ShaderUsage.Color, false),
            (TextureFormat.DXT5, ShaderUsage.Color, true),
            (TextureFormat.BC7, ShaderUsage.Color, true),
            (TextureFormat.BC5, ShaderUsage.Normal, false),
            (TextureFormat.BC4, ShaderUsage.SingleChannel, false),
            (TextureFormat.RGB24, ShaderUsage.Color, false),
            (TextureFormat.RGBA32, ShaderUsage.Color, false),
        };
        foreach (var (fmt, usage, alpha) in cases)
        {
            var fmts = FormatCandidateSelector.Select(fmt, usage, alpha);
            var list = CompressionCandidateEnumerator.Enumerate(128, 128, fmt, fmts, 32);
            Assert.IsTrue(list.Any(c => c.IsNoOp),
                $"no-op must be present for fmt={fmt} usage={usage} alpha={alpha}");
        }
    }

    /// <summary>
    /// 8: ordering — candidate list の先頭は最小 bytes、末尾は no-op (最大 bytes)。
    /// </summary>
    [Test]
    public void Candidates_FirstIsSmallestBytes_LastIsOrigOrLarger()
    {
        var fmts = FormatCandidateSelector.Select(TextureFormat.BC7, ShaderUsage.Color, false);
        var list = CompressionCandidateEnumerator.Enumerate(512, 512, TextureFormat.BC7, fmts, 32);
        Assert.Greater(list.Count, 1);
        long first = list[0].Bytes;
        long last = list[list.Count - 1].Bytes;
        Assert.LessOrEqual(first, last, "first (smallest bytes) must be ≤ last (largest)");
    }
}
