using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.Tiling;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Gate
{
    /// <summary>
    /// Multi-scale Structural Loss の CPU reference 実装。
    /// GPU 版 (MsslMetric) の正しさ検証用、およびデバッグ用。
    /// IMetric は実装せず、Texture2D を直接受ける独自 API。
    /// </summary>
    public class MsslMetricCpu
    {
        public float[] EvaluateDebug(
            Texture2D orig, Texture2D candidate,
            UvTileGrid grid, float[] rPerTile,
            DegradationCalibration calib)
        {
            int w = orig.width, h = orig.height;
            var pxO = orig.GetPixels();
            var pxC = candidate.GetPixels();
            var lumO = ToLuminance(pxO);
            var lumC = ToLuminance(pxC);

            var pyrO = BuildPyramid(lumO, w, h);
            var pyrC = BuildPyramid(lumC, w, h);

            var scores = new float[grid.Tiles.Length];
            int tileSize = grid.TileSize;

            for (int ty = 0; ty < grid.TilesY; ty++)
            for (int tx = 0; tx < grid.TilesX; tx++)
            {
                int idx = ty * grid.TilesX + tx;
                var tile = grid.Tiles[idx];
                if (!tile.HasCoverage) continue;

                float r = rPerTile[idx];
                int targetLevel = EffectiveResolutionCalculator.LevelFromR(r, tileSize);

                float worst = 0f;
                for (int dl = -1; dl <= 1; dl++)
                {
                    int lv = Mathf.Clamp(targetLevel + dl, 0, pyrO.Length - 1);
                    float scaleWeight = 1f - Mathf.Abs(dl) * 0.3f;

                    float band = BandEnergyLoss(pyrO, pyrC, lv, tx, ty, tileSize, w, h);
                    float struc = StructureOnlyLoss(
                        pyrO[lv].data, pyrC[lv].data, pyrO[lv].w, pyrO[lv].h,
                        tx, ty, tileSize, w, h);

                    float s = Mathf.Max(
                        band * calib.MsslBandEnergyScale,
                        struc * calib.MsslStructureScale) * scaleWeight;
                    if (s > worst) worst = s;
                }
                scores[idx] = worst;
            }
            return scores;
        }

        struct Lvl { public float[] data; public int w; public int h; }

        static Lvl[] BuildPyramid(float[] src, int w, int h)
        {
            int levels = 1;
            int d = Mathf.Max(w, h);
            while (d > 1) { d >>= 1; levels++; }
            var result = new Lvl[levels];
            result[0] = new Lvl { data = (float[])src.Clone(), w = w, h = h };
            for (int i = 1; i < levels; i++)
            {
                int pw = result[i - 1].w, ph = result[i - 1].h;
                int nw = Mathf.Max(1, pw / 2), nh = Mathf.Max(1, ph / 2);
                var next = new float[nw * nh];
                for (int y = 0; y < nh; y++)
                for (int x = 0; x < nw; x++)
                {
                    int x2 = x * 2, y2 = y * 2;
                    int x3 = Mathf.Min(x2 + 1, pw - 1);
                    int y3 = Mathf.Min(y2 + 1, ph - 1);
                    next[y * nw + x] = 0.25f * (
                        result[i - 1].data[y2 * pw + x2] +
                        result[i - 1].data[y2 * pw + x3] +
                        result[i - 1].data[y3 * pw + x2] +
                        result[i - 1].data[y3 * pw + x3]);
                }
                result[i] = new Lvl { data = next, w = nw, h = nh };
            }
            return result;
        }

        static float BandEnergyLoss(Lvl[] pyrO, Lvl[] pyrC, int lv,
            int tx, int ty, int tileSize, int texW, int texH)
        {
            if (lv >= pyrO.Length - 1) return 0f;
            var o = pyrO[lv].data;
            var co = pyrC[lv].data;
            var oParent = pyrO[lv + 1].data;
            var cParent = pyrC[lv + 1].data;
            int w = pyrO[lv].w, h = pyrO[lv].h;
            int pw = pyrO[lv + 1].w;

            int x0 = tx * tileSize * w / texW;
            int y0 = ty * tileSize * h / texH;
            int x1 = Mathf.Min(w, x0 + tileSize * w / texW);
            int y1 = Mathf.Min(h, y0 + tileSize * h / texH);
            if (x1 <= x0 || y1 <= y0) return 0f;

            double eO = 0, dDiff = 0;
            for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                int px = x >> 1, py = y >> 1;
                if (px >= pw) px = pw - 1;
                float laplO = o[y * w + x] - oParent[py * pw + px];
                float laplC = co[y * w + x] - cParent[py * pw + px];
                eO += laplO * laplO;
                float d = laplO - laplC;
                dDiff += d * d;
            }
            if (eO < 1e-8) return 0f;
            return Mathf.Clamp01((float)(dDiff / eO));
        }

        static float StructureOnlyLoss(float[] a, float[] b, int w, int h,
            int tx, int ty, int tileSize, int texW, int texH)
        {
            int x0 = tx * tileSize * w / texW;
            int y0 = ty * tileSize * h / texH;
            int x1 = Mathf.Min(w, x0 + tileSize * w / texW);
            int y1 = Mathf.Min(h, y0 + tileSize * h / texH);
            if (x1 - x0 < 7 || y1 - y0 < 7) return 0f;

            double sumLoss = 0;
            int count = 0;
            const int win = 5;
            for (int y = y0 + win / 2; y < y1 - win / 2; y += 2)
            for (int x = x0 + win / 2; x < x1 - win / 2; x += 2)
            {
                float muA = 0, muB = 0;
                for (int ky = -win / 2; ky <= win / 2; ky++)
                for (int kx = -win / 2; kx <= win / 2; kx++)
                {
                    muA += a[(y + ky) * w + (x + kx)];
                    muB += b[(y + ky) * w + (x + kx)];
                }
                muA /= win * win; muB /= win * win;

                float varA = 0, varB = 0, cov = 0;
                for (int ky = -win / 2; ky <= win / 2; ky++)
                for (int kx = -win / 2; kx <= win / 2; kx++)
                {
                    float da = a[(y + ky) * w + (x + kx)] - muA;
                    float db = b[(y + ky) * w + (x + kx)] - muB;
                    varA += da * da; varB += db * db; cov += da * db;
                }
                varA /= win * win; varB /= win * win; cov /= win * win;

                const float C2 = 0.03f * 0.03f;
                const float C3 = C2 * 0.5f;
                float sigA = Mathf.Sqrt(Mathf.Max(0f, varA));
                float sigB = Mathf.Sqrt(Mathf.Max(0f, varB));
                float c_term = (2f * sigA * sigB + C2) / (varA + varB + C2);
                float s_term = (cov + C3) / (sigA * sigB + C3);
                float structSim = c_term * s_term;

                sumLoss += Mathf.Max(0f, 1f - structSim);
                count++;
            }
            return count == 0 ? 0f : (float)(sumLoss / count);
        }

        static float[] ToLuminance(Color[] px)
        {
            var r = new float[px.Length];
            for (int i = 0; i < px.Length; i++)
                r[i] = 0.2126f * px[i].r + 0.7152f * px[i].g + 0.0722f * px[i].b;
            return r;
        }
    }
}
