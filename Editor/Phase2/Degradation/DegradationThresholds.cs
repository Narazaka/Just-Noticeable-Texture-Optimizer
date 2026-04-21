using System.Collections.Generic;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Degradation
{
    public static class DegradationThresholds
    {
        // FLIP: 0 = identical, 1 = maximum difference. Mean per-pixel FLIP error.
        // ChromaDrift: max CIEDE2000 ΔE00 / 10. 0 = no drift, 1 = ΔE00 >= 10.
        // AlphaQuantization: max(levelScore, rmseScore). 0 = no quantization, 1 = severe.
        public static readonly Dictionary<string, Dictionary<QualityPreset, float>> MaxScore = new Dictionary<string, Dictionary<QualityPreset, float>>
        {
            ["FLIP"] = Preset(0.15f, 0.10f, 0.06f, 0.03f),
            ["ChromaDrift"] = Preset(0.5f, 0.35f, 0.25f, 0.15f),
            ["AlphaQuantization"] = Preset(0.5f, 0.35f, 0.25f, 0.1f),
        };

        static Dictionary<QualityPreset, float> Preset(float l, float m, float h, float u) =>
            new Dictionary<QualityPreset, float> {
                { QualityPreset.Low, l }, { QualityPreset.Medium, m },
                { QualityPreset.High, h }, { QualityPreset.Ultra, u } };
    }
}
