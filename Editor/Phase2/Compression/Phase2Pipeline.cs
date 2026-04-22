using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2;
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

        public Phase2Result Find(Texture2D original, int targetSize, TextureRole role,
            QualityPreset preset, TexelDensityMap densityMap)
        {
            var chain = CompressionChain.For(role, original.format);
            int origMax = Mathf.Max(original.width, original.height);
            var origFmt = original.format;

            var primaryFmts = System.Array.FindAll(chain, f => f != TextureFormat.BC7);
            var fallbackFmts = System.Array.FindAll(chain, f => f == TextureFormat.BC7);

            var result = TrySizes(original, targetSize, origMax, primaryFmts, role, preset, densityMap);
            if (result != null) return result;

            if (fallbackFmts.Length > 0)
            {
                result = TrySizes(original, targetSize, origMax, fallbackFmts, role, preset, densityMap);
                if (result != null) return result;
            }

            _gate.Cleanup();
            return new Phase2Result { Final = original, Size = origMax, Format = origFmt };
        }

        Phase2Result TrySizes(Texture2D original, int targetSize, int origMax,
            TextureFormat[] fmts, TextureRole role, QualityPreset preset, TexelDensityMap densityMap)
        {
            for (int size = targetSize; size <= origMax; size *= 2)
            {
                var reference = ResolutionReducer.Resize(original, size);

                if (!_gate.PassesDownscale(original, reference, role, preset, densityMap, out _))
                {
                    Object.DestroyImmediate(reference);
                    continue;
                }

                foreach (var fmt in fmts)
                {
                    var candidate = TextureEncodeDecode.EncodeAndDecode(reference, fmt);
                    bool pass = _gate.PassesCompression(reference, candidate, role, preset, densityMap, out _);
                    Object.DestroyImmediate(candidate);

                    if (pass)
                    {
                        var final = CreateCompressed(reference, fmt, original.name);
                        Object.DestroyImmediate(reference);
                        _gate.Cleanup();
                        return new Phase2Result { Final = final, Size = size, Format = fmt };
                    }
                }

                Object.DestroyImmediate(reference);
            }
            return null;
        }

        static Texture2D CreateCompressed(Texture2D source, TextureFormat fmt, string originalName)
        {
            var tex = new Texture2D(source.width, source.height, TextureFormat.RGBA32, true);
            tex.name = $"{originalName}_{source.width}x{source.height}_{fmt}";
            tex.SetPixels(source.GetPixels());
            tex.Apply();
            UnityEditor.EditorUtility.CompressTexture(tex, fmt, UnityEditor.TextureCompressionQuality.Normal);
            SetStreamingMipmaps(tex, true);
            return tex;
        }

        static void SetStreamingMipmaps(Texture2D tex, bool enabled)
        {
            var so = new UnityEditor.SerializedObject(tex);
            var prop = so.FindProperty("m_StreamingMipmaps");
            if (prop != null)
            {
                prop.boolValue = enabled;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }
    }
}
