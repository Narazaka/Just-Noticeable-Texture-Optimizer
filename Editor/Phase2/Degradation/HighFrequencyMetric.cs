using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Degradation
{
    /// <summary>
    /// ラプラシアン (高周波成分) の保持度で精細度劣化を評価。
    /// L = image - gaussianBlur(image) で高周波を抽出し、
    /// 原本と候補のラプラシアン間の RMSE 比率をスコア化。
    /// スコア 0 = 完全保持、1 = 完全消失。
    /// </summary>
    public class HighFrequencyMetric : IDegradationMetric
    {
        public string Name => "HighFrequency";

        public float Evaluate(Texture2D original, Texture2D candidate)
        {
            int w = original.width, h = original.height;
            var po = original.GetPixels();
            var pc = candidate.GetPixels();
            if (po.Length != pc.Length || w < 3 || h < 3) return 0f;

            var blurO = GaussianBlur3x3(po, w, h);
            var blurC = GaussianBlur3x3(pc, w, h);

            double origEnergy = 0, diffEnergy = 0;
            int interior = (w - 2) * (h - 2);

            for (int y = 1; y < h - 1; y++)
            {
                for (int x = 1; x < w - 1; x++)
                {
                    int idx = y * w + x;
                    float loR = Lum(po[idx]) - blurO[idx];
                    float lcR = Lum(pc[idx]) - blurC[idx];

                    origEnergy += loR * loR;
                    float d = loR - lcR;
                    diffEnergy += d * d;
                }
            }

            if (origEnergy < 1e-8) return 0f;
            float ratio = (float)(diffEnergy / origEnergy);
            return Mathf.Clamp01(ratio);
        }

        static float[] GaussianBlur3x3(Color[] src, int w, int h)
        {
            var dst = new float[src.Length];
            for (int y = 1; y < h - 1; y++)
            {
                for (int x = 1; x < w - 1; x++)
                {
                    float sum =
                        Lum(src[(y-1)*w + (x-1)]) * 1 + Lum(src[(y-1)*w + x]) * 2 + Lum(src[(y-1)*w + (x+1)]) * 1 +
                        Lum(src[y*w + (x-1)])     * 2 + Lum(src[y*w + x])     * 4 + Lum(src[y*w + (x+1)])     * 2 +
                        Lum(src[(y+1)*w + (x-1)]) * 1 + Lum(src[(y+1)*w + x]) * 2 + Lum(src[(y+1)*w + (x+1)]) * 1;
                    dst[y * w + x] = sum / 16f;
                }
            }
            return dst;
        }

        static float Lum(Color c) => 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
    }
}
