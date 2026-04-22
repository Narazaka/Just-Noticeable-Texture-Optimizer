using System.Collections.Generic;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Degradation
{
    public static class DegradationThresholds
    {
        public static readonly Dictionary<string, Dictionary<QualityPreset, float>> MaxScore = new Dictionary<string, Dictionary<QualityPreset, float>>
        {
            ["FLIP"] = Preset(0.15f, 0.10f, 0.06f, 0.03f),
            ["ChromaDrift"] = Preset(0.5f, 0.35f, 0.25f, 0.15f),
            ["AlphaQuantization"] = Preset(0.5f, 0.35f, 0.25f, 0.1f),
            ["NormalAngle"] = Preset(0.20f, 0.15f, 0.10f, 0.05f),
            ["NormalVariance"] = Preset(0.30f, 0.20f, 0.15f, 0.08f),
            ["HighFrequency"] = Preset(0.40f, 0.30f, 0.20f, 0.10f),
            ["Banding"] = Preset(0.50f, 0.35f, 0.25f, 0.15f),
            ["SSIM"] = Preset(0.15f, 0.10f, 0.06f, 0.03f),
        };

        static Dictionary<QualityPreset, float> Preset(float l, float m, float h, float u) =>
            new Dictionary<QualityPreset, float> {
                { QualityPreset.Low, l }, { QualityPreset.Medium, m },
                { QualityPreset.High, h }, { QualityPreset.Ultra, u } };
    }
}
