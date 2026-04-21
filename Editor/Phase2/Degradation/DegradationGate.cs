using System.Collections.Generic;
using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Degradation
{
    public class DegradationGate
    {
        readonly List<IDegradationMetric> _metrics;

        public DegradationGate()
        {
            _metrics = new List<IDegradationMetric> {
                new FlipMetric(),
                new ChromaDriftMetric(),
                new AlphaQuantizationMetric(),
            };
        }

        public bool Passes(Texture2D original, Texture2D candidate, QualityPreset preset, out string failedMetric)
        {
            foreach (var m in _metrics)
            {
                float s = m.Evaluate(original, candidate);
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
    }
}
