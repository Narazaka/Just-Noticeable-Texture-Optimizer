using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Phase2
{
    public static class ResolutionReducer
    {
        public static Texture2D Resize(Texture2D src, int targetMaxDim, bool isLinear = false)
        {
            int tw, th;
            if (src.width >= src.height)
            {
                tw = targetMaxDim;
                th = Mathf.Max(4, RoundToMultipleOf4(Mathf.RoundToInt(targetMaxDim * (float)src.height / src.width)));
            }
            else
            {
                th = targetMaxDim;
                tw = Mathf.Max(4, RoundToMultipleOf4(Mathf.RoundToInt(targetMaxDim * (float)src.width / src.height)));
            }

            var desc = new RenderTextureDescriptor(tw, th, RenderTextureFormat.ARGB32, 0)
            {
                sRGB = !isLinear,
            };
            var rt = RenderTexture.GetTemporary(desc);
            Graphics.Blit(src, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var dst = new Texture2D(tw, th, TextureFormat.RGBA32, true, isLinear);
            dst.name = src.name + "_r" + targetMaxDim;
            dst.ReadPixels(new Rect(0, 0, tw, th), 0, 0);
            dst.Apply(true);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return dst;
        }

        static int RoundToMultipleOf4(int v)
        {
            return ((v + 3) / 4) * 4;
        }
    }
}
