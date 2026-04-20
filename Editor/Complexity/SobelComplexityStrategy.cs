using UnityEngine;
using Narazaka.VRChat.Jnto.Complexity;

namespace Narazaka.VRChat.Jnto.Editor.Complexity
{
    [CreateAssetMenu(menuName = "Just-Noticeable Texture Optimizer/Complexity/Sobel", fileName = "SobelComplexity")]
    public class SobelComplexityStrategy : ComplexityStrategyAsset
    {
        public override float Measure(Color[] region, int w, int h)
        {
            if (w < 3 || h < 3 || region.Length < w * h) return 0.5f;
            double sum = 0; int n = 0;
            for (int y = 1; y < h - 1; y++)
                for (int x = 1; x < w - 1; x++)
                {
                    float gx = Lum(region[y*w+x+1]) - Lum(region[y*w+x-1]);
                    float gy = Lum(region[(y+1)*w+x]) - Lum(region[(y-1)*w+x]);
                    sum += Mathf.Sqrt(gx*gx + gy*gy);
                    n++;
                }
            float mean = (float)(sum / System.Math.Max(n, 1));
            return Mathf.Clamp01(mean * 2f);
        }

        static float Lum(Color c) => 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
    }
}
