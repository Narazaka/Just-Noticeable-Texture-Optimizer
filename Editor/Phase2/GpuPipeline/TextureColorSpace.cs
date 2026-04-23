using UnityEditor;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.Compression;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.GpuPipeline
{
    /// <summary>
    /// テクスチャを内部 RT 上で linear 値として扱うべきかを判定する単一ヘルパー。
    ///
    /// 優先順:
    ///   1. TextureImporter.sRGBTexture (importer が取得できる場合)
    ///   2. format が物理的に非 sRGB (RGBAHalf/RGBAFloat/R16/BC6H) なら linear
    ///      (BC4/BC5/BC7/DXT* は sRGB-flag が importer 設定次第なのでここでは判定しない)
    ///   3. usageFallback: ShaderUsage.Normal / SingleChannel なら linear
    /// </summary>
    public static class TextureColorSpace
    {
        public static bool IsLinear(Texture2D tex, ShaderUsage usageFallback)
        {
            if (tex != null)
            {
                var path = AssetDatabase.GetAssetPath(tex);
                if (!string.IsNullOrEmpty(path))
                {
                    var imp = AssetImporter.GetAtPath(path) as TextureImporter;
                    if (imp != null) return !imp.sRGBTexture;
                }

                switch (tex.format)
                {
                    case TextureFormat.RGBAHalf:
                    case TextureFormat.RGBAFloat:
                    case TextureFormat.RHalf:
                    case TextureFormat.RFloat:
                    case TextureFormat.RGHalf:
                    case TextureFormat.RGFloat:
                    case TextureFormat.R16:
                    case TextureFormat.BC6H:
                        return true;
                }
            }

            return usageFallback == ShaderUsage.Normal
                || usageFallback == ShaderUsage.SingleChannel;
        }
    }
}
