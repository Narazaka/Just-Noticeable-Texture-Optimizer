using System.Collections.Generic;
using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Reporting
{
    public static class DecisionLog
    {
        static readonly List<DecisionRecord> _records = new();

        public static IReadOnlyList<DecisionRecord> All => _records;

        public static void Clear() => _records.Clear();

        public static void Add(DecisionRecord r)
        {
            _records.Add(r);
            Debug.Log(Format(r));
        }

        public static string Format(DecisionRecord r)
        {
            string fmtChange = r.OrigFormat == r.FinalFormat
                ? r.FinalFormat.ToString()
                : $"{r.OrigFormat}→{r.FinalFormat}";
            string sizeChange = r.OrigSize == r.FinalSize
                ? r.FinalSize.ToString()
                : $"{r.OrigSize}→{r.FinalSize}";
            string saved = FormatBytes(r.SavedBytes);
            string hit = r.CacheHit ? " (cache hit)" : "";
            string name = r.OriginalTexture != null ? r.OriginalTexture.name : "<null>";
            return $"[JNTO] {name}: {sizeChange} {fmtChange}, saved {saved}, " +
                   $"JND {r.TextureScore:F2}, dominant={r.DominantMetric ?? "-"}@L{r.DominantMipLevel}, t({r.WorstTileIndex}){hit}";
        }

        static string FormatBytes(long b)
        {
            if (b < 0) return "-" + FormatBytes(-b);
            if (b < 1024) return b + "B";
            if (b < 1024 * 1024) return (b / 1024f).ToString("F1") + "KB";
            return (b / 1024f / 1024f).ToString("F1") + "MB";
        }
    }
}
