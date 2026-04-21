using System.Collections.Generic;
using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Degradation
{
    public class DegradationGate
    {
        const int MaxEvalSize = 256;

        readonly List<IDegradationMetric> _metrics;

        public DegradationGate()
        {
            _metrics = new List<IDegradationMetric> {
                new SsimMetric(),
                new BandingMetric(),
                new BlockBoundaryMetric(),
                new RingingMetric(),
                new ChromaDriftMetric(),
                new HighFrequencyMetric(),
                new AlphaQuantizationMetric(),
            };
        }

        public bool Passes(Texture2D original, Texture2D candidate, QualityPreset preset, out string failedMetric)
        {
            var evalOrig = original;
            var evalCand = candidate;
            bool downsampled = false;

            if (original.width > MaxEvalSize || original.height > MaxEvalSize)
            {
                int evalSize = Mathf.Min(MaxEvalSize, Mathf.Min(original.width, original.height));
                evalOrig = Downsample(original, evalSize);
                evalCand = Downsample(candidate, evalSize);
                downsampled = true;
            }

            bool result = true;
            failedMetric = null;
            foreach (var m in _metrics)
            {
                float s = m.Evaluate(evalOrig, evalCand);
                string key = m.Name == "SSIM" ? "SSIM_inverse" : m.Name;
                float score = m.Name == "SSIM" ? 1f - s : s;
                if (score > DegradationThresholds.MaxScore[key][preset])
                {
                    failedMetric = m.Name;
                    result = false;
                    break;
                }
            }

            if (downsampled)
            {
                Object.DestroyImmediate(evalOrig);
                Object.DestroyImmediate(evalCand);
            }
            return result;
        }

        static Texture2D Downsample(Texture2D src, int size)
        {
            var rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(src, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var dst = new Texture2D(size, size, TextureFormat.RGBA32, false);
            dst.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            dst.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return dst;
        }
    }
}
