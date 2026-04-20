using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Phase2
{
    public static class DensityCalculator
    {
        public const int MinSize = 32;

        public static float BaseDensity(QualityPreset p)
        {
            switch (p)
            {
                case QualityPreset.Low: return 10f;
                case QualityPreset.Medium: return 20f;
                case QualityPreset.High: return 40f;
                case QualityPreset.Ultra: return 80f;
                default: return 20f;
            }
        }

        public static int ComputeTargetSize(
            MeshDensityStats stats,
            QualityPreset preset,
            float viewDistanceCm,
            float complexityFactor)
        {
            float baseD = BaseDensity(preset);
            float distanceFactor = (30f / Mathf.Max(viewDistanceCm, 1f));
            distanceFactor *= distanceFactor;
            float adjusted = baseD * distanceFactor * stats.BoneWeightAverage * complexityFactor;
            if (stats.UvArea <= 1e-8f || stats.WorldArea <= 1e-8f) return MinSize;
            float target = Mathf.Sqrt(adjusted * stats.WorldArea / stats.UvArea);
            int pot = CeilPowerOfTwo(Mathf.CeilToInt(target));
            return Mathf.Max(MinSize, pot);
        }

        public static int CeilPowerOfTwo(int v)
        {
            if (v <= 1) return 1;
            int p = 1;
            while (p < v) p <<= 1;
            return p;
        }
    }
}
