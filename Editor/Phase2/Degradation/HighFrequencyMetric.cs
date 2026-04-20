using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Degradation
{
    public class HighFrequencyMetric : IDegradationMetric
    {
        public string Name => "HighFrequency";

        public float Evaluate(Texture2D original, Texture2D candidate)
        {
            double eo = Energy(original), ec = Energy(candidate);
            if (eo < 1e-6) return 0f;
            double ratio = ec / eo;
            return Mathf.Clamp01((float)((0.5 - ratio) / 0.5));
        }

        static double Energy(Texture2D t)
        {
            int w = t.width, h = t.height;
            var p = t.GetPixels();
            double e = 0;
            for (int y = 1; y < h - 1; y++)
                for (int x = 1; x < w - 1; x++)
                {
                    float gx = Lum(p[y*w+x+1]) - Lum(p[y*w+x-1]);
                    float gy = Lum(p[(y+1)*w+x]) - Lum(p[(y-1)*w+x]);
                    e += gx*gx + gy*gy;
                }
            return e / Mathf.Max(1, (w - 2) * (h - 2));
        }

        static float Lum(Color c) => 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
    }
}
