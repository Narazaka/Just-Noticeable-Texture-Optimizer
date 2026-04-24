using UnityEditor;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Shared;

namespace Narazaka.VRChat.Jnto.Editor.Phase1
{
    public static class LilTexAlphaUsageAnalyzer
    {
        public static bool IsAlphaUsed(Material mat, string propertyName)
        {
            if (mat == null) return ConservativeNonLilFallback(mat, propertyName);
            var variantId = LilToonShaderIdentifier.TryGetVariantId(mat.shader);
            if (variantId != null) return LilToonAlphaRules.IsAlphaUsed(mat.shader, propertyName);
            return ConservativeNonLilFallback(mat, propertyName);
        }

        /// <summary>
        /// 旧 API 互換: 既存の lilToon 判定呼び出し向け。<see cref="LilToonShaderIdentifier.TryGetVariantId"/> に置換された。
        /// </summary>
        public static bool IsLilToon(Material mat)
            => mat != null && LilToonShaderIdentifier.TryGetVariantId(mat.shader) != null;

        static bool ConservativeNonLilFallback(Material mat, string propertyName)
        {
            if (mat == null) return true;   // null material は安全側
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
