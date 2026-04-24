using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Compression
{
    public static class ResolutionReducer
    {
        /// <summary>Max-dim ベースの縮小。非 POT は AspectSizeCalculator で短辺を丸める。</summary>
        public static Texture2D Resize(Texture2D src, int targetMaxDim, bool isLinear = false)
        {
            var (tw, th) = AspectSizeCalculator.Compute(src.width, src.height, targetMaxDim);
            return ResizeToSize(src, tw, th, isLinear);
        }

        /// <summary>(w, h) を直接指定する縮小。CompressionCandidateEnumerator の結果をそのまま渡す経路で使う。</summary>
        public static Texture2D ResizeToSize(Texture2D src, int width, int height, bool isLinear = false)
        {
            var desc = new RenderTextureDescriptor(width, height, RenderTextureFormat.ARGB32, 0)
            {
                sRGB = !isLinear,
            };
            var rt = RenderTexture.GetTemporary(desc);
            Graphics.Blit(src, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var dst = new Texture2D(width, height, TextureFormat.RGBA32, true, isLinear);
            dst.name = src.name + "_r" + Mathf.Max(width, height);
            dst.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            dst.Apply(true);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return dst;
        }
    }
}
