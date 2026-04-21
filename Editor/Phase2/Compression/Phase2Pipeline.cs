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

            for (int size = targetSize; size <= origMax; size *= 2)
            {
                if (size == origMax)
                {
                    // 元サイズでは元形式をそのまま採用 (再エンコード不要)
                    return new Phase2Result { Final = original, Size = origMax, Format = origFmt };
                }

                var resized = ResolutionReducer.Resize(original, size);
                var originalForCompare = ResolutionReducer.Resize(original, size);

                // DXT5 で検証して通れば DXT1 にも適用可 (圧縮傾向が同じ)
                // → チェイン内の DXT 系は DXT5 で代表検証
                bool dxtPassed = false;
                foreach (var fmt in chain)
                {
                    var testFmt = fmt;
                    if (fmt == TextureFormat.DXT1 && dxtPassed)
                    {
                        // DXT5 で通過済みなら DXT1 も通過とみなす
                        var final = CreateCompressed(resized, size, fmt);
                        Object.DestroyImmediate(originalForCompare);
                        Object.DestroyImmediate(resized);
                        return new Phase2Result { Final = final, Size = size, Format = fmt };
                    }

                    var decoded = TextureEncodeDecode.EncodeAndDecode(resized, testFmt);
                    bool pass = _gate.Passes(originalForCompare, decoded, preset, out _);
                    Object.DestroyImmediate(decoded);

                    if (pass)
                    {
                        // DXT5 通過を記録 (DXT1 チェインが後にある場合に活用)
                        if (fmt == TextureFormat.DXT5) dxtPassed = true;

                        var final = CreateCompressed(resized, size, fmt);
                        Object.DestroyImmediate(originalForCompare);
                        Object.DestroyImmediate(resized);
                        return new Phase2Result { Final = final, Size = size, Format = fmt };
                    }
                }

                Object.DestroyImmediate(originalForCompare);
                Object.DestroyImmediate(resized);
            }

            return new Phase2Result { Final = original, Size = origMax, Format = origFmt, FallbackReason = "all-candidates-rejected" };
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
