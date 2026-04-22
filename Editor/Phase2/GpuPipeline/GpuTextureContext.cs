using System;
using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.GpuPipeline
{
    /// <summary>
    /// 1 テクスチャの GPU 上コンテキスト (orig RT + mipmap chain)。
    /// 1 ビルド実行中は使い回し、Dispose で RT 解放。
    /// </summary>
    public class GpuTextureContext : IDisposable
    {
        public RenderTexture Original { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }

        public static GpuTextureContext FromTexture2D(Texture2D src)
        {
            if (src == null) throw new ArgumentNullException(nameof(src));

            var desc = new RenderTextureDescriptor(src.width, src.height, RenderTextureFormat.ARGB32, 0)
            {
                useMipMap = true,
                autoGenerateMips = false,
                sRGB = true,
            };
            var rt = new RenderTexture(desc) { name = "Jnto_Orig_" + src.name };
            rt.Create();

            Graphics.Blit(src, rt);
            rt.GenerateMips();

            return new GpuTextureContext
            {
                Original = rt,
                Width = src.width,
                Height = src.height,
            };
        }

        public void Dispose()
        {
            if (Original != null)
            {
                Original.Release();
                UnityEngine.Object.DestroyImmediate(Original);
                Original = null;
            }
        }
    }
}
