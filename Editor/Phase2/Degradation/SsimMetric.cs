using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Degradation
{
    public class SsimMetric : IDegradationMetric
    {
        public string Name => "SSIM";

        public float Evaluate(Texture2D a, Texture2D b)
        {
            var pa = a.GetPixels();
            var pb = b.GetPixels();
            int n = Mathf.Min(pa.Length, pb.Length);
            float muA = 0, muB = 0;
            for (int i = 0; i < n; i++) { muA += Lum(pa[i]); muB += Lum(pb[i]); }
            muA /= n; muB /= n;
            float varA = 0, varB = 0, cov = 0;
            for (int i = 0; i < n; i++)
            {
                float da = Lum(pa[i]) - muA;
                float db = Lum(pb[i]) - muB;
                varA += da * da; varB += db * db; cov += da * db;
            }
            varA /= n; varB /= n; cov /= n;
            const float C1 = 0.01f * 0.01f;
            const float C2 = 0.03f * 0.03f;
            float num = (2 * muA * muB + C1) * (2 * cov + C2);
            float den = (muA * muA + muB * muB + C1) * (varA + varB + C2);
            return num / den;
        }

        static float Lum(Color c) => 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
    }
}
