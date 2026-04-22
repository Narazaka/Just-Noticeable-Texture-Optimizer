using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Degradation
{
    /// <summary>
    /// ノーマルマップ専用メトリクス: 法線の局所分散保存度で質的変化を検出。
    /// 布の織り目などの高周波法線パターンが縮小で平滑化されると局所分散が低下する。
    /// 5x5ウィンドウで法線方向の分散を計算し、分散低下率でスコア化。
    /// </summary>
    public class NormalVarianceMetric : IPerPixelMetric
    {
        public string Name => "NormalVariance";
        const int WindowHalf = 2;

        public float Evaluate(Texture2D original, Texture2D candidate)
        {
            var scores = EvaluatePerPixel(original, candidate);
            if (scores == null || scores.Length == 0) return 0f;

            var sorted = new float[scores.Length];
            System.Array.Copy(scores, sorted, scores.Length);
            System.Array.Sort(sorted);
            int p99 = Mathf.Min((int)(sorted.Length * 0.99f), sorted.Length - 1);
            return sorted[p99];
        }

        public float[] EvaluatePerPixel(Texture2D original, Texture2D candidate)
        {
            int w = original.width, h = original.height;
            var po = original.GetPixels();
            var pc = candidate.GetPixels();
            if (po.Length != pc.Length || w < 5 || h < 5) return null;

            var normO = new Vector3[po.Length];
            var normC = new Vector3[pc.Length];
            for (int i = 0; i < po.Length; i++)
            {
                normO[i] = DecodeNormal(po[i]);
                normC[i] = DecodeNormal(pc[i]);
            }

            int count = (w - WindowHalf * 2) * (h - WindowHalf * 2);
            var scores = new float[po.Length];

            for (int y = WindowHalf; y < h - WindowHalf; y++)
            {
                for (int x = WindowHalf; x < w - WindowHalf; x++)
                {
                    float varO = LocalVariance(normO, x, y, w);
                    float varC = LocalVariance(normC, x, y, w);

                    float loss = 0f;
                    if (varO > 1e-6f)
                        loss = Mathf.Clamp01((varO - varC) / varO);

                    scores[y * w + x] = loss;
                }
            }

            return scores;
        }

        static float LocalVariance(Vector3[] normals, int cx, int cy, int w)
        {
            Vector3 mean = Vector3.zero;
            int count = 0;
            for (int dy = -WindowHalf; dy <= WindowHalf; dy++)
                for (int dx = -WindowHalf; dx <= WindowHalf; dx++)
                {
                    mean += normals[(cy + dy) * w + (cx + dx)];
                    count++;
                }
            mean /= count;

            float variance = 0;
            for (int dy = -WindowHalf; dy <= WindowHalf; dy++)
                for (int dx = -WindowHalf; dx <= WindowHalf; dx++)
                {
                    var diff = normals[(cy + dy) * w + (cx + dx)] - mean;
                    variance += Vector3.Dot(diff, diff);
                }
            return variance / count;
        }

        static Vector3 DecodeNormal(Color c)
        {
            float nx = c.r * 2f - 1f;
            float ny = c.g * 2f - 1f;
            float nz = Mathf.Sqrt(Mathf.Max(0f, 1f - nx * nx - ny * ny));
            float len = Mathf.Sqrt(nx * nx + ny * ny + nz * nz);
            if (len < 1e-6f) return Vector3.up;
            return new Vector3(nx / len, ny / len, nz / len);
        }
    }
}
