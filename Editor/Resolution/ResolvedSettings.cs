using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Resolution
{
    public class ResolvedSettings
    {
        public QualityPreset Preset = QualityPreset.Medium;
        public float ViewDistanceCm = 100f;
        public BoneWeightMap BoneWeights = BoneWeightMap.Default();
        public float HMDPixelsPerDegree = 20f;
        public EncodePolicy EncodePolicy = EncodePolicy.Safe;
        public bool AllowCrunched = false;
        public bool EnableChromaDrift = true;
        public OptimizationTarget OptimizationTarget = OptimizationTarget.VRAM;
        public CacheMode CacheMode = CacheMode.Full;
        public string DebugDumpPath = "";
        public Object Calibration;
    }
}
