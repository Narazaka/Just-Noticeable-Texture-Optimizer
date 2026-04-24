using System;
using System.Collections.Generic;
using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Shared
{
    /// <summary>
    /// lilToon / lilToon 派生シェーダーのプロパティ分類を解決する公開ファサード。
    /// 解決順: registered extensions → core catalog → miss。
    /// </summary>
    public static class LilToonPropertyCatalog
    {
        static readonly List<ICustomPropertyCatalogExtension> _extensions = new List<ICustomPropertyCatalogExtension>();

        /// <summary>
        /// (shader, propName) → 分類情報。以下の順で解決する:
        /// 1. Matches=true を返した最初の extension の TryClassify 結果 (true なら即確定、false なら core へ)
        /// 2. LilToonCoreCatalogData.TryGet(variantId, propName)
        /// 3. miss → false
        /// </summary>
        public static bool TryGet(Shader shader, string propName, out LilToonPropertyInfo info)
        {
            info = default;
            var variantId = LilToonShaderIdentifier.TryGetVariantId(shader);
            if (variantId == null) return false;

            foreach (var ext in _extensions)
            {
                if (!ext.Matches(shader)) continue;
                return ext.TryClassify(shader, variantId, propName, out info)
                    || LilToonCoreCatalogData.TryGet(variantId, propName, out info);
            }

            return LilToonCoreCatalogData.TryGet(variantId, propName, out info);
        }

        public static void RegisterExtension(ICustomPropertyCatalogExtension ext)
        {
            if (ext == null) throw new ArgumentNullException(nameof(ext));
            if (!_extensions.Contains(ext)) _extensions.Add(ext);
        }

        public static bool UnregisterExtension(ICustomPropertyCatalogExtension ext)
        {
            if (ext == null) return false;
            return _extensions.Remove(ext);
        }

        /// <summary>テスト用。production code では使用禁止。</summary>
        internal static void ResetExtensionsForTests() => _extensions.Clear();
    }
}
