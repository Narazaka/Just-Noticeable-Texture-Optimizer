using System.Collections.Generic;
using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Compression
{
    public static class CompressionChain
    {
        public static TextureFormat[] For(TextureRole role, TextureFormat originalFormat = TextureFormat.RGBA32)
        {
            var chain = DefaultChain(role);
            var result = new List<TextureFormat>();
            if (IsBCFormat(originalFormat) && System.Array.IndexOf(chain, originalFormat) < 0)
                result.Add(originalFormat);
            result.AddRange(chain);
            return result.ToArray();
        }

        static TextureFormat[] DefaultChain(TextureRole role)
        {
            switch (role)
            {
                case TextureRole.NormalMap:
                    return new[] { TextureFormat.BC5, TextureFormat.BC7 };
                case TextureRole.ColorOpaque:
                    return new[] { TextureFormat.DXT1, TextureFormat.BC7 };
                case TextureRole.ColorAlpha:
                    return new[] { TextureFormat.DXT5, TextureFormat.BC7 };
                case TextureRole.SingleChannel:
                    return new[] { TextureFormat.BC4, TextureFormat.R8 };
                case TextureRole.MatCapOrLut:
                    return new[] { TextureFormat.BC7 };
                default:
                    return new[] { TextureFormat.BC7 };
            }
        }

        static bool IsBCFormat(TextureFormat fmt)
        {
            switch (fmt)
            {
                case TextureFormat.DXT1:
                case TextureFormat.DXT1Crunched:
                case TextureFormat.DXT5:
                case TextureFormat.DXT5Crunched:
                case TextureFormat.BC4:
                case TextureFormat.BC5:
                case TextureFormat.BC6H:
                case TextureFormat.BC7:
                    return true;
                default:
                    return false;
            }
        }
    }
}
