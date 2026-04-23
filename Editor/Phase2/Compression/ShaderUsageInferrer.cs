using UnityEditor;
using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Compression
{
    /// <summary>
    /// material/shader prop name または TextureImporter から ShaderUsage を推定する。
    /// </summary>
    public static class ShaderUsageInferrer
    {
        public static ShaderUsage Infer(Material material, string propName, Texture2D tex)
        {
            // 1. lilToon prop semantics
            if (material != null && Phase1.LilTexAlphaUsageAnalyzer.IsLilToon(material))
            {
                switch (propName)
                {
                    case "_BumpMap":
                    case "_Bump2ndMap":
                    case "_OutlineBumpMap":
                        return ShaderUsage.Normal;
                    case "_AlphaMask":
                    case "_EmissionBlendMask":
                    case "_Emission2ndBlendMask":
                        return ShaderUsage.SingleChannel;
                }
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
