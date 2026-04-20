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
                var resized = ResolutionReducer.Resize(original, size);
                var originalUpForCompare = ResolutionReducer.Resize(original, size);
                foreach (var fmt in chain)
                {
                    var decoded = TextureEncodeDecode.EncodeAndDecode(resized, fmt);
                    if (_gate.Passes(originalUpForCompare, decoded, preset, out _))
                    {
                        var final = new Texture2D(size, size, TextureFormat.RGBA32, true);
                        final.SetPixels(resized.GetPixels());
                        final.Apply();
                        UnityEditor.EditorUtility.CompressTexture(final, fmt, UnityEditor.TextureCompressionQuality.Normal);
                        Object.DestroyImmediate(decoded);
                        Object.DestroyImmediate(originalUpForCompare);
                        Object.DestroyImmediate(resized);
                        return new Phase2Result { Final = final, Size = size, Format = fmt };
                    }
                    Object.DestroyImmediate(decoded);
                }
                Object.DestroyImmediate(originalUpForCompare);
                Object.DestroyImmediate(resized);
            }
            return new Phase2Result { Final = original, Size = origMax, Format = original.format, FallbackReason = "all-candidates-rejected" };
        }
    }
}
