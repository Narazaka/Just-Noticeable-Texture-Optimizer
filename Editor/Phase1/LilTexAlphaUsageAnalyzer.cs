using UnityEditor;
using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Phase1
{
    public static class LilTexAlphaUsageAnalyzer
    {
        public static bool IsAlphaUsed(Material mat, string propertyName)
        {
            if (IsLilToon(mat)) return LilToonAlphaRules.IsAlphaUsed(mat.shader, propertyName);
            return ConservativeNonLilFallback(mat, propertyName);
        }

        public static bool IsLilToon(Material mat)
        {
            if (mat == null || mat.shader == null) return false;
            var n = mat.shader.name ?? "";
            return n.Contains("lilToon") || n.Contains("_lil/");
        }

        static bool ConservativeNonLilFallback(Material mat, string propertyName)
        {
            var tex = mat.GetTexture(propertyName);
            if (tex == null) return false;
            var path = AssetDatabase.GetAssetPath(tex);
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) return true;
            switch (imp.textureType)
            {
                case TextureImporterType.NormalMap:
                case TextureImporterType.SingleChannel:
                    return false;
                default:
                    return true;
            }
        }
    }
}
