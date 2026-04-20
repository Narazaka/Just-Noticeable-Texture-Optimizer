using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Degradation
{
    public class BlockBoundaryMetric : IDegradationMetric
    {
        public string Name => "BlockBoundary";

        public float Evaluate(Texture2D original, Texture2D candidate)
        {
            int w = candidate.width, h = candidate.height;
            var pc = candidate.GetPixels();
            double onGrid = 0, offGrid = 0;
            int nOn = 0, nOff = 0;
            for (int y = 0; y < h; y++)
                for (int x = 1; x < w; x++)
                {
                    float d = Mathf.Abs(Lum(pc[y*w+x]) - Lum(pc[y*w+x-1]));
                    if (x % 4 == 0) { onGrid += d; nOn++; } else { offGrid += d; nOff++; }
                }
            if (nOn == 0 || nOff == 0) return 0f;
            double ratio = (onGrid / nOn) / System.Math.Max(offGrid / nOff, 1e-6);
            return Mathf.Clamp01((float)((ratio - 1.5) / 1.5));
        }

        static float Lum(Color c) => 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
    }
}
