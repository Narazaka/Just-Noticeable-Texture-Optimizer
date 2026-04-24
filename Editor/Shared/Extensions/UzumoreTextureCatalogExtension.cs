using UnityEditor;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.Compression;
using static Narazaka.VRChat.Jnto.Editor.Shared.ChannelMask;

namespace Narazaka.VRChat.Jnto.Editor.Shared.Extensions
{
    /// <summary>
    /// jp.sigmal00.uzumore-shader 用 (jnto 同梱) の分類拡張。
    /// Uzumore 未インストール環境では Matches が常時 false になり noop。
    /// </summary>
    public sealed class UzumoreTextureCatalogExtension : ICustomPropertyCatalogExtension
    {
        const string UZUMORE_PATH_MARKER = "jp.sigmal00.uzumore-shader";

        [InitializeOnLoadMethod]
        static void AutoRegister() =>
            LilToonPropertyCatalog.RegisterExtension(new UzumoreTextureCatalogExtension());

        public bool Matches(Shader shader)
        {
            if (shader == null) return false;
            var path = AssetDatabase.GetAssetPath(shader);
            return MatchesPath(path);
        }

        public static bool MatchesPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            return path.Replace('\\', '/').Contains("/" + UZUMORE_PATH_MARKER + "/");
        }

        public bool TryClassify(Shader shader, string variantId, string propName, out LilToonPropertyInfo info)
        {
            switch (propName)
            {
                case "_UzumoreMask":
                    // Audit: custom_insert.hlsl:15
                    //   LIL_SAMPLE_2D_LOD(_UzumoreMask, sampler_UzumoreMask, uv, 0).r
                    // Only .r is referenced — SingleChannel + R confirmed.
                    info = new LilToonPropertyInfo(
                        ShaderUsage.SingleChannel,
                        R,
                        "uzumore/custom_insert.hlsl:15 (calcIntrudePos で _UzumoreMask.r のみ参照)"
                    );
                    return true;
                default:
                    info = default;
                    return false;
            }
        }
    }
}
