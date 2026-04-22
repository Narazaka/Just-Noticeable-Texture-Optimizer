using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Degradation
{
    /// <summary>
    /// 11x11 ガウシアンウィンドウ SSIM (Wang et al. 2004)。
    /// 返り値は 1 - MSSIM (0=同一, 1=完全劣化) で DegradationGate と統一。
    /// </summary>
    public class SsimMetric : IDegradationMetric
    {
        public string Name => "SSIM";

        const int WindowSize = 11;
        const float Sigma = 1.5f;
        const float C1 = 0.01f * 0.01f;
        const float C2 = 0.03f * 0.03f;

        static readonly float[] GaussianKernel = BuildGaussianKernel();

        public float Evaluate(Texture2D a, Texture2D b)
        {
            var pa = a.GetPixels();
            var pb = b.GetPixels();
            int w = a.width, h = a.height;
            if (pa.Length != pb.Length || w < WindowSize || h < WindowSize) return 0f;

            var lumA = new float[pa.Length];
            var lumB = new float[pb.Length];
            for (int i = 0; i < pa.Length; i++)
            {
                lumA[i] = Lum(pa[i]);
                lumB[i] = Lum(pb[i]);
            }

            int half = WindowSize / 2;
            double ssimSum = 0;
            int count = 0;

            for (int y = half; y < h - half; y += 4)
            {
                for (int x = half; x < w - half; x += 4)
                {
                    float muA = 0, muB = 0;
                    for (int ky = -half; ky <= half; ky++)
                        for (int kx = -half; kx <= half; kx++)
                        {
                            float g = GaussianKernel[(ky + half) * WindowSize + (kx + half)];
                            int idx = (y + ky) * w + (x + kx);
                            muA += g * lumA[idx];
                            muB += g * lumB[idx];
                        }

                    float varA = 0, varB = 0, cov = 0;
                    for (int ky = -half; ky <= half; ky++)
                        for (int kx = -half; kx <= half; kx++)
                        {
                            float g = GaussianKernel[(ky + half) * WindowSize + (kx + half)];
                            int idx = (y + ky) * w + (x + kx);
                            float da = lumA[idx] - muA;
                            float db = lumB[idx] - muB;
                            varA += g * da * da;
                            varB += g * db * db;
                            cov += g * da * db;
                        }

                    float num = (2f * muA * muB + C1) * (2f * cov + C2);
                    float den = (muA * muA + muB * muB + C1) * (varA + varB + C2);
                    ssimSum += num / den;
                    count++;
                }
            }

            if (count == 0) return 0f;
            float mssim = (float)(ssimSum / count);
            return Mathf.Clamp01(1f - mssim);
        }

        static float[] BuildGaussianKernel()
        {
            var kernel = new float[WindowSize * WindowSize];
            int half = WindowSize / 2;
            float sum = 0;
            for (int y = -half; y <= half; y++)
                for (int x = -half; x <= half; x++)
                {
                    float g = Mathf.Exp(-(x * x + y * y) / (2f * Sigma * Sigma));
                    kernel[(y + half) * WindowSize + (x + half)] = g;
                    sum += g;
                }
            for (int i = 0; i < kernel.Length; i++) kernel[i] /= sum;
            return kernel;
        }

        static float Lum(Color c) => 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
    }
}
