using UnityEditor;
using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Compression
{
    public static class TextureEncodeDecode
    {
        public static Texture2D EncodeAndDecode(Texture2D src, TextureFormat fmt)
        {
            var compressed = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
            compressed.SetPixels(src.GetPixels());
            compressed.Apply();
            EditorUtility.CompressTexture(compressed, fmt, TextureCompressionQuality.Normal);

            var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(compressed, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var decoded = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
            decoded.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
            decoded.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            Object.DestroyImmediate(compressed);
            return decoded;
        }
    }
}
