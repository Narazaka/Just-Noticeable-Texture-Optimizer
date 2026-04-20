using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Compression
{
    public enum TextureRole { ColorOpaque, ColorAlpha, NormalMap, SingleChannel, MatCapOrLut }

    public static class TextureTypeClassifier
    {
        public static TextureRole Classify(Material mat, string propName, Texture2D tex, bool alphaRequired)
        {
            if (mat != null && Phase1.LilTexAlphaUsageAnalyzer.IsLilToon(mat))
            {
                switch (propName)
                {
                    case "_BumpMap":
                    case "_Bump2ndMap":
                    case "_OutlineBumpMap": return TextureRole.NormalMap;
                    case "_MatCapTex":
                    case "_MatCap2ndTex": return TextureRole.MatCapOrLut;
                    case "_AlphaMask":
                    case "_EmissionBlendMask":
                    case "_Emission2ndBlendMask": return TextureRole.SingleChannel;
                }
            }
            var path = UnityEditor.AssetDatabase.GetAssetPath(tex);
            var imp = UnityEditor.AssetImporter.GetAtPath(path) as UnityEditor.TextureImporter;
            if (imp != null)
            {
                if (imp.textureType == UnityEditor.TextureImporterType.NormalMap) return TextureRole.NormalMap;
                if (imp.textureType == UnityEditor.TextureImporterType.SingleChannel) return TextureRole.SingleChannel;
            }
            return alphaRequired ? TextureRole.ColorAlpha : TextureRole.ColorOpaque;
        }
    }
}
