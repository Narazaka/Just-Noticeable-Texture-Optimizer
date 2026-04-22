using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Compression
{
    public enum TextureRole { ColorOpaque, ColorAlpha, NormalMap, SingleChannel, MatCapOrLut }

    public static class TextureTypeClassifier
    {
        public static TextureRole Classify(Material mat, string propName, Texture2D tex, bool alphaRequired)
        {
            // 1. lilToon の prop name による NormalMap/SingleChannel/MatCap 判定 (最優先: 意図が明確)
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

            if (tex != null)
            {
                // 2. TextureImporter による NormalMap/SingleChannel 判定
                var path = UnityEditor.AssetDatabase.GetAssetPath(tex);
                var imp = UnityEditor.AssetImporter.GetAtPath(path) as UnityEditor.TextureImporter;
                if (imp != null)
                {
                    if (imp.textureType == UnityEditor.TextureImporterType.NormalMap) return TextureRole.NormalMap;
                    if (imp.textureType == UnityEditor.TextureImporterType.SingleChannel) return TextureRole.SingleChannel;
                }

                // 3. 元 texture format による強制判定 (保守的ガード)
                //    元 fmt の本質的特性を保持する: α 無し fmt → α 無し role、normal → NormalMap、single → SingleChannel
                switch (tex.format)
                {
                    case TextureFormat.BC5:
                        return TextureRole.NormalMap;
                    case TextureFormat.BC4:
                    case TextureFormat.R8:
                    case TextureFormat.Alpha8:
                        return TextureRole.SingleChannel;
                    case TextureFormat.DXT1:
                    case TextureFormat.DXT1Crunched:
                    case TextureFormat.BC6H:
                    case TextureFormat.RGB24:
                        // α 持たない fmt → alphaRequired を無視して ColorOpaque
                        return TextureRole.ColorOpaque;
                    case TextureFormat.DXT5:
                    case TextureFormat.DXT5Crunched:
                        // α 持つ fmt → 強制 ColorAlpha
                        return TextureRole.ColorAlpha;
                }
            }

            // 4. material から alphaRequired を見てフォールバック
            return alphaRequired ? TextureRole.ColorAlpha : TextureRole.ColorOpaque;
        }
    }
}
