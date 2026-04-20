using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Degradation
{
    public class BandingMetric : IDegradationMetric
    {
        public string Name => "Banding";

        public float Evaluate(Texture2D original, Texture2D candidate)
        {
            int w = original.width, h = original.height;
            var po = original.GetPixels();
            var pc = candidate.GetPixels();
            if (pc.Length != po.Length) return 1f;

            const float flatThreshold = 0.02f;
            bool[] flat = new bool[po.Length];
            int flatCount = 0;
            for (int y = 1; y < h - 1; y++)
                for (int x = 1; x < w - 1; x++)
                {
                    float l = Lum(po[y * w + x]);
                    float dMax = 0f;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            float d = Mathf.Abs(Lum(po[(y + dy) * w + (x + dx)]) - l);
                            if (d > dMax) dMax = d;
                        }
                    if (dMax < flatThreshold) { flat[y * w + x] = true; flatCount++; }
                }
            if (flatCount < 100) return 0f;

            var hist = new int[256];
            int samples = 0;
            for (int y = 1; y < h - 1; y++)
                for (int x = 1; x < w - 1; x++)
                {
                    if (!flat[y * w + x]) continue;
                    float lc = Lum(pc[y * w + x]);
                    float lx = Lum(pc[y * w + x - 1]);
                    float lr = Lum(pc[y * w + x + 1]);
                    float d2 = lr - 2f * lc + lx;
                    int bin = Mathf.Clamp(Mathf.RoundToInt((d2 + 0.5f) * 255f), 0, 255);
                    hist[bin]++;
                    samples++;
                }
            if (samples == 0) return 0f;

            int top = 0;
            for (int i = 0; i < 256; i++) if (i != 128) top = Mathf.Max(top, hist[i]);
            return Mathf.Clamp01(top / (samples * 0.02f));
        }

        static float Lum(Color c) => 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
    }
}
