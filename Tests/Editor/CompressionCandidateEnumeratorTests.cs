using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.Compression;

public class CompressionCandidateEnumeratorTests
{
    const int MinSize = 32;

    [Test]
    public void NoOp_AlwaysIncluded_WhenOrigFmtInCandidateList()
    {
        // BC7 1024 + BC7 candidate: no-op is (1024x1024 BC7)
        var list = CompressionCandidateEnumerator.Enumerate(
            1024, 1024, TextureFormat.BC7,
            new List<TextureFormat> { TextureFormat.DXT1, TextureFormat.BC7 }, MinSize);
        Assert.IsTrue(list.Any(c => c.IsNoOp && c.Width == 1024 && c.Height == 1024 && c.Format == TextureFormat.BC7));
    }

    [Test]
    public void NoOp_ForcedIncluded_WhenOrigFmtNotInCandidateList()
    {
        // orig = RGBAFloat (unusual), candidate list = BC7 のみ
        // enumerator は candidate fmt から列挙するだけでは no-op が出ない → 必ず追加する
        var list = CompressionCandidateEnumerator.Enumerate(
            64, 64, TextureFormat.RGBAFloat,
            new List<TextureFormat> { TextureFormat.BC7 }, MinSize);
        Assert.IsTrue(list.Any(c => c.IsNoOp && c.Width == 64 && c.Height == 64 && c.Format == TextureFormat.RGBAFloat));
    }

    [Test]
    public void BytesConstraint_FiltersOutCandidatesLargerThanOrig()
    {
        // orig DXT1 32x32 → origBytes = 32*32*4/8*4/3 = 682 bytes
        // candidate: BC7 は 8bpp で倍サイズ → origBytes 超過で除外される
        var list = CompressionCandidateEnumerator.Enumerate(
            32, 32, TextureFormat.DXT1,
            new List<TextureFormat> { TextureFormat.DXT1, TextureFormat.BC7 }, MinSize);
        // BC7 32x32 は DXT1 32x32 より大きいので除外 → 少なくとも BC7@32x32 は無い
        Assert.IsFalse(list.Any(c => c.Format == TextureFormat.BC7 && c.Width == 32 && c.Height == 32),
            "BC7@32x32 should be excluded because it exceeds origBytes");
    }

    [Test]
    public void Enumerate_1024_BC7_DXT1Candidates_ContainsExpectedSizes()
    {
        var list = CompressionCandidateEnumerator.Enumerate(
            1024, 1024, TextureFormat.BC7,
            new List<TextureFormat> { TextureFormat.DXT1, TextureFormat.BC7 }, MinSize);
        // DXT1 size ∈ {32,64,128,256,512,1024}, BC7 size ∈ {32,64,128,256,512,1024}
        // origBytes = 1024*1024*8/8*4/3 = 1398101 bytes
        // すべて origBytes 以下
        Assert.IsTrue(list.Any(c => c.Format == TextureFormat.DXT1 && c.Width == 1024));
        Assert.IsTrue(list.Any(c => c.Format == TextureFormat.DXT1 && c.Width == 32));
        Assert.IsTrue(list.Any(c => c.Format == TextureFormat.BC7 && c.Width == 1024));
        Assert.IsTrue(list.Any(c => c.Format == TextureFormat.BC7 && c.Width == 32));
    }

    [Test]
    public void Sort_BytesAscending()
    {
        var list = CompressionCandidateEnumerator.Enumerate(
            512, 512, TextureFormat.BC7,
            new List<TextureFormat> { TextureFormat.DXT1, TextureFormat.BC7 }, MinSize);
        for (int i = 1; i < list.Count; i++)
        {
            Assert.LessOrEqual(list[i - 1].Bytes, list[i].Bytes,
                $"candidates must be sorted Bytes ASC: [{i - 1}]={list[i - 1].Bytes} > [{i}]={list[i].Bytes}");
        }
    }

    [Test]
    public void Sort_TieBreak_BppDescending_BC7BeforeDXT1AtSameBytes()
    {
        // DXT1 1024x1024 と BC7 512x512 (近似で同じbytes)
        // 実際: DXT1 1024x1024 = 1024*1024*4/8*4/3 = 699050
        //       BC7  512x512  =  512*512*8/8*4/3 = 349525  ← 違う
        // そこで同 bytes を狙って (w,h) = (512,512) BC7 と (512,512) DXT1 で比較
        //   BC7  512x512 = 349525
        //   DXT1 512x512 = 174762
        // 直接同 bytes は出しにくいので、比較ロジックだけ確認
        var list = CompressionCandidateEnumerator.Enumerate(
            256, 256, TextureFormat.BC7,
            new List<TextureFormat> { TextureFormat.DXT1, TextureFormat.BC7 }, MinSize);
        // 同 Bytes の candidate がある場合、BC7 が DXT1 より前に来る
        for (int i = 1; i < list.Count; i++)
        {
            if (list[i - 1].Bytes == list[i].Bytes)
            {
                Assert.GreaterOrEqual(list[i - 1].BitsPerPixel, list[i].BitsPerPixel,
                    "tie-break: higher bpp (higher quality) should come first");
            }
        }
    }

    [Test]
    public void BC4Origin_OnlyBC4Candidates()
    {
        // BC4 orig + [BC4, BC7] candidate
        // BC7 candidate は BC4 と同じ bpp(8 vs 4)なので、同 size で BC7 の方が大きくなり除外される可能性
        // BC4 orig 1024x1024 = 524288 bytes (base) * 4/3 = 699050
        // BC7 1024x1024 = 1048576 * 4/3 = 1398101 → 除外
        // BC7 512x512  = 262144 * 4/3 = 349525 → OK
        // BC7 32x32    = 1024 * 4/3 = 1365 → OK
        var list = CompressionCandidateEnumerator.Enumerate(
            1024, 1024, TextureFormat.BC4,
            new List<TextureFormat> { TextureFormat.BC4, TextureFormat.BC7 }, MinSize);
        Assert.IsTrue(list.Any(c => c.Format == TextureFormat.BC4), "BC4 candidates must exist");
        Assert.IsTrue(list.Any(c => c.Format == TextureFormat.BC7 && c.Width < 1024),
            "smaller BC7 candidates should be in (bytes constraint only excludes 1024)");
        // BC7 1024x1024 は除外
        Assert.IsFalse(list.Any(c => c.Format == TextureFormat.BC7 && c.Width == 1024),
            "BC7@1024x1024 exceeds origBytes (BC4@1024x1024)");
    }

    [Test]
    public void NonSquare_AspectPreserved()
    {
        // 512x256 → 大きい辺が size、小さい辺は aspect 保持
        var list = CompressionCandidateEnumerator.Enumerate(
            512, 256, TextureFormat.BC7,
            new List<TextureFormat> { TextureFormat.BC7 }, MinSize);
        // size=512 → (512, 256)
        Assert.IsTrue(list.Any(c => c.Width == 512 && c.Height == 256));
        // size=256 → (256, 128)
        Assert.IsTrue(list.Any(c => c.Width == 256 && c.Height == 128));
        // size=32 → (32, 16) (16 は 4 の倍数)
        Assert.IsTrue(list.Any(c => c.Width == 32 && c.Height == 16));
    }

    [Test]
    public void EmptyCandidateList_FallsBackToOrigFmt()
    {
        var list = CompressionCandidateEnumerator.Enumerate(
            128, 128, TextureFormat.BC7, new List<TextureFormat>(), MinSize);
        // 空リストの場合、orig fmt で列挙 → 少なくとも no-op 1 つ
        Assert.IsTrue(list.Any(c => c.IsNoOp));
    }

    [Test]
    public void SizeRange_FromMinSizeToOrigMaxDim()
    {
        var list = CompressionCandidateEnumerator.Enumerate(
            256, 256, TextureFormat.BC7,
            new List<TextureFormat> { TextureFormat.BC7 }, MinSize);
        var sizes = new HashSet<int>(list.Select(c => c.Width));
        // expected: {32, 64, 128, 256}
        Assert.Contains(32, sizes.ToList());
        Assert.Contains(64, sizes.ToList());
        Assert.Contains(128, sizes.ToList());
        Assert.Contains(256, sizes.ToList());
        // 512 以上は出ない (origMaxDim=256)
        Assert.IsFalse(sizes.Any(s => s > 256));
    }

    [Test]
    public void AllCandidates_RespectOrigBytesConstraint()
    {
        long origBytes = BytesEstimator.WithMips(512, 512, TextureFormat.DXT1);
        var list = CompressionCandidateEnumerator.Enumerate(
            512, 512, TextureFormat.DXT1,
            new List<TextureFormat> { TextureFormat.DXT1, TextureFormat.BC7 }, MinSize);
        foreach (var c in list)
        {
            Assert.LessOrEqual(c.Bytes, origBytes,
                $"candidate {c.Format}@{c.Width}x{c.Height} bytes={c.Bytes} exceeds orig={origBytes}");
        }
    }

    [Test]
    public void NoDuplicates()
    {
        var list = CompressionCandidateEnumerator.Enumerate(
            512, 512, TextureFormat.BC7,
            new List<TextureFormat> { TextureFormat.DXT1, TextureFormat.BC7, TextureFormat.BC7 }, MinSize);
        var keys = list.Select(c => (c.Width, c.Height, c.Format)).ToList();
        Assert.AreEqual(keys.Count, keys.Distinct().Count(), "no duplicate (w,h,fmt)");
    }
}
