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
            IList<TextureFormat> candidateFmts, int minSize)
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

            // Sort: Bytes ASC, BitsPerPixel DESC (高品質優先), Format name ASC (安定)
            list.Sort((a, b) =>
            {
                int c = a.Bytes.CompareTo(b.Bytes);
                if (c != 0) return c;
                c = b.BitsPerPixel.CompareTo(a.BitsPerPixel);
                if (c != 0) return c;
                return string.CompareOrdinal(a.Format.ToString(), b.Format.ToString());
            });

            return list;
        }

        /// <summary>
        /// ResolutionReducer.Resize と同じロジックで (w, h) を計算する。
        /// 大きい辺 = targetMaxDim、小さい辺 = 比率を保って 4 の倍数に丸める。
        /// </summary>
        static (int w, int h) ComputeAspectSize(int origW, int origH, int targetMaxDim)
        {
            if (origW >= origH)
            {
                int tw = targetMaxDim;
                int th = Mathf.Max(4, RoundToMultipleOf4(Mathf.RoundToInt(targetMaxDim * (float)origH / origW)));
                return (tw, th);
            }
            else
            {
                int th = targetMaxDim;
                int tw = Mathf.Max(4, RoundToMultipleOf4(Mathf.RoundToInt(targetMaxDim * (float)origW / origH)));
                return (tw, th);
            }
        }

        static int RoundToMultipleOf4(int v) => ((v + 3) / 4) * 4;
    }
}
