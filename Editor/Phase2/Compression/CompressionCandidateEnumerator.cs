using System.Collections.Generic;
using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Compression
{
    /// <summary>
    /// R-D4-2 容量最小化探索で使う candidate のレコード。
    /// (Width, Height) は power-of-2 縮小後のサイズ、aspect は元に合わせる。
    /// </summary>
    public struct CompressionCandidate
    {
        public int Width;
        public int Height;
        public TextureFormat Format;
        public long Bytes;      // mip chain 込みの推定バイト数
        public int BitsPerPixel;
        public bool IsNoOp;     // (origW, origH, origFmt) と一致する「無加工」候補
    }

    /// <summary>
    /// (size × fmt) の全候補を列挙し、
    ///   - bytes ≤ origBytes でフィルタ
    ///   - bytes ASC (同値なら bpp DESC、それも同値なら fmt 名 ASC) で安定ソート
    /// した結果を返す。
    ///
    /// 必ず元 (origW, origH, origFmt) = no-op 候補を 1 つ含む (bytes ≤ origBytes は常に成立)。
    /// 呼び出し側はこの順に gate 評価していけば「通る中で最も容量が小さい」ものを採用できる。
    /// </summary>
    public static class CompressionCandidateEnumerator
    {
        public static IReadOnlyList<CompressionCandidate> Enumerate(
            int origWidth, int origHeight, TextureFormat origFmt,
            IList<TextureFormat> candidateFmts, int minSize,
            OptimizationTarget target = OptimizationTarget.VRAM)
        {
            if (origWidth <= 0 || origHeight <= 0) return new List<CompressionCandidate>();
            if (candidateFmts == null || candidateFmts.Count == 0) candidateFmts = new List<TextureFormat> { origFmt };
            if (minSize < 1) minSize = 1;

            int origMaxDim = Mathf.Max(origWidth, origHeight);
            long origBytes = BytesEstimator.WithMips(origWidth, origHeight, origFmt);

            var sizes = new List<int>();
            int s = minSize;
            while (s < origMaxDim)
            {
                sizes.Add(s);
                s *= 2;
            }
            sizes.Add(origMaxDim);

            // 重複排除用: (w, h, fmt) キー
            var seen = new HashSet<(int, int, TextureFormat)>();
            var list = new List<CompressionCandidate>();

            foreach (var size in sizes)
            {
                var (w, h) = ComputeAspectSize(origWidth, origHeight, size);
                foreach (var fmt in candidateFmts)
                {
                    if (!seen.Add((w, h, fmt))) continue;
                    long bytes = BytesEstimator.WithMips(w, h, fmt);
                    if (bytes > origBytes) continue; // 制約
                    list.Add(new CompressionCandidate
                    {
                        Width = w,
                        Height = h,
                        Format = fmt,
                        Bytes = bytes,
                        BitsPerPixel = BytesEstimator.BitsPerPixel(fmt),
                        IsNoOp = (w == origWidth && h == origHeight && fmt == origFmt),
                    });
                }
            }

            // 必ず no-op を 1 つ含める (候補 fmt に origFmt が無いケースも救済)
            bool hasNoOp = false;
            foreach (var c in list) if (c.IsNoOp) { hasNoOp = true; break; }
            if (!hasNoOp)
            {
                list.Add(new CompressionCandidate
                {
                    Width = origWidth,
                    Height = origHeight,
                    Format = origFmt,
                    Bytes = origBytes,
                    BitsPerPixel = BytesEstimator.BitsPerPixel(origFmt),
                    IsNoOp = true,
                });
            }

            // Sort order depends on OptimizationTarget:
            //
            // VRAM (default):
            //   Bytes ASC → PostCompressionRank ASC → Format int ASC
            //   最小 VRAM の候補を先に試行。同 VRAM なら事後圧縮効率の良い形式を優先。
            //
            // Download:
            //   PostCompressionRank ASC → Bytes ASC → Format int ASC
            //   事後圧縮効率の良い形式を最優先。同ランクなら小サイズ優先。
            //   例: DXT1Crunched (4bpp VRAM, ~2bpp DL, rank=0) を
            //       DXT1 (4bpp VRAM, ~4bpp DL, rank=1) より先に試行。
            list.Sort((a, b) =>
            {
                int c;
                if (target == OptimizationTarget.Download)
                {
                    c = PostCompressionRank(a.Format).CompareTo(PostCompressionRank(b.Format));
                    if (c != 0) return c;
                    c = a.Bytes.CompareTo(b.Bytes);
                    if (c != 0) return c;
                }
                else
                {
                    c = a.Bytes.CompareTo(b.Bytes);
                    if (c != 0) return c;
                    c = PostCompressionRank(a.Format).CompareTo(PostCompressionRank(b.Format));
                    if (c != 0) return c;
                }
                return ((int)a.Format).CompareTo((int)b.Format);
            });

            return list;
        }

        static (int w, int h) ComputeAspectSize(int origW, int origH, int targetMaxDim)
            => AspectSizeCalculator.Compute(origW, origH, targetMaxDim);

        /// <summary>
        /// 事後圧縮 (LZ4/LZMA) 効率の相対ランク。小さいほど効率が良い (= 優先)。
        /// 同 bytes/bpp 候補間のタイブレーカーとして使用。
        ///
        /// ブロック圧縮のエントロピー要因:
        ///   - チャンネル数: 少ないほど endpoint/index の多様性が低い
        ///   - モード選択: BC7/BC6H はブロックごとに 8 モード × パーティションを
        ///     選択するため、ヘッダビットのエントロピーが高い
        ///   - Crunched: 既にエントロピー符号化済みで LZ4/LZMA の重ね掛けは効果が薄い
        ///
        /// 非圧縮フォーマットは空間的相関をそのまま保持するため
        /// LZ4/LZMA のスライディングウィンドウが有効に機能する。
        /// </summary>
        static int PostCompressionRank(TextureFormat fmt)
        {
            switch (fmt)
            {
                // --- ブロック圧縮: 低→高エントロピー ---
                // 1-2ch ブロック: endpoint (8bit×2) + 3bit index ×16。
                // 構造が均質で LZ が長い一致を見つけやすい。
                case TextureFormat.BC4: return 0;
                case TextureFormat.BC5: return 0;

                // 3ch 色ブロック: endpoint (16bit×2) + 2bit index ×16。
                // 色 endpoint は BC4 の 8bit より多様だがモード選択なし。
                case TextureFormat.DXT1: return 1;

                // DXT1 + 独立 alpha ブロック。DXT1 より endpoint が増える。
                case TextureFormat.DXT5: return 2;

                // Crunched: 既にエントロピー符号化済みで実効サイズが約 1/2。
                // LZ4/LZMA との重ね掛けでも非 Crunched より最終サイズが小さい。
                // JNTO は Crunched 出力不可のため候補には現れないが、
                // no-op (オリジナル維持) のソート位置に影響する。
                case TextureFormat.DXT1Crunched: return 0;
                case TextureFormat.DXT5Crunched: return 0;

                // モード選択ブロック: 1-5bit のモードフィールド + パーティションインデックス。
                // ブロックごとにフォーマットが変わりエントロピーが最も高い。
                case TextureFormat.BC6H: return 4;
                case TextureFormat.BC7: return 4;

                // --- 非圧縮: チャンネル数順 ---
                // ピクセル単位の空間相関を LZ が直接利用できる。
                // チャンネルが少ないほど一致長が伸びる。
                case TextureFormat.R8: return 0;
                case TextureFormat.Alpha8: return 0;
                case TextureFormat.R16: return 0;
                case TextureFormat.RG16: return 1;
                case TextureFormat.RGB565: return 1;
                case TextureFormat.ARGB4444: return 2;
                case TextureFormat.RGBA4444: return 2;
                case TextureFormat.RGB24: return 2;
                case TextureFormat.RGBA32: return 3;
                case TextureFormat.ARGB32: return 3;
                case TextureFormat.BGRA32: return 3;
                case TextureFormat.RGBAHalf: return 4;
                case TextureFormat.RGBAFloat: return 5;

                default: return 3;
            }
        }

    }
}
