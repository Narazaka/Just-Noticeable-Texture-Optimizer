using System.Collections.Generic;

namespace Narazaka.VRChat.Jnto.Editor.Shared
{
    /// <summary>
    /// lilToon 本体 (stock) シェーダーの (variantId, propName) → 分類情報 データ。
    /// key の variantId が null のエントリは「全 variant 共通」。
    /// 解決順: (variantId, prop) hit → (null, prop) hit → miss。
    ///
    /// データ充填は spec の監査ワークフロー (Phase 3) で行う。
    /// </summary>
    internal static class LilToonCoreCatalogData
    {
        // (variantId, propName) → info。variantId=null が共通エントリ。
        static readonly Dictionary<(string, string), LilToonPropertyInfo> Table = new Dictionary<(string, string), LilToonPropertyInfo>();

        internal static bool TryGet(string variantId, string propName, out LilToonPropertyInfo info)
        {
            if (variantId != null && Table.TryGetValue((variantId, propName), out info)) return true;
            return Table.TryGetValue((null, propName), out info);
        }

        /// <summary>
        /// Phase 2 / Phase 3 で entry を追加する。同一 key 重複は例外 (catalog の静的検証)。
        /// </summary>
        internal static void Add(string variantId, string propName, LilToonPropertyInfo info)
        {
            Table.Add((variantId, propName), info);
        }

        /// <summary>全エントリ列挙 (table-driven テスト用)。</summary>
        internal static IEnumerable<KeyValuePair<(string variantId, string propName), LilToonPropertyInfo>> EnumerateAll() => Table;
    }
}
