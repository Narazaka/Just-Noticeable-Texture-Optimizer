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
            if (n == 0) return 0f;

            var deltas = new float[n];
            for (int i = 0; i < n; i++)
                deltas[i] = (float)DeltaE00(RgbToLab(po[i]), RgbToLab(pc[i]));

            System.Array.Sort(deltas);
            float p99 = deltas[Mathf.Min((int)(n * 0.99f), n - 1)];
            return Mathf.Clamp01(p99 / 10f);
        }

        static Vector3 RgbToLab(Color c)
        {
            float r = SrgbToLin(c.r), g = SrgbToLin(c.g), b = SrgbToLin(c.b);
            float X = (r * 0.4124564f + g * 0.3575761f + b * 0.1804375f) / 0.95047f;
            float Y = r * 0.2126729f + g * 0.7151522f + b * 0.0721750f;
            float Z = (r * 0.0193339f + g * 0.1191920f + b * 0.9503041f) / 1.08883f;
            return new Vector3(116f * F(Y) - 16f, 500f * (F(X) - F(Y)), 200f * (F(Y) - F(Z)));
        }

        static float SrgbToLin(float v) => v <= 0.04045f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f);
        static float F(float t) => t > 0.008856f ? Mathf.Pow(t, 1f / 3f) : 7.787f * t + 16f / 116f;

        static double DeltaE00(Vector3 lab1, Vector3 lab2)
        {
            double L1 = lab1.x, a1 = lab1.y, b1 = lab1.z;
            double L2 = lab2.x, a2 = lab2.y, b2 = lab2.z;

            double Lb = (L1 + L2) / 2.0;
            double C1 = System.Math.Sqrt(a1 * a1 + b1 * b1);
            double C2 = System.Math.Sqrt(a2 * a2 + b2 * b2);
            double Cb = (C1 + C2) / 2.0;

            double Cb7 = System.Math.Pow(Cb, 7);
            double G = 0.5 * (1.0 - System.Math.Sqrt(Cb7 / (Cb7 + 6103515625.0)));
            double a1p = a1 * (1.0 + G);
            double a2p = a2 * (1.0 + G);
            double C1p = System.Math.Sqrt(a1p * a1p + b1 * b1);
            double C2p = System.Math.Sqrt(a2p * a2p + b2 * b2);
            double Cbp = (C1p + C2p) / 2.0;

            double h1p = System.Math.Atan2(b1, a1p) * 180.0 / System.Math.PI;
            if (h1p < 0) h1p += 360.0;
            double h2p = System.Math.Atan2(b2, a2p) * 180.0 / System.Math.PI;
            if (h2p < 0) h2p += 360.0;

            double Hbp;
            if (System.Math.Abs(h1p - h2p) <= 180.0)
                Hbp = (h1p + h2p) / 2.0;
            else if (h1p + h2p < 360.0)
                Hbp = (h1p + h2p + 360.0) / 2.0;
            else
                Hbp = (h1p + h2p - 360.0) / 2.0;

            double T = 1.0
                - 0.17 * System.Math.Cos((Hbp - 30.0) * System.Math.PI / 180.0)
                + 0.24 * System.Math.Cos((2.0 * Hbp) * System.Math.PI / 180.0)
                + 0.32 * System.Math.Cos((3.0 * Hbp + 6.0) * System.Math.PI / 180.0)
                - 0.20 * System.Math.Cos((4.0 * Hbp - 63.0) * System.Math.PI / 180.0);

            double dhp;
            if (System.Math.Abs(h2p - h1p) <= 180.0)
                dhp = h2p - h1p;
            else if (h2p - h1p > 180.0)
                dhp = h2p - h1p - 360.0;
            else
                dhp = h2p - h1p + 360.0;

            double dLp = L2 - L1;
            double dCp = C2p - C1p;
            double dHp = 2.0 * System.Math.Sqrt(C1p * C2p) * System.Math.Sin(dhp * System.Math.PI / 360.0);

            double Lb50sq = (Lb - 50.0) * (Lb - 50.0);
            double SL = 1.0 + 0.015 * Lb50sq / System.Math.Sqrt(20.0 + Lb50sq);
            double SC = 1.0 + 0.045 * Cbp;
            double SH = 1.0 + 0.015 * Cbp * T;

            double Cbp7 = System.Math.Pow(Cbp, 7);
            double RC = 2.0 * System.Math.Sqrt(Cbp7 / (Cbp7 + 6103515625.0));
            double dTheta = 30.0 * System.Math.Exp(-((Hbp - 275.0) / 25.0) * ((Hbp - 275.0) / 25.0));
            double RT = -System.Math.Sin(2.0 * dTheta * System.Math.PI / 180.0) * RC;

            double t1 = dLp / SL, t2 = dCp / SC, t3 = dHp / SH;
            return System.Math.Sqrt(t1 * t1 + t2 * t2 + t3 * t3 + RT * t2 * t3);
        }
    }
}
