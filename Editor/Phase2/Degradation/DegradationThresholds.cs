using System.Collections.Generic;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Degradation
{
    public static class DegradationThresholds
    {
        public static readonly Dictionary<string, Dictionary<QualityPreset, float>> MaxScore = new Dictionary<string, Dictionary<QualityPreset, float>>
        {
            ["SSIM_inverse"] = new Dictionary<QualityPreset, float> {
                { QualityPreset.Low, 0.08f }, { QualityPreset.Medium, 0.05f },
                { QualityPreset.High, 0.03f }, { QualityPreset.Ultra, 0.01f } },
            ["Banding"] = Preset(0.4f, 0.3f, 0.2f, 0.1f),
            ["BlockBoundary"] = Preset(0.5f, 0.35f, 0.2f, 0.1f),
            ["Ringing"] = Preset(0.5f, 0.35f, 0.2f, 0.1f),
            ["ChromaDrift"] = Preset(0.5f, 0.35f, 0.25f, 0.15f),
            ["HighFrequency"] = Preset(0.6f, 0.45f, 0.3f, 0.15f),
            ["AlphaQuantization"] = Preset(0.5f, 0.35f, 0.25f, 0.1f),
        };

        static Dictionary<QualityPreset, float> Preset(float l, float m, float h, float u) =>
            new Dictionary<QualityPreset, float> {
                { QualityPreset.Low, l }, { QualityPreset.Medium, m },
                { QualityPreset.High, h }, { QualityPreset.Ultra, u } };
    }
}
