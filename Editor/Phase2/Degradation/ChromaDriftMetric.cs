using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Degradation
{
    public class ChromaDriftMetric : IDegradationMetric
    {
        public string Name => "ChromaDrift";

        public float Evaluate(Texture2D original, Texture2D candidate)
        {
            var po = original.GetPixels();
            var pc = candidate.GetPixels();
            int n = Mathf.Min(po.Length, pc.Length);
            double maxDe = 0;
            for (int i = 0; i < n; i++)
            {
                var la = RgbToLab(po[i]);
                var lb = RgbToLab(pc[i]);
                double dL = la.x - lb.x, da = la.y - lb.y, db = la.z - lb.z;
                double de = System.Math.Sqrt(dL*dL + da*da + db*db);
                if (de > maxDe) maxDe = de;
            }
            return Mathf.Clamp01((float)(maxDe / 10.0));
        }

        static Vector3 RgbToLab(Color c)
        {
            float r = SrgbToLin(c.r), g = SrgbToLin(c.g), b = SrgbToLin(c.b);
            float X = r*0.4124564f + g*0.3575761f + b*0.1804375f;
            float Y = r*0.2126729f + g*0.7151522f + b*0.0721750f;
            float Z = r*0.0193339f + g*0.1191920f + b*0.9503041f;
            X /= 0.95047f; Z /= 1.08883f;
            float fx = F(X), fy = F(Y), fz = F(Z);
            return new Vector3(116f*fy - 16f, 500f*(fx - fy), 200f*(fy - fz));
        }

        static float SrgbToLin(float v) => v <= 0.04045f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f);
        static float F(float t) => t > 0.008856f ? Mathf.Pow(t, 1f/3f) : (7.787f * t + 16f/116f);
    }
}
