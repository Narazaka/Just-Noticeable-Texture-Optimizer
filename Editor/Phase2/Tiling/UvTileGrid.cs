using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Tiling
{
    public class UvTileGrid
    {
        public int TextureWidth;
        public int TextureHeight;
        public int TileSize;
        public int TilesX;
        public int TilesY;
        public TileStats[] Tiles;  // length = TilesX * TilesY

        /// <summary>
        /// 入力テクスチャの最大辺から、望ましいタイルサイズを決定する。
        /// 式: clamp(nearestPow2(max(W,H) / 8), 16, 64)
        /// 大きいテクスチャ (>=512) は 64 固定、小さいテクスチャは縮小適応する。
        /// </summary>
        public static int DetermineTileSize(int textureWidth, int textureHeight)
        {
            int maxDim = Mathf.Max(textureWidth, textureHeight);
            int raw = Mathf.Max(1, maxDim / 8);
            int pot = 1;
            while (pot < raw) pot <<= 1;
            return Mathf.Clamp(pot, 16, 64);
        }

        public static UvTileGrid Create(int textureWidth, int textureHeight)
        {
            int tileSize = DetermineTileSize(textureWidth, textureHeight);
            int tx = Mathf.Max(1, (textureWidth + tileSize - 1) / tileSize);
            int ty = Mathf.Max(1, (textureHeight + tileSize - 1) / tileSize);
            return new UvTileGrid
            {
                TextureWidth = textureWidth,
                TextureHeight = textureHeight,
                TileSize = tileSize,
                TilesX = tx,
                TilesY = ty,
                Tiles = new TileStats[tx * ty],
            };
        }

        public ref TileStats GetTile(int tx, int ty) => ref Tiles[ty * TilesX + tx];
    }
}
