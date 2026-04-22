using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Resolution
{
    public class ResolvedSettings
    {
        public QualityPreset Preset = QualityPreset.Medium;
        public float ViewDistanceCm = 30f;
        public BoneWeightMap BoneWeights = BoneWeightMap.Default();
        public float HMDPixelsPerDegree = 20f;
        public EncodePolicy EncodePolicy = EncodePolicy.Safe;
        public CacheMode CacheMode = CacheMode.Full;
        public string DebugDumpPath = "";
        public Object Calibration;
    }
}
