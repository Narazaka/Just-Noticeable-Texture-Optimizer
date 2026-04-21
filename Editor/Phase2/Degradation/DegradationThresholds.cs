using System.Collections.Generic;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Degradation
{
    public static class DegradationThresholds
    {
        public static readonly Dictionary<string, Dictionary<QualityPreset, float>> MaxScore = new Dictionary<string, Dictionary<QualityPreset, float>>
        {
            ["SSIM_inverse"] = new Dictionary<QualityPreset, float> {
                { QualityPreset.Low, 0.20f }, { QualityPreset.Medium, 0.15f },
                { QualityPreset.High, 0.08f }, { QualityPreset.Ultra, 0.03f } },
            ["Banding"] = Preset(0.8f, 0.7f, 0.4f, 0.2f),
            ["BlockBoundary"] = Preset(0.9f, 0.8f, 0.5f, 0.2f),
            ["Ringing"] = Preset(0.9f, 0.8f, 0.5f, 0.2f),
            ["ChromaDrift"] = Preset(0.9f, 0.8f, 0.5f, 0.3f),
            ["HighFrequency"] = Preset(0.9f, 0.8f, 0.6f, 0.3f),
            ["AlphaQuantization"] = Preset(0.9f, 0.8f, 0.5f, 0.2f),
        };

        static Dictionary<QualityPreset, float> Preset(float l, float m, float h, float u) =>
            new Dictionary<QualityPreset, float> {
                { QualityPreset.Low, l }, { QualityPreset.Medium, m },
                { QualityPreset.High, h }, { QualityPreset.Ultra, u } };
    }
}
