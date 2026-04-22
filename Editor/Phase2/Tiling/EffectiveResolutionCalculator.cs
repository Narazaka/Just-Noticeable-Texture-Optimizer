using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Tiling
{
    public static class EffectiveResolutionCalculator
    {
        public const int RMin = 4;

        public static float Oversampling(QualityPreset p)
        {
            switch (p)
            {
                case QualityPreset.Low: return 0.75f;
                case QualityPreset.Medium: return 1f;
                case QualityPreset.High: return 1.5f;
                case QualityPreset.Ultra: return 2f;
                default: return 1f;
            }
        }

        /// <summary>
        /// タイル内 worst-case 三角形が Nyquist を満たす実効表示解像度 r(T) を返す。
        /// 単位: タイル内ローカルのピクセル数 (0 .. tileSize)。
        /// 0 は「このタイルは見えない/coverage 無し」を表す。
        /// </summary>
        public static float ComputeR(
            TileStats tile,
            int tileSize,
            float viewDistanceCm,
            float hmdPxPerDeg,
            QualityPreset preset)
        {
            if (!tile.HasCoverage || tile.Density <= 1e-8f) return 0f;

            float pxPerCm = hmdPxPerDeg * (180f / Mathf.PI) / Mathf.Max(viewDistanceCm, 1f);
            float texelsPerCm = pxPerCm * Oversampling(preset) * tile.BoneWeight;
            float texelsPerCm2Desired = texelsPerCm * texelsPerCm;

            float ratio = texelsPerCm2Desired / tile.Density;
            float r = tileSize * Mathf.Sqrt(ratio);
            return Mathf.Clamp(r, RMin, tileSize);
        }

        /// <summary>
        /// r(T) に対応するピラミッドレベル (0=tileSize, 1=tileSize/2, ...) を返す。
        /// r が大きいほど level は小さい (=フル解像度寄り)。
        /// </summary>
        public static int LevelFromR(float r, int tileSize)
        {
            if (r <= 0) return 0;
            float ratio = tileSize / Mathf.Max(r, 1e-3f);
            int level = Mathf.Clamp(
                Mathf.FloorToInt(Mathf.Log(ratio, 2)),
                0,
                Mathf.FloorToInt(Mathf.Log(tileSize, 2)));
            return level;
        }
    }
}
