using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Complexity
{
    public static class ComplexitySampler
    {
        public static (Color[] pixels, int w, int h) Sample(Texture2D tex, Rect uvRect)
        {
            int tw = tex.width, th = tex.height;
            int x = Mathf.Clamp(Mathf.FloorToInt(uvRect.xMin * tw), 0, tw - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt(uvRect.yMin * th), 0, th - 1);
            int w = Mathf.Clamp(Mathf.CeilToInt(uvRect.width * tw), 1, tw - x);
            int h = Mathf.Clamp(Mathf.CeilToInt(uvRect.height * th), 1, th - y);
            var readable = tex.isReadable ? tex : MakeReadable(tex);
            var px = readable.GetPixels(x, y, w, h);
            return (px, w, h);
        }

        static Texture2D MakeReadable(Texture2D src)
        {
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
