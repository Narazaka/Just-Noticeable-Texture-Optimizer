using System.Collections.Generic;
using UnityEngine;
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
                m.Evaluate(orig, candidate, grid, rPerTile, _calib, tmp);
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
            for (int i = 0; i < tileCount; i++)
            {
                if (!grid.Tiles[i].HasCoverage) continue;
                if (accum[i] > texMax)
                {
                    texMax = accum[i];
                    worstIdx = i;
                }
            }

            return new GateVerdict
            {
                Pass = texMax < threshold,
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
