using System.Collections.Generic;
using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Gate
{
    [CreateAssetMenu(menuName = "Just-Noticeable Texture Optimizer/Degradation Calibration", fileName = "DegradationCalibration")]
    public class DegradationCalibration : ScriptableObject
    {
        [Tooltip("MSSL Band Energy Loss を JND 単位に換算する係数。1.0 / (loss_ratio_at_JND)")]
        public float MsslBandEnergyScale = 3.3f;

        [Tooltip("MSSL Structure-only SSIM loss を JND 単位に換算する係数。")]
        public float MsslStructureScale = 5.0f;

        [Tooltip("Ridge Preservation の強度減衰 → JND 係数。Ridge 差分は [0,1] に正規化済みで saturate しやすいため、threshold と余裕を持たせるため 1.5 に設定。")]
        public float RidgeScale = 1.5f;

        [Tooltip("Banding ピーク比 → JND 係数。")]
        public float BandingScale = 2.5f;

        [Tooltip("BlockBoundary 比 → JND 係数。")]
        public float BlockBoundaryScale = 2.5f;

        [Tooltip("AlphaQuantization → JND 係数。")]
        public float AlphaQuantScale = 2.5f;

        [Tooltip("NormalAngle (正規化済み 0-1) → JND 係数。max-per-tile で攻めすぎるため 4.0 に緩和。")]
        public float NormalAngleScale = 4.0f;

        [Tooltip("ChromaDrift (CIE76 ΔE) → JND 係数。0 で無効。圧縮によるエンドポイント量子化の色相シフトを検出。")]
        public float ChromaDriftScale = 2.5f;

        [Tooltip("Preset 別 JND 閾値 (tex_score < T_preset で pass)。")]
        public float ThresholdLow = 1.5f;
        public float ThresholdMedium = 1.0f;
        public float ThresholdHigh = 0.7f;
        public float ThresholdUltra = 0.5f;

        public float GetThreshold(QualityPreset p)
        {
            switch (p)
            {
                case QualityPreset.Low: return ThresholdLow;
                case QualityPreset.Medium: return ThresholdMedium;
                case QualityPreset.High: return ThresholdHigh;
                case QualityPreset.Ultra: return ThresholdUltra;
                default: return ThresholdMedium;
            }
        }

        public static DegradationCalibration Default()
        {
            return CreateInstance<DegradationCalibration>();
        }
    }
}
