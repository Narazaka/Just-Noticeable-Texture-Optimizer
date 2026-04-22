using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;
using Narazaka.VRChat.Jnto.Editor.Phase2.Tiling;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Gate
{
    public struct GateVerdict
    {
        public bool Pass;
        public float TextureScore;
        public int WorstTileIndex;
        public string DominantMetric;
        public int DominantMipLevel;
    }

    public class PerceptualGate
    {
        readonly DegradationCalibration _calib;

        public PerceptualGate(DegradationCalibration calib) { _calib = calib; }

        public GateVerdict Evaluate(
            RenderTexture orig, RenderTexture candidate,
            UvTileGrid grid, float[] rPerTile,
            QualityPreset preset, IReadOnlyList<IMetric> metrics)
        {
            float threshold = _calib.GetThreshold(preset);
            int tileCount = grid.Tiles.Length;

            var accum = new float[tileCount];
            var dominant = new string[tileCount];

            var tmp = new float[tileCount];
            foreach (var m in metrics)
            {
                System.Array.Clear(tmp, 0, tmp.Length);
                Profiler.BeginSample("JNTO.Gate." + m.Name);
                try
                {
                    m.Evaluate(orig, candidate, grid, rPerTile, _calib, tmp);
                }
                finally
                {
                    Profiler.EndSample();
                }
                for (int i = 0; i < tileCount; i++)
                {
                    if (tmp[i] > accum[i])
                    {
                        accum[i] = tmp[i];
                        dominant[i] = m.Name;
                    }
                }
            }

            float texMax = 0f;
            int worstIdx = -1;
            bool hasCovered = false;
            for (int i = 0; i < tileCount; i++)
            {
                if (!grid.Tiles[i].HasCoverage) continue;
                hasCovered = true;
                if (accum[i] > texMax)
                {
                    texMax = accum[i];
                    worstIdx = i;
                }
            }

            return new GateVerdict
            {
                Pass = hasCovered && texMax < threshold,
                TextureScore = texMax,
                WorstTileIndex = worstIdx,
                DominantMetric = worstIdx >= 0 ? dominant[worstIdx] : null,
                DominantMipLevel = worstIdx >= 0
                    ? EffectiveResolutionCalculator.LevelFromR(rPerTile[worstIdx], grid.TileSize)
                    : -1,
            };
        }

        public GateVerdict EvaluateDebug(
            RenderTexture orig, RenderTexture candidate,
            UvTileGrid grid, float[] rPerTile,
            QualityPreset preset, IReadOnlyList<IMetric> metrics,
            out float[][] perMetricScores, out string[] metricNames)
        {
            int tileCount = grid.Tiles.Length;
            perMetricScores = new float[metrics.Count][];
            metricNames = new string[metrics.Count];
            for (int i = 0; i < metrics.Count; i++)
            {
                perMetricScores[i] = new float[tileCount];
                metricNames[i] = metrics[i].Name;
                Profiler.BeginSample("JNTO.Gate." + metrics[i].Name);
                try
                {
                    metrics[i].Evaluate(orig, candidate, grid, rPerTile, _calib, perMetricScores[i]);
                }
                finally
                {
                    Profiler.EndSample();
                }
            }

            var accum = new float[tileCount];
            var dominant = new string[tileCount];
            for (int m = 0; m < metrics.Count; m++)
            {
                var tmp = perMetricScores[m];
                for (int i = 0; i < tileCount; i++)
                {
                    if (tmp[i] > accum[i]) { accum[i] = tmp[i]; dominant[i] = metricNames[m]; }
                }
            }

            float texMax = 0f;
            int worstIdx = -1;
            bool hasCovered = false;
            for (int i = 0; i < tileCount; i++)
            {
                if (!grid.Tiles[i].HasCoverage) continue;
                hasCovered = true;
                if (accum[i] > texMax) { texMax = accum[i]; worstIdx = i; }
            }

            return new GateVerdict
            {
                Pass = hasCovered && texMax < _calib.GetThreshold(preset),
                TextureScore = texMax,
                WorstTileIndex = worstIdx,
                DominantMetric = worstIdx >= 0 ? dominant[worstIdx] : null,
                DominantMipLevel = worstIdx >= 0
                    ? EffectiveResolutionCalculator.LevelFromR(rPerTile[worstIdx], grid.TileSize)
                    : -1,
            };
        }
    }
}
