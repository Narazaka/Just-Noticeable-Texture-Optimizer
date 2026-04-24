using UnityEditor;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Shared;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Compression
{
    /// <summary>
    /// material/shader prop name または TextureImporter から ShaderUsage を推定する。
    /// lilToon プロパティの分類は <see cref="LilToonPropertyCatalog"/> を参照。
    /// </summary>
    public static class ShaderUsageInferrer
    {
        public static ShaderUsage Infer(Material material, string propName, Texture2D tex)
        {
            // 1. lilToon prop semantics
            if (material != null
                && LilToonPropertyCatalog.TryGet(material.shader, propName, out var info))
            {
                return info.Usage;
            }

            // 2. TextureImporter.textureType
            if (tex != null)
            {
                var path = AssetDatabase.GetAssetPath(tex);
                if (!string.IsNullOrEmpty(path))
                {
                    var imp = AssetImporter.GetAtPath(path) as TextureImporter;
                    if (imp != null)
                    {
                        if (imp.textureType == TextureImporterType.NormalMap) return ShaderUsage.Normal;
                        if (imp.textureType == TextureImporterType.SingleChannel) return ShaderUsage.SingleChannel;
                    }
                }
            }

            // 3. デフォルト
            return ShaderUsage.Color;
        }
    }
}
