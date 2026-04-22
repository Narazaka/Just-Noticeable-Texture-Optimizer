using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Compression
{
    public struct FormatPrediction
    {
        public TextureFormat Format;
        public float Confidence;    // 0..1, 高いほど pass 確度高
        public string Reason;
    }

    public static class FormatPredictor
    {
        /// <summary>
        /// 軽量 fmt の pass 確度を返す。1.0 に近いほど verify 省略可能。
        /// </summary>
        public static FormatPrediction PredictLightweight(
            BlockStats[] stats, TextureRole role, QualityPreset preset)
        {
            switch (role)
            {
                case TextureRole.ColorOpaque:
                    return PredictDxt1(stats, preset);
                case TextureRole.ColorAlpha:
                    return PredictDxt5(stats, preset);
                case TextureRole.NormalMap:
                    return PredictBc5(stats, preset);
                case TextureRole.SingleChannel:
                    return new FormatPrediction
                    {
                        Format = TextureFormat.BC4,
                        Confidence = 1f,
                        Reason = "single-channel BC4 is near-lossless",
                    };
                default:
                    return new FormatPrediction
                    {
                        Format = TextureFormat.BC7,
                        Confidence = 1f,
                        Reason = "BC7 is near-lossless fallback",
                    };
            }
        }

        static FormatPrediction PredictDxt1(BlockStats[] s, QualityPreset preset)
        {
            int highNonLin = 0, highPlan = 0;
            foreach (var b in s)
            {
                if (b.Nonlinearity > 0.08f) highNonLin++;
                if (b.Planarity > 0.35f) highPlan++;
            }
            float threshold = PresetBlockFailRate(preset);
            float failRate = Mathf.Max(highNonLin, highPlan) / (float)Mathf.Max(1, s.Length);
            float confidence = Mathf.Clamp01(1f - failRate / threshold);
            return new FormatPrediction
            {
                Format = TextureFormat.DXT1,
                Confidence = confidence,
                Reason = $"fail-risk blocks {failRate:P1} (threshold {threshold:P1})",
            };
        }

        static FormatPrediction PredictDxt5(BlockStats[] s, QualityPreset preset)
        {
            var color = PredictDxt1(s, preset);
            int alphaBad = 0;
            foreach (var b in s) if (b.AlphaNonlinearity > 0.05f) alphaBad++;
            float alphaThreshold = PresetBlockFailRate(preset);
            float alphaFail = alphaBad / (float)Mathf.Max(1, s.Length);
            float alphaConf = Mathf.Clamp01(1f - alphaFail / alphaThreshold);
            return new FormatPrediction
            {
                Format = TextureFormat.DXT5,
                Confidence = Mathf.Min(color.Confidence, alphaConf),
                Reason = $"color {color.Confidence:F2}, alpha {alphaConf:F2}",
            };
        }

        static FormatPrediction PredictBc5(BlockStats[] s, QualityPreset preset)
        {
            int nonLin = 0;
            foreach (var b in s) if (b.Nonlinearity > 0.1f) nonLin++;
            float failRate = nonLin / (float)Mathf.Max(1, s.Length);
            float threshold = PresetBlockFailRate(preset);
            return new FormatPrediction
            {
                Format = TextureFormat.BC5,
                Confidence = Mathf.Clamp01(1f - failRate / threshold),
                Reason = $"normal variance {failRate:P1}",
            };
        }

        static float PresetBlockFailRate(QualityPreset p)
        {
            switch (p)
            {
                case QualityPreset.Low: return 0.20f;
                case QualityPreset.Medium: return 0.10f;
                case QualityPreset.High: return 0.05f;
                case QualityPreset.Ultra: return 0.02f;
                default: return 0.10f;
            }
        }
    }
}
