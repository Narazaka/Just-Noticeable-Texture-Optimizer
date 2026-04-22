using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Tests.Fixtures
{
    public static class TestTextureFactory
    {
        public static Texture2D MakeCheckerboard(int w, int h, int period = 1)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                px[y * w + x] = (((x / period) + (y / period)) & 1) == 0 ? Color.black : Color.white;
            t.SetPixels(px); t.Apply();
            return t;
        }

        public static Texture2D MakeSolid(int w, int h, Color c)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var px = new Color[w * h];
            for (int i = 0; i < px.Length; i++) px[i] = c;
            t.SetPixels(px); t.Apply();
            return t;
        }

        public static Texture2D MakeGradient(int w, int h, int quantize = 0)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float v = x / (float)Mathf.Max(1, w - 1);
                if (quantize > 0) v = Mathf.Round(v * (quantize - 1)) / Mathf.Max(1, quantize - 1);
                px[y * w + x] = new Color(v, v, v, 1f);
            }
            t.SetPixels(px); t.Apply();
            return t;
        }

        public static Texture2D MakeTilePerTilePattern(int w, int h, int tileSize)
        {
            // 各 tileSize×tileSize ブロックに別々のパターン (周期=tx+ty) を仕込む
            // bug A 検出用: tile ごとに metric score が変わるはず
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var px = new Color[w * h];
            int tilesX = Mathf.Max(1, w / tileSize);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int tx = x / tileSize;
                int ty = y / tileSize;
                int period = 1 + ((tx + ty) % 4);
                int local = ((x / period) + (y / period)) & 1;
                px[y * w + x] = local == 0 ? Color.black : Color.white;
            }
            t.SetPixels(px); t.Apply();
            return t;
        }

        public static Texture2D MakeFlatNormal(int w, int h)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var px = new Color[w * h];
            for (int i = 0; i < px.Length; i++) px[i] = new Color(0.5f, 0.5f, 1f, 1f);
            t.SetPixels(px); t.Apply();
            return t;
        }

        public static Texture2D MakeAlphaGradient(int w, int h, int quantize = 0)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float a = x / (float)Mathf.Max(1, w - 1);
                if (quantize > 0) a = Mathf.Round(a * (quantize - 1)) / Mathf.Max(1, quantize - 1);
                px[y * w + x] = new Color(0.5f, 0.5f, 0.5f, a);
            }
            t.SetPixels(px); t.Apply();
            return t;
        }
    }
}
