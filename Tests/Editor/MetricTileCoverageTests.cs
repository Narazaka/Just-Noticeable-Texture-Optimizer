using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.Gate;
using Narazaka.VRChat.Jnto.Editor.Phase2.GpuPipeline;
using Narazaka.VRChat.Jnto.Editor.Phase2.Tiling;
using Narazaka.VRChat.Jnto.Editor.Tests.Fixtures;

/// <summary>
/// 各 GPU metric が「全タイル」を実際に評価しているか検証。
/// バグA回帰防止: タイル ID を gid から取って ceil(TilesX/8) Dispatch していた頃は
/// TilesX/8 × TilesY/8 タイルしか走らず、残り 98% は score=0 になっていた。
/// 各タイルに別々のパターンを仕込み、score 分布が「ばらつく」ことを assert する。
/// </summary>
public class MetricTileCoverageTests
{
    [Test]
    public void Mssl_AllTilesEvaluated_WhenInputDiffersPerTile()
    {
        AssertTileScoreVariance(new MsslMetric(), MetricBlur());
    }

    [Test]
    public void Ridge_AllTilesEvaluated_WhenInputDiffersPerTile()
    {
        AssertTileScoreVariance(new RidgeMetric(), MetricBlur());
    }

    [Test]
    public void Banding_AllTilesEvaluated_WhenInputDiffersPerTile()
    {
        // Banding は orig が「平坦領域あり」、candidate が「量子化済み」の差で立つ
        // grid tile size は 256x256 で 32 (UvTileGrid.DetermineTileSize)、なので pattern_tile=32 で合わせる
        // orig は tile 内で slight な gradient を持ち flat region を広く確保、cand は粗く量子化して段差を作る
        var origTex = MakeTilewiseQuantizedGradient(256, 32, quantizeBase: 0);
        var candTex = MakeTilewiseQuantizedGradient(256, 32, quantizeBase: 4);
        try
        {
            AssertTileScoreVarianceWithCustomTextures(new BandingMetric(), origTex, candTex);
        }
        finally
        {
            Object.DestroyImmediate(origTex);
            Object.DestroyImmediate(candTex);
        }
    }

    [Test]
    public void BlockBoundary_AllTilesEvaluated_WhenInputDiffersPerTile()
    {
        // BlockBoundary は candidate のみ参照、4-px ブロック境界差を見る
        // grid tile size 32 に合わせて pattern_tile=32、period を tile ごとに {1,2,3} (0/1/2 mod 3)
        var candTex = MakeTilewiseStepPattern(256, 32);
        try
        {
            AssertTileScoreVarianceWithCustomTextures(new BlockBoundaryMetric(), candTex, candTex);
        }
        finally { Object.DestroyImmediate(candTex); }
    }

    [Test]
    public void AlphaQuantization_AllTilesEvaluated_WhenInputDiffersPerTile()
    {
        var origTex = MakeTilewiseAlphaGradient(256, 32, quantizeBase: 0);
        var candTex = MakeTilewiseAlphaGradient(256, 32, quantizeBase: 4);
        try
        {
            AssertTileScoreVarianceWithCustomTextures(new AlphaQuantizationMetric(), origTex, candTex);
        }
        finally
        {
            Object.DestroyImmediate(origTex);
            Object.DestroyImmediate(candTex);
        }
    }

    [Test]
    public void NormalAngle_AllTilesEvaluated_WhenInputDiffersPerTile()
    {
        // タイル別に異なる傾斜の normal map
        var origTex = TestTextureFactory.MakeFlatNormal(256, 256);
        var candTex = MakeTilewiseTiltedNormal(256, 32);
        try
        {
            AssertTileScoreVarianceWithCustomTextures(new NormalAngleMetric(), origTex, candTex);
        }
        finally
        {
            Object.DestroyImmediate(origTex);
            Object.DestroyImmediate(candTex);
        }
    }

    /// <summary>
    /// 共通: per-tile pattern を仕込んで、score の分散がゼロでないことを確認。
    /// Mssl/Ridge は orig=元、candidate=Blur 版 で score が出る
    /// </summary>
    static void AssertTileScoreVariance(IMetric metric, System.Func<Texture2D, Texture2D> blur)
    {
        // 256×256, tileSize=64 → 4×4 = 16 タイル。
        // 各タイルに違う周期の checker を仕込む
        var orig = TestTextureFactory.MakeTilePerTilePattern(256, 256, 64);
        var cand = blur(orig);
        try
        {
            AssertTileScoreVarianceWithCustomTextures(metric, orig, cand);
        }
        finally
        {
            Object.DestroyImmediate(orig);
            Object.DestroyImmediate(cand);
        }
    }

    static void AssertTileScoreVarianceWithCustomTextures(IMetric metric, Texture2D origTex, Texture2D candTex)
    {
        var grid = TestGridFactory.AllCovered(origTex.width, origTex.height);
        var r = TestGridFactory.FullR(grid);
        var calib = DegradationCalibration.Default();

        try
        {
            using (var ctxA = GpuTextureContext.FromTexture2D(origTex))
            using (var ctxB = GpuTextureContext.FromTexture2D(candTex))
            {
                var scores = new float[grid.Tiles.Length];
                metric.Evaluate(ctxA.Original, ctxB.Original, grid, r, calib, scores);

                // 分散がゼロでなければ通る (= タイル毎に違う score になっている)
                float min = float.MaxValue, max = 0f;
                int nonZeroCount = 0;
                for (int i = 0; i < scores.Length; i++)
                {
                    if (scores[i] < min) min = scores[i];
                    if (scores[i] > max) max = scores[i];
                    if (scores[i] > 1e-4f) nonZeroCount++;
                }

                // 重要: バグA存在時は最初の TilesX/8 × TilesY/8 = 1 タイル(8x8 dispatch で 1) しか
                // 評価されないので、残り 15 tile が全て score=0 のままになる。
                // 修正後は全 16 タイルで評価され、tile per pattern により score がばらつく。
                Assert.GreaterOrEqual(nonZeroCount, grid.Tiles.Length * 3 / 4,
                    $"At least 75% of tiles should have non-zero score (got {nonZeroCount}/{grid.Tiles.Length}). " +
                    "If this fails, GPU dispatch only evaluates a subset of tiles (bug A).");

                Assert.Greater(max - min, 0.01f,
                    $"Tile scores should vary based on per-tile content (max-min={max - min:F3}). " +
                    "If this fails, all tiles got the same score, suggesting only one tile was evaluated.");
            }
        }
        finally { Object.DestroyImmediate(calib); }
    }

    static System.Func<Texture2D, Texture2D> MetricBlur() => Blur5;

    static Texture2D Blur5(Texture2D src)
    {
        int w = src.width, h = src.height;
        var p = src.GetPixels();
        for (int pass = 0; pass < 5; pass++)
        {
            var q = new Color[p.Length];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                Color s = Color.black; int n = 0;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int xx = x + dx, yy = y + dy;
                    if (xx < 0 || yy < 0 || xx >= w || yy >= h) continue;
                    s += p[yy * w + xx]; n++;
                }
                q[y * w + x] = s / n;
            }
            p = q;
        }
        var dst = new Texture2D(w, h, TextureFormat.RGBA32, false);
        dst.SetPixels(p); dst.Apply();
        return dst;
    }

    static Texture2D MakeTilewiseQuantizedGradient(int n, int tileSize, int quantizeBase)
    {
        // Banding metric 用: 「平坦領域で量子化段差を検出」
        // orig (quantizeBase=0): per-tile に baseValue + 微小 gradient (<0.02/3px で flat 判定通過)
        // cand (quantizeBase>0): 微小 gradient を 4..7 レベルで粗量子化 → 段差を作る
        // タイル別に baseValue と gradient 方向をばらつかせ、score に variance を出す。
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            int tx = x / tileSize;
            int ty = y / tileSize;
            float baseValue = 0.3f + ((tx + ty) % 4) * 0.1f;   // 0.3..0.6
            float amplitude = 0.15f;                             // 3x3 max-min ~ 2*(0.15/32)*3 = 0.028; sRGB compression → OK
            // タイル内 gradient 方向 (x/y) を tile 依存で切替
            bool xDir = ((tx * 3 + ty) & 1) == 0;
            float local = xDir
                ? (x % tileSize) / (float)tileSize
                : (y % tileSize) / (float)tileSize;
            float v = baseValue + (local - 0.5f) * amplitude;
            int q = quantizeBase == 0 ? 0 : (quantizeBase + ((tx + ty) % 4));
            if (q > 0) v = Mathf.Round(v * (q - 1)) / Mathf.Max(1, q - 1);
            px[y * n + x] = new Color(v, v, v, 1f);
        }
        t.SetPixels(px); t.Apply();
        return t;
    }

    static Texture2D MakeTilewiseStepPattern(int n, int tileSize)
    {
        // BlockBoundary metric は (x & 3) == 0 のグリッド上での隣接差を見る。
        // period=4 の pure step は全 tile で ratio が saturate して同じ score になる。
        // タイル別に「noise レベル」を変えてシグナル強度を変える:
        //  - noise=0: pure step → high score
        //  - noise 大: off 列にも差分が出て ratio 低下 → low score
        // これで score に variance を出す。
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
        var px = new Color[n * n];
        // deterministic pseudo-noise via hash of (x,y)
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            int tx = x / tileSize;
            int ty = y / tileSize;
            // タイル別 step 強度 (0.1..0.7) と noise 振幅 (0..0.3) を変化
            float stepAmp = 0.1f + ((tx * 5 + ty * 7) % 7) / 6f * 0.6f;   // 0.1..0.7
            float noiseAmp = ((tx * 11 + ty * 13 + 5) % 8) / 7f * 0.3f;    // 0..0.3
            int blk = (x % tileSize) / 4;
            float step = (blk & 1) == 0 ? -0.5f * stepAmp : 0.5f * stepAmp;
            // hash-based pseudo noise, deterministic per (x,y)
            int h = (x * 73856093) ^ (y * 19349663);
            float noise = (((h & 0xffff) / 65535f) - 0.5f) * noiseAmp;
            float v = Mathf.Clamp01(0.5f + step + noise);
            px[y * n + x] = new Color(v, v, v, 1f);
        }
        t.SetPixels(px); t.Apply();
        return t;
    }

    static Texture2D MakeTilewiseAlphaGradient(int n, int tileSize, int quantizeBase)
    {
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
        var px = new Color[n * n];
        int tilesX = n / tileSize;
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            int tx = x / tileSize;
            int ty = y / tileSize;
            int q = quantizeBase == 0 ? 0 : (quantizeBase + ((tx + ty) % 4));
            float a = (x % tileSize) / (float)tileSize;
            if (q > 0) a = Mathf.Round(a * (q - 1)) / Mathf.Max(1, q - 1);
            px[y * n + x] = new Color(0.5f, 0.5f, 0.5f, a);
        }
        t.SetPixels(px); t.Apply();
        return t;
    }

    static Texture2D MakeTilewiseTiltedNormal(int n, int tileSize)
    {
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            int tx = x / tileSize;
            int ty = y / tileSize;
            // タイル別に違う傾斜
            float tiltX = ((tx + ty) % 4) * 0.2f - 0.3f;  // -0.3 .. +0.3
            float tiltY = ((tx * 3 + ty) % 4) * 0.2f - 0.3f;
            px[y * n + x] = new Color(tiltX * 0.5f + 0.5f, tiltY * 0.5f + 0.5f, 1f, 1f);
        }
        t.SetPixels(px); t.Apply();
        return t;
    }
}
