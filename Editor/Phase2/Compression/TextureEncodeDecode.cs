using UnityEditor;
using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Compression
{
    public static class TextureEncodeDecode
    {
        public static Texture2D EncodeAndDecode(Texture2D src, TextureFormat fmt)
        {
            // バグ#5 回帰防止: verify 用の EncodeAndDecode と最終 Encode が同じ
            // mipchain (= true) + CompressTexture 呼び出しを共有することで、
            // verify で Pass した圧縮結果と最終出力が決定論的に一致する。
            var compressed = new Texture2D(src.width, src.height, TextureFormat.RGBA32, true);
            compressed.SetPixels(src.GetPixels());
            compressed.Apply(updateMipmaps: true);
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
