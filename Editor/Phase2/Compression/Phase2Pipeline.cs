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

            for (int size = targetSize; size <= origMax; size *= 2)
            {
                // Identity shortcut: at original size, formats at or above original quality auto-pass
                bool isOrigSize = (size == origMax);

                var resized = ResolutionReducer.Resize(original, size);
                Texture2D originalForCompare = null;

                foreach (var fmt in chain)
                {
                    // If same size AND same (or higher quality) format as original → no degradation possible
                    if (isOrigSize && IsFormatSameOrBetter(fmt, original.format))
                    {
                        var final = new Texture2D(size, size, TextureFormat.RGBA32, true);
                        final.SetPixels(resized.GetPixels());
                        final.Apply();
                        UnityEditor.EditorUtility.CompressTexture(final, fmt, UnityEditor.TextureCompressionQuality.Normal);
                        Object.DestroyImmediate(resized);
                        return new Phase2Result { Final = final, Size = size, Format = fmt };
                    }

                    // Lazy-create comparison baseline
                    if (originalForCompare == null)
                        originalForCompare = ResolutionReducer.Resize(original, size);

                    var decoded = TextureEncodeDecode.EncodeAndDecode(resized, fmt);
                    bool pass = _gate.Passes(originalForCompare, decoded, preset, out _);
                    Object.DestroyImmediate(decoded);

                    if (pass)
                    {
                        var final = new Texture2D(size, size, TextureFormat.RGBA32, true);
                        final.SetPixels(resized.GetPixels());
                        final.Apply();
                        UnityEditor.EditorUtility.CompressTexture(final, fmt, UnityEditor.TextureCompressionQuality.Normal);
                        if (originalForCompare != null) Object.DestroyImmediate(originalForCompare);
                        Object.DestroyImmediate(resized);
                        return new Phase2Result { Final = final, Size = size, Format = fmt };
                    }
                }

                if (originalForCompare != null) Object.DestroyImmediate(originalForCompare);
                Object.DestroyImmediate(resized);
            }

            return new Phase2Result { Final = original, Size = origMax, Format = original.format, FallbackReason = "all-candidates-rejected" };
        }

        static bool IsFormatSameOrBetter(TextureFormat candidate, TextureFormat original)
        {
            if (candidate == original) return true;
            // BC7 is same-or-better than DXT1/DXT5/BC4/BC5
            if (candidate == TextureFormat.BC7) return true;
            // Uncompressed is always same-or-better
            if (candidate == TextureFormat.RGBA32 || candidate == TextureFormat.RGB24 || candidate == TextureFormat.R8) return true;
            return false;
        }
    }
}
