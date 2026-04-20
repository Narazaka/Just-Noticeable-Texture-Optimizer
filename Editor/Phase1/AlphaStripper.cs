using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Phase1
{
    public static class AlphaStripper
    {
        public static Texture2D StripAlpha(Texture2D src)
        {
            var readable = EnsureReadable(src);
            var dst = new Texture2D(src.width, src.height, TextureFormat.RGB24, src.mipmapCount > 1);
            dst.name = src.name + "_noalpha";
            var px = readable.GetPixels();
            for (int i = 0; i < px.Length; i++) px[i].a = 1f;
            dst.SetPixels(px);
            dst.Apply(src.mipmapCount > 1);
            return dst;
        }

        static Texture2D EnsureReadable(Texture2D src)
        {
            if (src.isReadable) return src;
            var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(src, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var copy = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
            copy.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return copy;
        }
    }
}
