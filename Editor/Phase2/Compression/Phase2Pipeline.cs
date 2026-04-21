using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.Degradation;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Compression
{
    public class Phase2Result
    {
        public Texture2D Final;
        public int Size;
        public TextureFormat Format;
        public string FallbackReason;
    }

    public class Phase2Pipeline
    {
        readonly DegradationGate _gate = new DegradationGate();

        public Phase2Result Find(Texture2D original, int targetSize, TextureRole role, QualityPreset preset)
        {
            var chain = CompressionChain.For(role);
            int origMax = Mathf.Max(original.width, original.height);
            var origFmt = original.format;

            // 高圧縮形式 (DXT系) を全サイズで先に試行、BC7 は後回し
            var primaryFmts = System.Array.FindAll(chain, f => f != TextureFormat.BC7);
            var fallbackFmts = System.Array.FindAll(chain, f => f == TextureFormat.BC7);

            // Pass 1: DXT 系を各サイズで試行
            var result = TrySizes(original, targetSize, origMax, primaryFmts, preset);
            if (result != null) return result;

            // Pass 2: BC7 を各サイズで試行 (DXT 全滅時のみ)
            if (fallbackFmts.Length > 0)
            {
                result = TrySizes(original, targetSize, origMax, fallbackFmts, preset);
                if (result != null) return result;
            }

            // origSize: 元テクスチャをそのまま返す
            return new Phase2Result { Final = original, Size = origMax, Format = origFmt };
        }

        Phase2Result TrySizes(Texture2D original, int targetSize, int origMax, TextureFormat[] fmts, QualityPreset preset)
        {
            for (int size = targetSize; size < origMax; size *= 2)
            {
                var resized = ResolutionReducer.Resize(original, size);
                var originalForCompare = ResolutionReducer.Resize(original, size);

                foreach (var fmt in fmts)
                {
                    var decoded = TextureEncodeDecode.EncodeAndDecode(resized, fmt);
                    bool pass = _gate.Passes(originalForCompare, decoded, preset, out _);
                    Object.DestroyImmediate(decoded);

                    if (pass)
                    {
                        var final = CreateCompressed(resized, size, fmt);
                        Object.DestroyImmediate(originalForCompare);
                        Object.DestroyImmediate(resized);
                        return new Phase2Result { Final = final, Size = size, Format = fmt };
                    }
                }

                Object.DestroyImmediate(originalForCompare);
                Object.DestroyImmediate(resized);
            }
            return null;
        }

        static Texture2D CreateCompressed(Texture2D source, int size, TextureFormat fmt)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
            tex.SetPixels(source.GetPixels());
            tex.Apply();
            UnityEditor.EditorUtility.CompressTexture(tex, fmt, UnityEditor.TextureCompressionQuality.Normal);
            return tex;
        }
    }
}
