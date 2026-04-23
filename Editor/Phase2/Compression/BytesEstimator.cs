using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Compression
{
    /// <summary>
    /// TextureFormat ごとの bpp (bits per pixel) と、width×height×(mip chain) での
    /// バイト数推定を提供する共通ユーティリティ。
    ///
    /// R-D4-2 で NewResolutionReducePass.BytesFor / NewPhase2Pipeline の容量比較
    /// で使われる計算を一元化するために新設。
    /// </summary>
    public static class BytesEstimator
    {
        /// <summary>
        /// TextureFormat の bpp (bits per pixel) を返す。
        /// ブロック圧縮は 16 バイト / 16px = 8bpp 等に換算した値。
        /// 未知 fmt は 32bpp (= RGBA32 相当) で安全側。
        /// </summary>
        public static int BitsPerPixel(TextureFormat fmt)
        {
            switch (fmt)
            {
                // 4bpp ブロック (8 bytes / 4x4 = 128 bits / 16 pixels)
                case TextureFormat.DXT1:
                case TextureFormat.DXT1Crunched:
                case TextureFormat.BC4:
                    return 4;

                // 8bpp ブロック (16 bytes / 4x4 = 256 bits / 16 pixels)
                case TextureFormat.DXT5:
                case TextureFormat.DXT5Crunched:
                case TextureFormat.BC5:
                case TextureFormat.BC6H:
                case TextureFormat.BC7:
                    return 8;

                // 8bpp single channel
                case TextureFormat.R8:
                case TextureFormat.Alpha8:
                    return 8;

                // 16bpp
                case TextureFormat.RG16:
                case TextureFormat.R16:
                case TextureFormat.RGB565:
                case TextureFormat.ARGB4444:
                case TextureFormat.RGBA4444:
                    return 16;

                // 24bpp
                case TextureFormat.RGB24:
                    return 24;

                // 32bpp
                case TextureFormat.RGBA32:
                case TextureFormat.ARGB32:
                case TextureFormat.BGRA32:
                    return 32;

                // 64bpp
                case TextureFormat.RGBAHalf:
                    return 64;

                // 128bpp
                case TextureFormat.RGBAFloat:
                    return 128;

                default:
                    return 32; // 保守的
            }
        }

        /// <summary>
        /// mipmap なしのベースサイズ (bytes)。
        /// </summary>
        public static long BaseBytes(int width, int height, TextureFormat fmt)
        {
            if (width <= 0 || height <= 0) return 0;
            long bits = (long)width * height * BitsPerPixel(fmt);
            return bits / 8;
        }

        /// <summary>
        /// mip chain 込みのバイト数 (base × 4/3 の近似)。
        /// NewResolutionReducePass の既存 *1.33 と互換になるよう 4/3 で計算する。
        /// </summary>
        public static long WithMips(int width, int height, TextureFormat fmt)
        {
            long baseBytes = BaseBytes(width, height, fmt);
            // 1 + 1/4 + 1/16 + ... → 4/3
            return baseBytes * 4 / 3;
        }
    }
}
