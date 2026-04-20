using UnityEngine;
using Narazaka.VRChat.Jnto.Complexity;

namespace Narazaka.VRChat.Jnto.Editor.Complexity
{
    [CreateAssetMenu(menuName = "Just-Noticeable Texture Optimizer/Complexity/FFT (DCT approximation)", fileName = "FftComplexity")]
    public class FftComplexityStrategy : ComplexityStrategyAsset
    {
        public override float Measure(Color[] region, int w, int h)
        {
            int N = 32;
            if (w < 4 || h < 4) return 0.5f;
            var small = ResampleToLuma(region, w, h, N);
            var dct = Dct2D(small, N);

            double total = 0, high = 0;
            int halfN = N / 2;
            for (int v = 0; v < N; v++)
                for (int u = 0; u < N; u++)
                {
                    double e = dct[v*N+u] * dct[v*N+u];
                    total += e;
                    if (u >= halfN || v >= halfN) high += e;
                }
            if (total < 1e-9) return 0f;
            return Mathf.Clamp01((float)(high / total * 2.0));
        }

        static float[] ResampleToLuma(Color[] region, int w, int h, int N)
        {
            var r = new float[N*N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    int sx = x * w / N;
                    int sy = y * h / N;
                    var c = region[sy*w+sx];
                    r[y*N+x] = 0.299f*c.r + 0.587f*c.g + 0.114f*c.b;
                }
            return r;
        }

        static float[] Dct2D(float[] a, int N)
        {
            var row = new float[N*N];
            for (int y = 0; y < N; y++)
                for (int u = 0; u < N; u++)
                {
                    double s = 0;
                    for (int x = 0; x < N; x++)
                        s += a[y*N+x] * System.Math.Cos(System.Math.PI * (2*x+1) * u / (2.0 * N));
                    row[y*N+u] = (float)(s * (u == 0 ? System.Math.Sqrt(1.0/N) : System.Math.Sqrt(2.0/N)));
                }
            var o = new float[N*N];
            for (int v = 0; v < N; v++)
                for (int u = 0; u < N; u++)
                {
                    double s = 0;
                    for (int y = 0; y < N; y++)
                        s += row[y*N+u] * System.Math.Cos(System.Math.PI * (2*y+1) * v / (2.0 * N));
                    o[v*N+u] = (float)(s * (v == 0 ? System.Math.Sqrt(1.0/N) : System.Math.Sqrt(2.0/N)));
                }
            return o;
        }
    }
}
