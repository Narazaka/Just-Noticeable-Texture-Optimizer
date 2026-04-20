using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Degradation
{
    public class AlphaQuantizationMetric : IDegradationMetric
    {
        public string Name => "AlphaQuantization";

        public float Evaluate(Texture2D original, Texture2D candidate)
        {
            var po = original.GetPixels();
            var pc = candidate.GetPixels();
            int n = Mathf.Min(po.Length, pc.Length);
            float mina = 1f, maxa = 0f;
            for (int i = 0; i < n; i++) { mina = Mathf.Min(mina, po[i].a); maxa = Mathf.Max(maxa, po[i].a); }
            if (maxa - mina < 0.02f) return 0f;

            double mseA = 0;
            var levels = new System.Collections.Generic.HashSet<int>();
            for (int i = 0; i < n; i++)
            {
                float d = po[i].a - pc[i].a;
                mseA += d * d;
                levels.Add(Mathf.RoundToInt(pc[i].a * 255f));
            }
            mseA /= n;
            float rmse = (float)System.Math.Sqrt(mseA);
            int levelCount = levels.Count;
            float levelScore = Mathf.Clamp01((8 - levelCount) / 8f);
            float rmseScore = Mathf.Clamp01(rmse * 20f);
            return Mathf.Max(levelScore, rmseScore);
        }
    }
}
