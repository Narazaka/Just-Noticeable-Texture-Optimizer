using Narazaka.VRChat.Jnto.Editor.Shared;

namespace Narazaka.VRChat.Jnto.Editor.Phase1
{
    /// <summary>
    /// lilToon テクスチャプロパティごとに、アルファチャネルがシェーダーで意味を持つかを判定する。
    /// true = アルファに意味あり (strip 禁止)、false = アルファ不使用 (strip 可)。
    /// 分類本体は <see cref="LilToonTextureCatalog"/> に集約されている。
    /// </summary>
    public static class LilToonAlphaRules
    {
        public static bool IsAlphaUsed(string propertyName)
        {
            // 未知プロパティは安全側で alpha 使用扱い (strip 禁止) とする。
            if (!LilToonTextureCatalog.TryGet(propertyName, out var info)) return true;
            return info.AlphaUsed;
        }
    }
}
