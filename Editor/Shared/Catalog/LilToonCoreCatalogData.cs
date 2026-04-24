using System.Collections.Generic;
using Narazaka.VRChat.Jnto.Editor.Phase2.Compression;
using static Narazaka.VRChat.Jnto.Editor.Shared.ChannelMask;

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

        static LilToonCoreCatalogData()
        {
            const string SEED = "(seed from prev catalog)";

            // --- Color + alpha 使用 ----
            void ColorA(string prop) => Table.Add((null, prop), new LilToonPropertyInfo(ShaderUsage.Color, RGBA, SEED));
            ColorA("_MainTex");
            ColorA("_Main2ndTex");
            ColorA("_Main3rdTex");
            ColorA("_Main2ndBlendMask");
            ColorA("_Main3rdBlendMask");
            ColorA("_ShadowColorTex");
            ColorA("_Shadow2ndColorTex");
            ColorA("_Shadow3rdColorTex");
            ColorA("_MatCapTex");
            ColorA("_MatCap2ndTex");
            ColorA("_RimColorTex");
            ColorA("_ReflectionColorTex");
            ColorA("_EmissionMap");
            ColorA("_EmissionGradTex");
            ColorA("_Emission2ndMap");
            ColorA("_Emission2ndGradTex");
            ColorA("_BacklightColorTex");
            ColorA("_GlitterColorTex");
            ColorA("_MainGradationTex");
            ColorA("_AudioLinkMask");
            ColorA("_RimShadeMask");
            ColorA("_EmissionBlendMask");
            ColorA("_Emission2ndBlendMask");

            // --- Color + alpha 不使用 ----
            void ColorRgb(string prop) => Table.Add((null, prop), new LilToonPropertyInfo(ShaderUsage.Color, RGB, SEED));
            ColorRgb("_OutlineTex");
            ColorRgb("_MatCapBlendMask");
            ColorRgb("_MatCap2ndBlendMask");
            ColorRgb("_GlitterShapeTex");

            // --- Normal + DXT5nm (.ag 参照) ----
            void NormalAG(string prop) => Table.Add((null, prop), new LilToonPropertyInfo(ShaderUsage.Normal, A | G, SEED));
            NormalAG("_BumpMap");
            NormalAG("_Bump2ndMap");
            NormalAG("_MatCapBumpMap");
            NormalAG("_MatCap2ndBumpMap");

            // --- Normal + RGB 参照 (alpha 不使用) ----
            void NormalRgb(string prop) => Table.Add((null, prop), new LilToonPropertyInfo(ShaderUsage.Normal, RGB, SEED));
            NormalRgb("_OutlineBumpMap");
            NormalRgb("_OutlineVectorTex");

            // --- SingleChannel (R のみ) ----
            void SingleR(string prop) => Table.Add((null, prop), new LilToonPropertyInfo(ShaderUsage.SingleChannel, R, SEED));
            SingleR("_ShadowStrengthMask");
            SingleR("_ShadowBorderMask");
            SingleR("_ShadowBlurMask");
            SingleR("_OutlineWidthMask");
            SingleR("_MainColorAdjustMask");
            SingleR("_SmoothnessTex");
            SingleR("_MetallicGlossMap");
            SingleR("_AlphaMask");
            SingleR("_Bump2ndScaleMask");
            SingleR("_ParallaxMap");
            SingleR("_DissolveMask");
            SingleR("_DissolveNoiseMask");
            SingleR("_Main2ndDissolveMask");
            SingleR("_Main2ndDissolveNoiseMask");
            SingleR("_Main3rdDissolveMask");
            SingleR("_Main3rdDissolveNoiseMask");
            SingleR("_AudioLinkLocalMap");
        }

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
