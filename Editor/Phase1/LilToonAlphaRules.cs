using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Shared;

namespace Narazaka.VRChat.Jnto.Editor.Phase1
{
    /// <summary>
    /// lilToon テクスチャプロパティのアルファ使用判定。
    /// 分類の実体は <see cref="LilToonPropertyCatalog"/>。
    /// </summary>
    public static class LilToonAlphaRules
    {
        public static bool IsAlphaUsed(Shader shader, string propertyName)
        {
            // 未知プロパティは安全側 true (strip 禁止)
            if (!LilToonPropertyCatalog.TryGet(shader, propertyName, out var info)) return true;
            return info.AlphaUsed;
        }
    }
}
