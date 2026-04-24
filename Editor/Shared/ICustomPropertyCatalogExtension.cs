using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Shared
{
    /// <summary>
    /// 第三者 / 同梱のカスタムシェーダー用プロパティ分類の拡張点。
    /// <see cref="LilToonPropertyCatalog.RegisterExtension"/> で登録する。
    /// </summary>
    public interface ICustomPropertyCatalogExtension
    {
        /// <summary>この extension が当該 shader の分類を管轄するか。</summary>
        bool Matches(Shader shader);

        /// <summary>
        /// 管轄下のプロパティ分類を返す。
        /// Matches=true でも当該 prop について独自分類を持たない場合は false を返して良い
        /// (core catalog へフォールバックする)。
        /// </summary>
        bool TryClassify(Shader shader, string variantId, string propName, out LilToonPropertyInfo info);
    }
}
