using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Degradation
{
    /// <summary>
    /// NVIDIA FLIP (LDR) の C# 実装。
    /// カラーパイプライン (Hunt調整 + CSFフィルタ + HyAB色差) と
    /// フィーチャーパイプライン (ガウシアン微分によるエッジ/ポイント検出) を統合。
    /// </summary>
    public class FlipMetric : IPerPixelMetric
    {
        public string Name => "FLIP";

        const float Qc = 0.7f;
        const float Pc = 0.4f;
        const float Pt = 0.95f;
        const float Gw = 0.082f;
        const float Qf = 0.5f;
        const float DefaultPpd = 60.5f;

        public float Evaluate(Texture2D original, Texture2D candidate)
        {
            var scores = EvaluatePerPixel(original, candidate);
            if (scores == null || scores.Length == 0) return 0f;
            double sum = 0;
            for (int i = 0; i < scores.Length; i++) sum += scores[i];
            return (float)(sum / scores.Length);
        }

        public float[] EvaluatePerPixel(Texture2D original, Texture2D candidate)
        {
            int w = original.width, h = original.height;
            var pxO = original.GetPixels();
            var pxC = candidate.GetPixels();
            if (pxO.Length != pxC.Length || w < 3 || h < 3) return null;

            float ppd = DefaultPpd;

            var ycxczO = new Vector3[w * h];
            var ycxczC = new Vector3[w * h];
            var lumO = new float[w * h];
            var lumC = new float[w * h];
            for (int i = 0; i < pxO.Length; i++)
            {
                ycxczO[i] = SrgbToYCxCz(pxO[i]);
                ycxczC[i] = SrgbToYCxCz(pxC[i]);
                lumO[i] = 0.2126f * SrgbToLin(pxO[i].r) + 0.7152f * SrgbToLin(pxO[i].g) + 0.0722f * SrgbToLin(pxO[i].b);
                lumC[i] = 0.2126f * SrgbToLin(pxC[i].r) + 0.7152f * SrgbToLin(pxC[i].g) + 0.0722f * SrgbToLin(pxC[i].b);
            }

            CsfFilter(ycxczO, w, h, ppd);
            CsfFilter(ycxczC, w, h, ppd);

            float cmax = ComputeCmax();
            float pccmax = Pc * cmax;
            var colorDiff = new float[w * h];
            for (int i = 0; i < pxO.Length; i++)
            {
                float d = HyAB(ycxczO[i], ycxczC[i]);
                d = Mathf.Pow(d, Qc);
                if (d < pccmax)
                    d *= Pt / pccmax;
                else
                    d = Pt + ((d - pccmax) / (cmax - pccmax)) * (1f - Pt);
                colorDiff[i] = Mathf.Clamp01(d);
            }

            var featureDiff = ComputeFeatureDiff(lumO, lumC, w, h, ppd);

            var result = new float[w * h];
            for (int i = 0; i < pxO.Length; i++)
                result[i] = Mathf.Pow(colorDiff[i], Mathf.Max(1f - featureDiff[i], 0.001f));

            return result;
        }

        static Vector3 SrgbToYCxCz(Color c)
        {
            float r = SrgbToLin(c.r), g = SrgbToLin(c.g), b = SrgbToLin(c.b);
            float X = 0.4124564f * r + 0.3575761f * g + 0.1804375f * b;
            float Y = 0.2126729f * r + 0.7151522f * g + 0.0721750f * b;
            float Z = 0.0193339f * r + 0.1191920f * g + 0.9503041f * b;
            float fX = LabF(X / 0.95047f);
            float fY = LabF(Y);
            float fZ = LabF(Z / 1.08883f);
            float Yy = 116f * fY - 16f;
            float Cx = 500f * (fX - fY);
            float Cz = 200f * (fY - fZ);
            Cx += 0.01f * Yy * Cx;
            Cz += 0.01f * Yy * Cz;
            return new Vector3(Yy, Cx, Cz);
        }

        static float LabF(float t) => t > 0.008856f ? Mathf.Pow(t, 1f / 3f) : 7.787f * t + 16f / 116f;

        static float SrgbToLin(float v) => v <= 0.04045f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f);

        static float HyAB(Vector3 a, Vector3 b)
        {
            float dL = Mathf.Abs(a.x - b.x);
            float dCx = a.y - b.y, dCz = a.z - b.z;
            return dL + Mathf.Sqrt(dCx * dCx + dCz * dCz);
        }

        static float ComputeCmax()
        {
            // HyAB distance between Hunt-adjusted green and blue at max luminance
            var green = SrgbToYCxCz(new Color(0, 1, 0, 1));
            var blue = SrgbToYCxCz(new Color(0, 0, 1, 1));
            return Mathf.Pow(HyAB(green, blue), Qc);
        }

        // CSF filtering: 2-component Gaussian per YCxCz channel
        static void CsfFilter(Vector3[] img, int w, int h, float ppd)
        {
            // Y: b1=0.0047, Cx: b1=0.0053, Cz: b1=0.04/b2=0.025
            FilterChannel(img, w, h, 0, ppd, 1f, 0.0047f, 0f, 1e-5f);
            FilterChannel(img, w, h, 1, ppd, 1f, 0.0053f, 0f, 1e-5f);
            FilterChannel(img, w, h, 2, ppd, 34.1f, 0.04f, 13.5f, 0.025f);
        }

        static void FilterChannel(Vector3[] img, int w, int h, int ch, float ppd, float a1, float b1, float a2, float b2)
        {
            float maxB = Mathf.Max(b1, b2);
            int radius = Mathf.CeilToInt(3f * Mathf.Sqrt(maxB / (2f * Mathf.PI * Mathf.PI)) * ppd);
            radius = Mathf.Min(radius, Mathf.Min(w, h) / 2 - 1);
            if (radius < 1) return;

            int kSize = 2 * radius + 1;
            var kernel = new float[kSize];
            float kSum = 0;
            for (int i = 0; i < kSize; i++)
            {
                float x = (i - radius) / ppd;
                float x2 = x * x;
                kernel[i] = a1 * Mathf.Sqrt(Mathf.PI / b1) * Mathf.Exp(-Mathf.PI * Mathf.PI * x2 / b1);
                if (a2 > 0) kernel[i] += a2 * Mathf.Sqrt(Mathf.PI / b2) * Mathf.Exp(-Mathf.PI * Mathf.PI * x2 / b2);
                kSum += kernel[i];
            }
            for (int i = 0; i < kSize; i++) kernel[i] /= kSum;

            // Separable: horizontal then vertical
            var tmp = new float[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float sum = 0;
                    for (int k = -radius; k <= radius; k++)
                    {
                        int sx = Mathf.Clamp(x + k, 0, w - 1);
                        sum += GetCh(img[y * w + sx], ch) * kernel[k + radius];
                    }
                    tmp[y * w + x] = sum;
                }
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float sum = 0;
                    for (int k = -radius; k <= radius; k++)
                    {
                        int sy = Mathf.Clamp(y + k, 0, h - 1);
                        sum += tmp[sy * w + x] * kernel[k + radius];
                    }
                    SetCh(ref img[y * w + x], ch, sum);
                }
        }

        static float GetCh(Vector3 v, int ch) => ch == 0 ? v.x : ch == 1 ? v.y : v.z;
        static void SetCh(ref Vector3 v, int ch, float val) { if (ch == 0) v.x = val; else if (ch == 1) v.y = val; else v.z = val; }

        // Feature detection: Gaussian 1st/2nd derivatives
        static float[] ComputeFeatureDiff(float[] lumO, float[] lumC, int w, int h, float ppd)
        {
            float sigma = 0.5f * Gw * ppd;
            int radius = Mathf.CeilToInt(3f * sigma);
            radius = Mathf.Min(radius, Mathf.Min(w, h) / 2 - 1);
            if (radius < 1)
            {
                var zero = new float[w * h];
                return zero;
            }

            int kSize = 2 * radius + 1;
            var g = new float[kSize];
            var dg = new float[kSize];
            var ddg = new float[kSize];

            for (int i = 0; i < kSize; i++)
            {
                float x = i - radius;
                float g0 = Mathf.Exp(-x * x / (2f * sigma * sigma));
                g[i] = g0;
                dg[i] = -x * g0;
                ddg[i] = (x * x / (sigma * sigma) - 1f) * g0;
            }

            var edgeO = DetectEdges(lumO, w, h, g, dg, radius);
            var edgeC = DetectEdges(lumC, w, h, g, dg, radius);
            var pointO = DetectPoints(lumO, w, h, g, ddg, radius);
            var pointC = DetectPoints(lumC, w, h, g, ddg, radius);

            float maxEdge = 1e-6f, maxPoint = 1e-6f;
            for (int i = 0; i < w * h; i++)
            {
                maxEdge = Mathf.Max(maxEdge, Mathf.Max(edgeO[i], edgeC[i]));
                maxPoint = Mathf.Max(maxPoint, Mathf.Max(pointO[i], pointC[i]));
            }

            var result = new float[w * h];
            float invSqrt2 = 1f / Mathf.Sqrt(2f);
            for (int i = 0; i < w * h; i++)
            {
                float edgeDiff = Mathf.Abs(edgeO[i] - edgeC[i]) / maxEdge;
                float pointDiff = Mathf.Abs(pointO[i] - pointC[i]) / maxPoint;
                float f = invSqrt2 * Mathf.Max(edgeDiff, pointDiff);
                result[i] = Mathf.Pow(Mathf.Clamp01(f), Qf);
            }
            return result;
        }

        static float[] DetectEdges(float[] lum, int w, int h, float[] g, float[] dg, int r)
        {
            var dx = SepConv2D(lum, w, h, dg, g, r);
            var dy = SepConv2D(lum, w, h, g, dg, r);
            var result = new float[w * h];
            for (int i = 0; i < result.Length; i++)
                result[i] = Mathf.Sqrt(dx[i] * dx[i] + dy[i] * dy[i]);
            return result;
        }

        static float[] DetectPoints(float[] lum, int w, int h, float[] g, float[] ddg, int r)
        {
            var ddx = SepConv2D(lum, w, h, ddg, g, r);
            var ddy = SepConv2D(lum, w, h, g, ddg, r);
            var result = new float[w * h];
            for (int i = 0; i < result.Length; i++)
                result[i] = Mathf.Sqrt(ddx[i] * ddx[i] + ddy[i] * ddy[i]);
            return result;
        }

        static float[] SepConv2D(float[] src, int w, int h, float[] kx, float[] ky, int r)
        {
            var tmp = new float[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float sum = 0;
                    for (int k = -r; k <= r; k++)
                        sum += src[y * w + Mathf.Clamp(x + k, 0, w - 1)] * kx[k + r];
                    tmp[y * w + x] = sum;
                }
            var dst = new float[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float sum = 0;
                    for (int k = -r; k <= r; k++)
                        sum += tmp[Mathf.Clamp(y + k, 0, h - 1) * w + x] * ky[k + r];
                    dst[y * w + x] = sum;
                }
            return dst;
        }
    }
}
