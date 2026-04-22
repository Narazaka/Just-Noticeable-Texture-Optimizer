using System.Collections.Generic;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2;
using Narazaka.VRChat.Jnto.Editor.Phase2.Compression;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Degradation
{
    public class DegradationGate
    {
        Texture2D _cachedReadable;
        int _cachedOriginalId = -1;

        public bool PassesDownscale(Texture2D original, Texture2D reference,
            TextureRole role, QualityPreset preset, TexelDensityMap densityMap,
            out string failedMetric)
        {
            int origMax = Mathf.Max(original.width, original.height);
            int refMax = Mathf.Max(reference.width, reference.height);
            if (refMax >= origMax)
            {
                failedMetric = null;
                return true;
            }

            var readable = GetReadable(original);
            var upscaled = ResolutionReducer.Resize(reference, origMax);
            try
            {
                var metrics = DownscaleMetricsFor(role);
                foreach (var m in metrics)
                {
                    float s;
                    if (densityMap != null && m is IPerPixelMetric perPixel)
                        s = EvaluateDensityWeighted(perPixel, readable, upscaled, densityMap);
                    else
                        s = m.Evaluate(readable, upscaled);

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
                Object.DestroyImmediate(upscaled);
            }

            failedMetric = null;
            return true;
        }

        public bool PassesCompression(Texture2D reference, Texture2D candidate,
            TextureRole role, QualityPreset preset, TexelDensityMap densityMap,
            out string failedMetric)
        {
            TexelDensityMap evalMap = densityMap;
            if (densityMap != null && (densityMap.Width != reference.width || densityMap.Height != reference.height))
                evalMap = TexelDensityMap.ResizeTo(densityMap, reference.width, reference.height);

            var metrics = CompressionMetricsFor(role);
            foreach (var m in metrics)
            {
                float s;
                if (evalMap != null && m is IPerPixelMetric perPixel)
                    s = EvaluateDensityWeighted(perPixel, reference, candidate, evalMap);
                else
                    s = m.Evaluate(reference, candidate);

                if (!DegradationThresholds.MaxScore.TryGetValue(m.Name, out var presetMap))
                    continue;
                if (s > presetMap[preset])
                {
                    failedMetric = m.Name;
                    return false;
                }
            }

            failedMetric = null;
            return true;
        }

        public void Cleanup()
        {
            if (_cachedReadable != null)
            {
                Object.DestroyImmediate(_cachedReadable);
                _cachedReadable = null;
                _cachedOriginalId = -1;
            }
        }

        Texture2D GetReadable(Texture2D original)
        {
            if (original.isReadable) return original;
            int id = original.GetInstanceID();
            if (_cachedReadable != null && _cachedOriginalId == id) return _cachedReadable;

            if (_cachedReadable != null) Object.DestroyImmediate(_cachedReadable);

            var rt = RenderTexture.GetTemporary(original.width, original.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(original, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            _cachedReadable = new Texture2D(original.width, original.height, TextureFormat.RGBA32, false);
            _cachedReadable.ReadPixels(new Rect(0, 0, original.width, original.height), 0, 0);
            _cachedReadable.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            _cachedOriginalId = id;
            return _cachedReadable;
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

        static List<IDegradationMetric> DownscaleMetricsFor(TextureRole role)
        {
            switch (role)
            {
                case TextureRole.NormalMap:
                    return new List<IDegradationMetric>
                    {
                        new NormalAngleMetric(),
                        new NormalVarianceMetric(),
                    };
                default:
                    return new List<IDegradationMetric>
                    {
                        new HighFrequencyMetric(),
                    };
            }
        }

        static List<IDegradationMetric> CompressionMetricsFor(TextureRole role)
        {
            switch (role)
            {
                case TextureRole.NormalMap:
                    return new List<IDegradationMetric>
                    {
                        new NormalAngleMetric(),
                    };
                case TextureRole.ColorAlpha:
                    return new List<IDegradationMetric>
                    {
                        new BandingMetric(),
                        new BlockBoundaryMetric(),
                        new AlphaQuantizationMetric(),
                    };
                default:
                    return new List<IDegradationMetric>
                    {
                        new BandingMetric(),
                        new BlockBoundaryMetric(),
                    };
            }
        }
    }
}
