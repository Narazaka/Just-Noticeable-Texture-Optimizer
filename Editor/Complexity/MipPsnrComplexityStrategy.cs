using UnityEngine;
using Narazaka.VRChat.Jnto.Complexity;

namespace Narazaka.VRChat.Jnto.Editor.Complexity
{
    [CreateAssetMenu(menuName = "Just-Noticeable Texture Optimizer/Complexity/Mip PSNR", fileName = "MipPsnrComplexity")]
    public class MipPsnrComplexityStrategy : ComplexityStrategyAsset
    {
        public override float Measure(Color[] region, int w, int h)
        {
            if (w < 4 || h < 4) return 0.5f;
            int hw = w / 2, hh = h / 2;
            var half = new Color[hw * hh];
            for (int y = 0; y < hh; y++)
                for (int x = 0; x < hw; x++)
                {
                    half[y*hw+x] = (region[2*y*w + 2*x] + region[2*y*w + 2*x+1] +
                                    region[(2*y+1)*w + 2*x] + region[(2*y+1)*w + 2*x+1]) * 0.25f;
                }
            double mse = 0;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var up = half[Mathf.Min(y/2, hh-1) * hw + Mathf.Min(x/2, hw-1)];
                    var o = region[y*w + x];
                    mse += (up.r - o.r) * (up.r - o.r) + (up.g - o.g) * (up.g - o.g) + (up.b - o.b) * (up.b - o.b);
                }
            mse /= (double)(w * h * 3);
            if (mse < 1e-9) return 0f;
            double psnr = 10.0 * System.Math.Log10(1.0 / mse);
            return 1f - Mathf.Clamp01((float)((psnr - 20.0) / 20.0));
        }
    }
}
