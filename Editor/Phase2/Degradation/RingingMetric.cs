using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Degradation
{
    public class RingingMetric : IDegradationMetric
    {
        public string Name => "Ringing";

        public float Evaluate(Texture2D original, Texture2D candidate)
        {
            int w = original.width, h = original.height;
            var po = original.GetPixels();
            var pc = candidate.GetPixels();
            if (pc.Length != po.Length) return 1f;

            bool[] edge = new bool[po.Length];
            int edgeCount = 0;
            for (int y = 1; y < h - 1; y++)
                for (int x = 1; x < w - 1; x++)
                {
                    float gx = Lum(po[y*w+x+1]) - Lum(po[y*w+x-1]);
                    float gy = Lum(po[(y+1)*w+x]) - Lum(po[(y-1)*w+x]);
                    if (gx*gx + gy*gy > 0.04f) { edge[y*w+x] = true; edgeCount++; }
                }
            if (edgeCount < 50) return 0f;

            double sumAdd = 0; int n = 0;
            for (int y = 2; y < h - 2; y++)
                for (int x = 2; x < w - 2; x++)
                {
                    if (!NearEdge(edge, x, y, w, h, 3)) continue;
                    float d2o = Lum(po[y*w+x+1]) - 2f*Lum(po[y*w+x]) + Lum(po[y*w+x-1]);
                    float d2c = Lum(pc[y*w+x+1]) - 2f*Lum(pc[y*w+x]) + Lum(pc[y*w+x-1]);
                    sumAdd += Mathf.Max(0f, Mathf.Abs(d2c) - Mathf.Abs(d2o));
                    n++;
                }
            return n == 0 ? 0f : Mathf.Clamp01((float)(sumAdd / n * 10.0));
        }

        static bool NearEdge(bool[] edge, int x, int y, int w, int h, int r)
        {
            int y0 = Mathf.Max(0, y - r), y1 = Mathf.Min(h - 1, y + r);
            int x0 = Mathf.Max(0, x - r), x1 = Mathf.Min(w - 1, x + r);
            for (int yy = y0; yy <= y1; yy++) for (int xx = x0; xx <= x1; xx++)
                if (edge[yy*w + xx]) return true;
            return false;
        }

        static float Lum(Color c) => 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
    }
}
