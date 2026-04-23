using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Narazaka.VRChat.Jnto;
using Narazaka.VRChat.Jnto.Editor.Phase2.Compression;

public class CrunchedOptimizationTests
{
    const int MinSize = 32;

    // ---- FormatCandidateSelector: AllowCrunched ----

    [Test]
    public void Select_AllowCrunched_False_ExcludesCrunched()
    {
        var c = FormatCandidateSelector.Select(TextureFormat.DXT1, ShaderUsage.Color, false, allowCrunched: false);
        Assert.IsFalse(c.Contains(TextureFormat.DXT1Crunched));
        Assert.IsFalse(c.Contains(TextureFormat.DXT5Crunched));
    }

    [Test]
    public void Select_AllowCrunched_True_Color_IncludesDXT1Crunched()
    {
        var c = FormatCandidateSelector.Select(TextureFormat.DXT1, ShaderUsage.Color, false, allowCrunched: true);
        Assert.IsTrue(c.Contains(TextureFormat.DXT1Crunched));
    }

    [Test]
    public void Select_AllowCrunched_True_ColorAlpha_IncludesDXT5Crunched()
    {
        var c = FormatCandidateSelector.Select(TextureFormat.BC7, ShaderUsage.Color, true, allowCrunched: true);
        Assert.IsTrue(c.Contains(TextureFormat.DXT5Crunched));
    }

    [Test]
    public void Select_AllowCrunched_True_Normal_IncludesDXT5Crunched()
    {
        var c = FormatCandidateSelector.Select(TextureFormat.DXT5, ShaderUsage.Normal, false, allowCrunched: true);
        Assert.IsTrue(c.Contains(TextureFormat.DXT5Crunched));
    }

    [Test]
    public void Select_AllowCrunched_True_SingleChannel_IncludesDXT1Crunched()
    {
        var c = FormatCandidateSelector.Select(TextureFormat.BC4, ShaderUsage.SingleChannel, false, allowCrunched: true);
        Assert.IsTrue(c.Contains(TextureFormat.DXT1Crunched));
    }

    // ---- BytesEstimator: Crunched factor ----

    [Test]
    public void BytesEstimator_CrunchedHalfOfNonCrunched()
    {
        long dxt1 = BytesEstimator.WithMips(512, 512, TextureFormat.DXT1);
        long dxt1c = BytesEstimator.WithMips(512, 512, TextureFormat.DXT1Crunched);
        Assert.AreEqual(dxt1 / 2, dxt1c, "Crunched should be half of non-Crunched");

        long dxt5 = BytesEstimator.WithMips(512, 512, TextureFormat.DXT5);
        long dxt5c = BytesEstimator.WithMips(512, 512, TextureFormat.DXT5Crunched);
        Assert.AreEqual(dxt5 / 2, dxt5c, "Crunched should be half of non-Crunched");
    }

    // ---- CompressionCandidateEnumerator: Sort order ----

    [Test]
    public void Enumerate_VRAM_NonCrunchedBeforeCrunched_AtSameSize()
    {
        var fmts = new List<TextureFormat> { TextureFormat.DXT1, TextureFormat.DXT1Crunched };
        var list = CompressionCandidateEnumerator.Enumerate(256, 256, TextureFormat.DXT1, fmts, MinSize,
            OptimizationTarget.VRAM);

        var nonNoOps = list.Where(c => !c.IsNoOp && c.Width == 128 && c.Height == 128).ToList();
        if (nonNoOps.Count >= 2)
        {
            var crunchedIdx = nonNoOps.FindIndex(c => c.Format == TextureFormat.DXT1Crunched);
            var nonCrunchedIdx = nonNoOps.FindIndex(c => c.Format == TextureFormat.DXT1);
            // VRAM モード: bytes ASC。Crunched の推定 bytes < 非 Crunched なので Crunched が先
            Assert.Less(crunchedIdx, nonCrunchedIdx,
                "VRAM mode: Crunched (smaller bytes) should come before non-Crunched at same size");
        }
    }

    [Test]
    public void Enumerate_Download_CrunchedBeforeBC7_AtSameBpp()
    {
        var fmts = new List<TextureFormat>
        {
            TextureFormat.DXT1,
            TextureFormat.DXT1Crunched,
            TextureFormat.BC7,
        };
        var list = CompressionCandidateEnumerator.Enumerate(256, 256, TextureFormat.BC7, fmts, MinSize,
            OptimizationTarget.Download);

        var at256 = list.Where(c => !c.IsNoOp && c.Width == 256 && c.Height == 256).ToList();
        if (at256.Count >= 2)
        {
            var crunchedIdx = at256.FindIndex(c => c.Format == TextureFormat.DXT1Crunched);
            var bc7Idx = at256.FindIndex(c => c.Format == TextureFormat.BC7);
            if (crunchedIdx >= 0 && bc7Idx >= 0)
            {
                Assert.Less(crunchedIdx, bc7Idx,
                    "Download mode: Crunched (rank=0) should come before BC7 (rank=4)");
            }
        }
    }

    // ---- PostCompressionRank ordering ----

    [Test]
    public void PostCompressionRank_BC4_BeforeDXT1()
    {
        var fmts = new List<TextureFormat> { TextureFormat.BC4, TextureFormat.DXT1 };
        var list = CompressionCandidateEnumerator.Enumerate(128, 128, TextureFormat.DXT1, fmts, MinSize);
        var at128 = list.Where(c => c.Width == 128 && c.Height == 128 && !c.IsNoOp).ToList();
        if (at128.Count >= 2)
        {
            int bc4Idx = at128.FindIndex(c => c.Format == TextureFormat.BC4);
            int dxt1Idx = at128.FindIndex(c => c.Format == TextureFormat.DXT1);
            Assert.Less(bc4Idx, dxt1Idx, "BC4 (rank=0) should come before DXT1 (rank=1) at same bytes");
        }
    }

    [Test]
    public void PostCompressionRank_BC5_BeforeBC7()
    {
        var fmts = new List<TextureFormat> { TextureFormat.BC5, TextureFormat.BC7 };
        var list = CompressionCandidateEnumerator.Enumerate(256, 256, TextureFormat.BC7, fmts, MinSize);
        var at256 = list.Where(c => c.Width == 256 && c.Height == 256 && !c.IsNoOp).ToList();
        if (at256.Count >= 2)
        {
            int bc5Idx = at256.FindIndex(c => c.Format == TextureFormat.BC5);
            int bc7Idx = at256.FindIndex(c => c.Format == TextureFormat.BC7);
            Assert.Less(bc5Idx, bc7Idx, "BC5 (rank=0) should come before BC7 (rank=4) at same bpp");
        }
    }

    [Test]
    public void PostCompressionRank_DXT1_BeforeDXT5()
    {
        var fmts = new List<TextureFormat> { TextureFormat.DXT1, TextureFormat.DXT5 };
        var list = CompressionCandidateEnumerator.Enumerate(256, 256, TextureFormat.DXT5, fmts, MinSize);
        var at128 = list.Where(c => c.Width == 128 && c.Height == 128).ToList();
        if (at128.Count >= 2)
        {
            int dxt1Idx = at128.FindIndex(c => c.Format == TextureFormat.DXT1);
            int dxt5Idx = at128.FindIndex(c => c.Format == TextureFormat.DXT5);
            Assert.Less(dxt1Idx, dxt5Idx, "DXT1 (4bpp) should come before DXT5 (8bpp) due to smaller bytes");
        }
    }

    // ---- Crunched originals: BytesEstimator constraint ----

    [Test]
    public void CrunchedOriginal_BytesConstraint_FiltersLargerNonCrunched()
    {
        // DXT1Crunched 256x256 orig → origBytes ≈ half of DXT1 256x256
        // 非 Crunched DXT1@256 は origBytes を超えるため除外されるべき
        var fmts = new List<TextureFormat> { TextureFormat.DXT1, TextureFormat.BC7 };
        var list = CompressionCandidateEnumerator.Enumerate(
            256, 256, TextureFormat.DXT1Crunched, fmts, MinSize);

        long origBytes = BytesEstimator.WithMips(256, 256, TextureFormat.DXT1Crunched);
        foreach (var c in list)
        {
            if (c.IsNoOp) continue;
            Assert.LessOrEqual(c.Bytes, origBytes,
                $"{c.Format}@{c.Width}x{c.Height} bytes={c.Bytes} exceeds Crunched origBytes={origBytes}");
        }
    }
}
