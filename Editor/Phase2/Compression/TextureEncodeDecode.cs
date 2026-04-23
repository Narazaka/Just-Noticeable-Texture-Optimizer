using UnityEditor;
using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Compression
{
    public static class TextureEncodeDecode
    {
        public static void EnableStreamingMipmaps(Texture2D tex)
        {
            var so = new SerializedObject(tex);
            var sp = so.FindProperty("m_StreamingMipmaps");
            if (sp != null)
            {
                sp.boolValue = true;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        /// <summary>
        /// DXT5nm → BC5 変換時に必要なチャンネル再マッピング。
        /// DXT5nm は X=Alpha, Y=Green で格納するが、BC5 は R,G の 2ch のみ。
        /// lilToon は normalTex.a *= normalTex.r してから ag を読むため、
        /// BC5.R = 元の X (Alpha), BC5.G = 元の Y (Green) に再配置すれば互換。
        /// </summary>
        public static bool NeedsDxt5nmToBC5Remap(TextureFormat srcOrigFmt, TextureFormat dstFmt)
        {
            return dstFmt == TextureFormat.BC5
                && (srcOrigFmt == TextureFormat.DXT5 || srcOrigFmt == TextureFormat.DXT5Crunched);
        }

        public static void RemapDxt5nmForBC5(Texture2D tex)
        {
            var px = tex.GetPixels();
            for (int i = 0; i < px.Length; i++)
            {
                // A→R (X component), G stays (Y component)
                px[i] = new Color(px[i].a, px[i].g, 0f, 1f);
            }
            tex.SetPixels(px);
            tex.Apply(updateMipmaps: true);
        }

        public static Texture2D EncodeAndDecode(Texture2D src, TextureFormat fmt, bool isLinear = false,
            TextureFormat srcOriginalFormat = TextureFormat.RGBA32)
        {
            var compressed = new Texture2D(src.width, src.height, TextureFormat.RGBA32, true, isLinear);
            compressed.SetPixels(src.GetPixels());
            compressed.Apply(updateMipmaps: true);
            if (NeedsDxt5nmToBC5Remap(srcOriginalFormat, fmt))
                RemapDxt5nmForBC5(compressed);
            EditorUtility.CompressTexture(compressed, fmt, TextureCompressionQuality.Normal);

            var desc = new RenderTextureDescriptor(src.width, src.height, RenderTextureFormat.ARGB32, 0)
            {
                sRGB = !isLinear,
            };
            var rt = RenderTexture.GetTemporary(desc);
            Graphics.Blit(compressed, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var decoded = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false, isLinear);
            decoded.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
            decoded.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            Object.DestroyImmediate(compressed);
            return decoded;
        }
    }
}
