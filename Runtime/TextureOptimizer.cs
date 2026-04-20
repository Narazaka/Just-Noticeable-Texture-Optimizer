using System.Collections.Generic;
using UnityEngine;
using VRC.SDKBase;

namespace Narazaka.VRChat.Jnto
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Just-Noticeable Texture Optimizer/Texture Optimizer")]
    public class TextureOptimizer : MonoBehaviour, IEditorOnly
    {
        public TextureOptimizerMode Mode = TextureOptimizerMode.Root;

        [Tooltip("品質プリセット (Root必須、Override空可)")]
        public QualityPresetOverride Preset = new QualityPresetOverride { HasValue = true, Value = QualityPreset.Medium };

        [Tooltip("想定最近接視距離 cm (Root必須、Override空可)")]
        public FloatOverride ViewDistanceCm = new FloatOverride { HasValue = true, Value = 30f };

        [Tooltip("ボーン重み (Root必須、Override空可)")]
        public BoneWeightMapOverride BoneWeights = new BoneWeightMapOverride { HasValue = true, Value = BoneWeightMap.Default() };

        public Complexity.ComplexityStrategyAsset ComplexityStrategy;

        public List<Object> ExcludeList = new List<Object>();
    }

    [System.Serializable] public struct QualityPresetOverride { public bool HasValue; public QualityPreset Value; }
    [System.Serializable] public struct FloatOverride { public bool HasValue; public float Value; }
    [System.Serializable] public class BoneWeightMapOverride { public bool HasValue; public BoneWeightMap Value; }
}
