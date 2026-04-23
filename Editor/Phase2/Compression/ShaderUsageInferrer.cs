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
                    case "_MatCapBumpMap":
                    case "_MatCap2ndBumpMap":
                    case "_OutlineVectorTex":
                        return ShaderUsage.Normal;
                    case "_AlphaMask":
                    case "_EmissionBlendMask":
                    case "_Emission2ndBlendMask":
                    case "_ShadowStrengthMask":
                    case "_ShadowBorderMask":
                    case "_ShadowBlurMask":
                    case "_OutlineWidthMask":
                    case "_MainColorAdjustMask":
                    case "_SmoothnessTex":
                    case "_MetallicGlossMap":
                    case "_Bump2ndScaleMask":
                    case "_ParallaxMap":
                    case "_DissolveMask":
                    case "_DissolveNoiseMask":
                    case "_Main2ndDissolveMask":
                    case "_Main2ndDissolveNoiseMask":
                    case "_Main3rdDissolveMask":
                    case "_Main3rdDissolveNoiseMask":
                    case "_AudioLinkLocalMap":
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
