using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Degradation
{
    /// <summary>
    /// 多スケール高周波保持度メトリクス。
    /// 複数カーネルサイズ (3x3, 7x7, 15x15) でラプラシアンを抽出し、
    /// 各スケールの高周波エネルギー損失率の最悪値をスコアとして返す。
    /// 3x3のみでは捉えられない中周波数の劣化（4K→512で4-8pxパターンの消失等）を検出。
    /// </summary>
    public class HighFrequencyMetric : IDegradationMetric
    {
        public string Name => "HighFrequency";

        static readonly int[] KernelRadii = { 1, 3, 7 };

        public float Evaluate(Texture2D original, Texture2D candidate)
        {
            int w = original.width, h = original.height;
            var po = original.GetPixels();
            var pc = candidate.GetPixels();
            if (po.Length != pc.Length || w < 15 || h < 15) return 0f;

            var lumO = new float[po.Length];
            var lumC = new float[pc.Length];
            for (int i = 0; i < po.Length; i++)
            {
                lumO[i] = Lum(po[i]);
                lumC[i] = Lum(pc[i]);
            }

            float worstScore = 0f;
            foreach (int radius in KernelRadii)
            {
                if (w <= radius * 2 + 1 || h <= radius * 2 + 1) continue;

                var blurO = GaussianBlur(lumO, w, h, radius);
                float score = ComputeScore(lumO, lumC, blurO, w, h, radius);
                if (score > worstScore) worstScore = score;
            }

            return worstScore;
        }

        static float ComputeScore(float[] lumO, float[] lumC, float[] blurO, int w, int h, int r)
        {
            double origEnergy = 0, diffEnergy = 0;
            for (int y = r; y < h - r; y++)
            {
                for (int x = r; x < w - r; x++)
                {
                    int idx = y * w + x;
                    float loR = lumO[idx] - blurO[idx];
                    float lcR = lumC[idx] - blurO[idx];
                    origEnergy += loR * loR;
                    float d = loR - lcR;
                    diffEnergy += d * d;
                }
            }
            if (origEnergy < 1e-8) return 0f;
            return Mathf.Clamp01((float)(diffEnergy / origEnergy));
        }

        static float[] GaussianBlur(float[] src, int w, int h, int radius)
        {
            var kernel = BuildKernel(radius);
            int kSize = radius * 2 + 1;

            var tmp = new float[src.Length];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float sum = 0;
                    for (int k = -radius; k <= radius; k++)
                    {
                        int sx = Mathf.Clamp(x + k, 0, w - 1);
                        sum += src[y * w + sx] * kernel[k + radius];
                    }
                    tmp[y * w + x] = sum;
                }

            var dst = new float[src.Length];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float sum = 0;
                    for (int k = -radius; k <= radius; k++)
                    {
                        int sy = Mathf.Clamp(y + k, 0, h - 1);
                        sum += tmp[sy * w + x] * kernel[k + radius];
                    }
                    dst[y * w + x] = sum;
                }

            return dst;
        }

        static float[] BuildKernel(int radius)
        {
            int size = radius * 2 + 1;
            float sigma = radius / 2f;
            var kernel = new float[size];
            float sum = 0;
            for (int i = 0; i < size; i++)
            {
                float x = i - radius;
                kernel[i] = Mathf.Exp(-x * x / (2f * sigma * sigma));
                sum += kernel[i];
            }
            for (int i = 0; i < size; i++) kernel[i] /= sum;
            return kernel;
        }

        static float Lum(Color c) => 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
    }
}
