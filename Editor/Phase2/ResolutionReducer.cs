using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Phase2
{
    public static class ResolutionReducer
    {
        public static Texture2D Resize(Texture2D src, int targetSize)
        {
            var rt = RenderTexture.GetTemporary(targetSize, targetSize, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(src, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var dst = new Texture2D(targetSize, targetSize, TextureFormat.RGBA32, true);
            dst.name = src.name + "_r" + targetSize;
            dst.ReadPixels(new Rect(0, 0, targetSize, targetSize), 0, 0);
            dst.Apply(true);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return dst;
        }
    }
}
