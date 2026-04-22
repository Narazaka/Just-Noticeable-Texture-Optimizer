using System.Collections.Generic;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2;
using Narazaka.VRChat.Jnto.Editor.Phase2.Compression;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Degradation
{
    public class DegradationGate
    {
        public bool Passes(
            Texture2D original, Texture2D candidate,
            TextureRole role, QualityPreset preset,
            TexelDensityMap densityMap,
            out string failedMetric)
        {
            Texture2D evalOriginal = original;
            Texture2D evalCandidate = candidate;
            bool destroyOriginal = false;
            bool destroyCandidate = false;

            if (!original.isReadable)
            {
                evalOriginal = MakeReadable(original);
                destroyOriginal = true;
            }

            if (candidate.width != evalOriginal.width || candidate.height != evalOriginal.height)
            {
                int maxDim = Mathf.Max(evalOriginal.width, evalOriginal.height);
                evalCandidate = ResolutionReducer.Resize(candidate, maxDim);
                destroyCandidate = true;
            }

            try
            {
                var metrics = MetricsFor(role);
                foreach (var m in metrics)
                {
                    float s;
                    if (densityMap != null && m is IPerPixelMetric perPixel)
                        s = EvaluateDensityWeighted(perPixel, evalOriginal, evalCandidate, densityMap);
                    else
                        s = m.Evaluate(evalOriginal, evalCandidate);

                    if (!DegradationThresholds.MaxScore.TryGetValue(m.Name, out var presetMap))
                        continue;
                    if (s > presetMap[preset])
                    {
                        failedMetric = m.Name;
                        return false;
                    }
                }
            }
            finally
            {
                if (destroyCandidate) Object.DestroyImmediate(evalCandidate);
                if (destroyOriginal) Object.DestroyImmediate(evalOriginal);
            }

            failedMetric = null;
            return true;
        }

        static Texture2D MakeReadable(Texture2D src)
        {
            var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(src, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var dst = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
            dst.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
            dst.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return dst;
        }

        static float EvaluateDensityWeighted(IPerPixelMetric metric, Texture2D original, Texture2D candidate, TexelDensityMap densityMap)
        {
            float[] scores = metric.EvaluatePerPixel(original, candidate);
            if (scores == null || scores.Length == 0) return 0f;

            int w = original.width, h = original.height;
            int mapW = densityMap.Width, mapH = densityMap.Height;

            var weighted = new List<float>(scores.Length / 4);
            for (int i = 0; i < scores.Length; i++)
            {
                int px = i % w, py = i / w;
                int mx = Mathf.Clamp(px * mapW / w, 0, mapW - 1);
                int my = Mathf.Clamp(py * mapH / h, 0, mapH - 1);
                float d = densityMap.Density[my * mapW + mx];
                if (d > 0.01f)
                    weighted.Add(scores[i]);
            }

            if (weighted.Count == 0) return 0f;
            weighted.Sort();
            int p99idx = Mathf.Min((int)(weighted.Count * 0.99f), weighted.Count - 1);
            return weighted[p99idx];
        }

        static List<IDegradationMetric> MetricsFor(TextureRole role)
        {
            switch (role)
            {
                case TextureRole.NormalMap:
                    return new List<IDegradationMetric>
                    {
                        new NormalAngleMetric(),
                        new NormalVarianceMetric(),
                    };
                case TextureRole.ColorOpaque:
                    return new List<IDegradationMetric>
                    {
                        new FlipMetric(),
                        new BandingMetric(),
                    };
                case TextureRole.ColorAlpha:
                    return new List<IDegradationMetric>
                    {
                        new FlipMetric(),
                        new AlphaQuantizationMetric(),
                        new BandingMetric(),
                    };
                case TextureRole.SingleChannel:
                    return new List<IDegradationMetric>
                    {
                        new SsimMetric(),
                        new BandingMetric(),
                    };
                case TextureRole.MatCapOrLut:
                    return new List<IDegradationMetric>
                    {
                        new ChromaDriftMetric(),
                        new BandingMetric(),
                    };
                default:
                    return new List<IDegradationMetric>
                    {
                        new FlipMetric(),
                    };
            }
        }
    }
}
