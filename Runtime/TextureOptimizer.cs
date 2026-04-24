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

        [Tooltip("想定最近接視距離 cm (Root必須、Override空可)。VRChat 一般的会話距離 80-120cm。")]
        public FloatOverride ViewDistanceCm = new FloatOverride { HasValue = true, Value = 100f };

        [Tooltip("ボーン重み (Root必須、Override空可)")]
        public BoneWeightMapOverride BoneWeights = new BoneWeightMapOverride { HasValue = true, Value = BoneWeightMap.Default() };

        [Tooltip("HMD 片目のピクセル密度 (px/度)。Quest/Index 級で 20 が目安。")]
        public FloatOverride HMDPixelsPerDegree = new FloatOverride { HasValue = true, Value = 20f };

        [Tooltip("フォーマット決定時の encode 試行ポリシー。")]
        public EncodePolicyOverride EncodePolicy = new EncodePolicyOverride { HasValue = true, Value = Jnto.EncodePolicy.Safe };

        [Tooltip("Crunched (DXT1Crunched/DXT5Crunched) を候補に含めるか。" +
            "Crunched はダウンロード容量を大幅に削減するが、ランタイムで CPU 展開負荷がかかる。")]
        public BoolOverride AllowCrunched = new BoolOverride { HasValue = true, Value = false };

        [Tooltip("圧縮時の色相ドリフト検出を有効にする。DXT1 等のエンドポイント量子化による色シフトを検出。不要なら無効化可能。")]
        public BoolOverride EnableChromaDrift = new BoolOverride { HasValue = true, Value = true };

        [Tooltip("最適化の優先対象。VRAM: GPU メモリ最小化 (bpp 基準)。Download: アセットバンドル圧縮後のダウンロード容量最小化。")]
        public OptimizationTargetOverride OptimizationTarget = new OptimizationTargetOverride
            { HasValue = true, Value = Jnto.OptimizationTarget.VRAM };

        [Tooltip("永続キャッシュのモード。")]
        public CacheModeOverride Cache = new CacheModeOverride { HasValue = true, Value = Jnto.CacheMode.Full };

        [Tooltip("デバッグダンプ先ディレクトリ (空なら無効)。")]
        public string DebugDumpPath = "";

        [Tooltip("JND キャリブレーション (DegradationCalibration asset)。空で既定値。")]
        public UnityEngine.Object Calibration;

        public List<Object> ExcludeList = new List<Object>();
    }

    [System.Serializable] public struct QualityPresetOverride { public bool HasValue; public QualityPreset Value; }
    [System.Serializable] public struct FloatOverride { public bool HasValue; public float Value; }
    [System.Serializable] public class BoneWeightMapOverride { public bool HasValue; public BoneWeightMap Value; }
    [System.Serializable] public struct BoolOverride { public bool HasValue; public bool Value; }
    [System.Serializable] public struct EncodePolicyOverride { public bool HasValue; public EncodePolicy Value; }
    [System.Serializable] public struct CacheModeOverride { public bool HasValue; public CacheMode Value; }
    [System.Serializable] public struct OptimizationTargetOverride { public bool HasValue; public OptimizationTarget Value; }
}
