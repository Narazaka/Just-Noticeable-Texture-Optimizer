using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Shared
{
    /// <summary>
    /// Shader アセットの path から lilToon 系 variant ID (= ファイル名 stem) を抽出する。
    /// カスタムシェーダー (.lilcontainer ScriptedImporter) にも対応。
    /// </summary>
    public static class LilToonShaderIdentifier
    {
        /// <summary>
        /// 与えられた Shader が lilToon 系なら variantId (アセットファイル名 stem) を返す。
        /// 以下のいずれにもマッチしない場合は null:
        ///   - .lilcontainer 拡張子のアセット (カスタムシェーダー)
        ///   - Packages/jp.lilxyzw.liltoon/Shader/ 配下の .shader
        /// </summary>
        public static string TryGetVariantId(Shader shader)
        {
            if (shader == null) return null;
            var path = AssetDatabase.GetAssetPath(shader);
            return TryGetVariantIdFromPath(path);
        }

        /// <summary>
        /// Task 4 で InternalsVisibleTo 追加後に internal に戻す想定だが、Task 2 時点では public。
        /// テストが lilToon / Shader 実体を要求せず path 文字列だけで回せるようにするため。
        /// </summary>
        public static string TryGetVariantIdFromPath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return null;
            var normalized = assetPath.Replace('\\', '/');

            if (normalized.EndsWith(".lilcontainer", StringComparison.Ordinal))
                return Path.GetFileNameWithoutExtension(normalized);

            if (normalized.EndsWith(".shader", StringComparison.Ordinal)
                && normalized.Contains("/jp.lilxyzw.liltoon/Shader/"))
                return Path.GetFileNameWithoutExtension(normalized);

            return null;
        }
    }
}
