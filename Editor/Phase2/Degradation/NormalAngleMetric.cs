using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Degradation
{
    /// <summary>
    /// ノーマルマップ専用メトリクス: 法線ベクトル間の角度差で劣化を評価。
    /// ピクセルRGをtangent space法線としてデコードし、dot productで角度差を算出。
    /// 99thパーセンタイルの角度差を正規化して返す。
    /// </summary>
    public class NormalAngleMetric : IPerPixelMetric
    {
        public string Name => "NormalAngle";

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
            var po = original.GetPixels();
            var pc = candidate.GetPixels();
            int n = Mathf.Min(po.Length, pc.Length);
            if (n == 0) return null;

            var scores = new float[n];
            for (int i = 0; i < n; i++)
            {
                var no = DecodeNormal(po[i]);
                var nc = DecodeNormal(pc[i]);
                float dot = Mathf.Clamp(Vector3.Dot(no, nc), -1f, 1f);
                float angle = Mathf.Acos(dot);
                scores[i] = Mathf.Clamp01(angle / (Mathf.PI * 0.5f));
            }
            return scores;
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
