using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Compression
{
    public static class CompressionChain
    {
        public static TextureFormat[] For(TextureRole role)
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
    }
}
