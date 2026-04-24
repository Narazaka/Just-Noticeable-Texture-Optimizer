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
            // --- Color + alpha 使用 ----
            void ColorA(string prop, string evidence) => Table.Add((null, prop), new LilToonPropertyInfo(ShaderUsage.Color, RGBA, evidence));
            // _MainTex: LIL_SAMPLE_2D(_MainTex, ...) → fd.col (float4), all channels used via fd.col *= _Color
            ColorA("_MainTex",               "Includes/lil_common_frag.hlsl:349");
            // _Main2ndTex: LIL_GET_SUBTEX → lilGetSubTex returns float4, color2nd.rgba all used (rgb blend, a for alpha mode)
            ColorA("_Main2ndTex",            "Includes/lil_common_frag.hlsl:748");
            // _Main3rdTex: same pattern
            ColorA("_Main3rdTex",            "Includes/lil_common_frag.hlsl:844");
            // _ShadowColorTex: LIL_SAMPLE_2D → shadowColorTex.rgb and shadowColorTex.a both used
            ColorA("_ShadowColorTex",        "Includes/lil_common_frag.hlsl:1092");
            // _Shadow2ndColorTex: same pattern
            ColorA("_Shadow2ndColorTex",     "Includes/lil_common_frag.hlsl:1095");
            // _Shadow3rdColorTex: same pattern
            ColorA("_Shadow3rdColorTex",     "Includes/lil_common_frag.hlsl:1098");
            // _MatCapTex: LIL_SAMPLE_2D_LOD → matCapColor *= tex (float4 mul), matCapColor.a used for transparency
            ColorA("_MatCapTex",             "Includes/lil_common_frag.hlsl:1542");
            // _MatCap2ndTex: same pattern
            ColorA("_MatCap2ndTex",          "Includes/lil_common_frag.hlsl:1610");
            // _RimColorTex: LIL_SAMPLE_2D_ST → rimColor *= tex (float4 mul), .rgba all used
            ColorA("_RimColorTex",           "Includes/lil_common_frag.hlsl:1652");
            // _ReflectionColorTex: LIL_SAMPLE_2D_ST → reflectionColor *= tex (float4 mul), .a used for transparency
            ColorA("_ReflectionColorTex",    "Includes/lil_common_frag.hlsl:1450");
            // _EmissionMap: LIL_SAMPLE_2D_ST → emissionColor *= tex (float4 mul), emissionColor.a used for blend
            ColorA("_EmissionMap",           "Includes/lil_common_frag.hlsl:1833");
            // _EmissionGradTex: LIL_SAMPLE_1D_LOD → emissionColor *= tex (float4 mul), .rgba all consumed
            ColorA("_EmissionGradTex",       "Includes/lil_common_frag.hlsl:1850");
            // _Emission2ndMap: same as EmissionMap
            ColorA("_Emission2ndMap",        "Includes/lil_common_frag.hlsl:1917");
            // _Emission2ndGradTex: same as EmissionGradTex
            ColorA("_Emission2ndGradTex",    "Includes/lil_common_frag.hlsl:1934");
            // _BacklightColorTex: LIL_SAMPLE_2D_ST → backlightColor *= tex (float4 mul), .rgba all consumed
            ColorA("_BacklightColorTex",     "Includes/lil_common_frag.hlsl:1246");
            // _GlitterColorTex: LIL_SAMPLE_2D_ST → glitterColor *= tex (float4 mul), .rgba all consumed
            ColorA("_GlitterColorTex",       "Includes/lil_common_frag.hlsl:1779");
            // _AudioLinkMask: moved to Color+R|G section below (only .r and .g are referenced)
            // _EmissionBlendMask: LIL_SAMPLE_2D_ST → emissionColor *= tex (float4 mul), .rgba all consumed
            ColorA("_EmissionBlendMask",     "Includes/lil_common_frag.hlsl:1841");
            // _Emission2ndBlendMask: same as EmissionBlendMask
            ColorA("_Emission2ndBlendMask",  "Includes/lil_common_frag.hlsl:1925");

            // --- Color + alpha 不使用 ----
            void ColorRgb(string prop, string evidence) => Table.Add((null, prop), new LilToonPropertyInfo(ShaderUsage.Color, RGB, evidence));
            // _OutlineTex: LIL_SAMPLE_2D → fd.col (float4); fd.col.a *= _OutlineColor.a (lil_common_frag.hlsl:389)
            // In cutout (LIL_RENDER==1) fd.col.a drives alpha test; in transparent (LIL_RENDER==2) clip(fd.col.a-_Cutoff)
            // → texture alpha IS used in non-opaque outline variants → corrected to Color+RGBA
            ColorA("_OutlineTex",             "Includes/lil_common_frag.hlsl:364 + lil_pass_forward_normal.hlsl:232 (cutout/transparent alpha)");
            // _MatCapBlendMask: LIL_SAMPLE_2D_ST → .rgb only (matCapMask = tex.rgb)
            ColorRgb("_MatCapBlendMask",      "Includes/lil_common_frag.hlsl:1557");
            // _MatCap2ndBlendMask: same pattern
            ColorRgb("_MatCap2ndBlendMask",   "Includes/lil_common_frag.hlsl:1625");
            // _GlitterShapeTex: LIL_SAMPLE_2D_GRAD → shapeTex.rgb * shapeTex.a; alpha IS used as shape mask
            // Seed was RGB but hlsl evidence shows RGBA usage → corrected to RGBA
            Table.Add((null, "_GlitterShapeTex"), new LilToonPropertyInfo(ShaderUsage.Color, RGBA, "Includes/lil_common_functions.hlsl:1266"));
            // _MainGradationTex: lilGradationMap uses LIL_SAMPLE_1D for R, G, B channels separately; no alpha
            // Seed was RGBA but hlsl only reads RGB → corrected
            ColorRgb("_MainGradationTex",     "Includes/lil_common_functions.hlsl:369");
            // _ShadowBlurMask: LIL_SAMPLE_2D → uses .r (shadow1 blur), .g (shadow2nd blur), .b (shadow3rd blur)
            // Seed was SingleChannel+R but hlsl uses RGB → corrected to Color+RGB (prevents BC4 candidate that would lose G/B)
            ColorRgb("_ShadowBlurMask",       "Includes/lil_common_frag.hlsl:991");
            // _ShadowBorderMask: LIL_SAMPLE_2D → uses .r/.g/.b for 3 shadow layer AO shifts
            // Seed was SingleChannel+R but hlsl uses RGB → corrected to Color+RGB
            ColorRgb("_ShadowBorderMask",     "Includes/lil_common_frag.hlsl:1006");
            // _AudioLinkMask: LIL_SAMPLE_2D → audioLinkMask (float4); uses .r (delay/strength) and .g (band) only
            // Seed was Color+RGBA but hlsl only uses R|G → corrected; Color usage prevents BC4 candidate
            Table.Add((null, "_AudioLinkMask"), new LilToonPropertyInfo(ShaderUsage.Color, R | G, "Includes/lil_common_frag.hlsl:661"));

            // --- Normal + DXT5nm (.ag 参照) ----
            void NormalAG(string prop, string evidence) => Table.Add((null, prop), new LilToonPropertyInfo(ShaderUsage.Normal, A | G, evidence));
            // _BumpMap: LIL_SAMPLE_2D_ST → lilUnpackNormalScale uses .ag (DXT5nm path)
            NormalAG("_BumpMap",             "Includes/lil_common_frag.hlsl:569");
            // _Bump2ndMap: same pattern
            NormalAG("_Bump2ndMap",          "Includes/lil_common_frag.hlsl:592");
            // _MatCapBumpMap: LIL_SAMPLE_2D_ST → lilUnpackNormalScale uses .ag
            NormalAG("_MatCapBumpMap",       "Includes/lil_common_frag.hlsl:1529");
            // _MatCap2ndBumpMap: same pattern
            NormalAG("_MatCap2ndBumpMap",    "Includes/lil_common_frag.hlsl:1597");

            // _OutlineBumpMap: searched Packages/jp.lilxyzw.liltoon entire package → 0 hits → does NOT exist in this version
            // Seed entry was a legacy artifact; removed (Task 13 audit).
            // _OutlineVectorTex: lilGetOutlineVector (lil_common_functions.hlsl:293) → lilUnpackNormalScale
            //   lilUnpackNormalScale uses .ag (DXT5nm path, line 176) same as _BumpMap → NormalAG
            NormalAG("_OutlineVectorTex",   "Includes/lil_common_functions.hlsl:293 (lilUnpackNormalScale .ag same as _BumpMap)");
            // _AnisotropyTangentMap: LIL_SAMPLE_2D_ST → anisoTangentMap (float4),
            //   then lilUnpackNormalScale(anisoTangentMap, 1.0) → uses .ag (DXT5nm path, same as _BumpMap)
            //   (lil_common_frag.hlsl:606/622)
            NormalAG("_AnisotropyTangentMap", "Includes/lil_common_frag.hlsl:606 → lilUnpackNormalScale .ag (DXT5nm, same as _BumpMap)");

            // _ShadowStrengthMask: SDF face shadow mode (_ShadowMaskType == 2) reads all RGBA
            //   Includes/lil_common_frag.hlsl:957 → shadowStrengthMask.g / .r (LdotR sign branch)
            //   Includes/lil_common_frag.hlsl:967 → shadowStrengthMask.b (lerp weight)
            //   Includes/lil_common_frag.hlsl:970 → shadowStrengthMask.a (assigned to .r)
            //   Non-SDF mode reads .r only (at line 1049+)
            //   Runtime mode is not known at audit time → conservative union = RGBA
            //   Seed was SingleChannel+R but BC4 would destroy G/B/A needed for SDF → corrected to Color+RGBA
            Table.Add((null, "_ShadowStrengthMask"), new LilToonPropertyInfo(ShaderUsage.Color, RGBA, "Includes/lil_common_frag.hlsl:938 (SDF face shadow reads .rgba at 957/967/970)"));

            // --- SingleChannel (R のみ) ----
            void SingleR(string prop, string evidence) => Table.Add((null, prop), new LilToonPropertyInfo(ShaderUsage.SingleChannel, R, evidence));
            // _ShadowBorderMask: moved to ColorRgb above (hlsl reads .r/.g/.b)
            // _ShadowBlurMask: moved to ColorRgb above (hlsl reads .r/.g/.b)
            // _OutlineWidthMask: LIL_SAMPLE_2D_LOD → .r only
            SingleR("_OutlineWidthMask",          "Includes/lil_common_functions.hlsl:277");
            // _MainColorAdjustMask: LIL_SAMPLE_2D → .r only (colorAdjustMask)
            SingleR("_MainColorAdjustMask",       "Includes/lil_common_frag.hlsl:331");
            // _SmoothnessTex: LIL_SAMPLE_2D_ST → .r only (fd.smoothness *)
            SingleR("_SmoothnessTex",             "Includes/lil_common_frag.hlsl:1434");
            // _MetallicGlossMap: LIL_SAMPLE_2D_ST → .r only (metallic *)
            SingleR("_MetallicGlossMap",          "Includes/lil_common_frag.hlsl:1443");
            // _AlphaMask: LIL_SAMPLE_2D_ST → .r only (alphaMask)
            SingleR("_AlphaMask",                 "Includes/lil_common_frag.hlsl:460");
            // _Bump2ndScaleMask: LIL_SAMPLE_2D_ST → .r only (bump2ndScale *)
            SingleR("_Bump2ndScaleMask",          "Includes/lil_common_frag.hlsl:579");
            // _ParallaxMap: LIL_SAMPLE_2D_LOD → .r only (height)
            SingleR("_ParallaxMap",               "Includes/lil_common_functions.hlsl:578");
            // _DissolveMask: LIL_SAMPLE_2D → .r only (dissolveMaskVal)
            SingleR("_DissolveMask",              "Includes/lil_common_functions.hlsl:645");
            // _DissolveNoiseMask: LIL_SAMPLE_2D → .r only (dissolveNoise)
            SingleR("_DissolveNoiseMask",         "Includes/lil_common_functions.hlsl:694");
            // _Main2ndBlendMask: LIL_SAMPLE_2D → .r only (color2nd.a *=)
            // Seed was Color+RGBA but hlsl only reads .r → corrected to SingleChannel+R
            SingleR("_Main2ndBlendMask",          "Includes/lil_common_frag.hlsl:751");
            // _Main2ndDissolveMask: passed to lilCalcDissolve → .r only
            SingleR("_Main2ndDissolveMask",       "Includes/lil_common_frag.hlsl:769 ; Includes/lil_common_functions.hlsl:645");
            // _Main2ndDissolveNoiseMask: passed to lilCalcDissolveWithNoise → .r only
            SingleR("_Main2ndDissolveNoiseMask",  "Includes/lil_common_frag.hlsl:772 ; Includes/lil_common_functions.hlsl:694");
            // _Main3rdBlendMask: LIL_SAMPLE_2D → .r only (color3rd.a *=)
            // Seed was Color+RGBA but hlsl only reads .r → corrected to SingleChannel+R
            SingleR("_Main3rdBlendMask",          "Includes/lil_common_frag.hlsl:847");
            // _Main3rdDissolveMask: passed to lilCalcDissolve → .r only
            SingleR("_Main3rdDissolveMask",       "Includes/lil_common_frag.hlsl:865 ; Includes/lil_common_functions.hlsl:645");
            // _Main3rdDissolveNoiseMask: passed to lilCalcDissolveWithNoise → .r only
            SingleR("_Main3rdDissolveNoiseMask",  "Includes/lil_common_frag.hlsl:868 ; Includes/lil_common_functions.hlsl:694");
            // _AudioLinkLocalMap: LIL_SAMPLE_2D → .r only (fd.audioLinkValue)
            SingleR("_AudioLinkLocalMap",         "Includes/lil_common_frag.hlsl:683");
            // _RimShadeMask: LIL_SAMPLE_2D → .r only (rim *=)
            SingleR("_RimShadeMask",              "Includes/lil_common_frag.hlsl:1213");
            // _AnisotropyScaleMask: LIL_SAMPLE_2D_ST → .r only (fd.anisotropy *=)
            SingleR("_AnisotropyScaleMask",       "Includes/lil_common_frag.hlsl:612");
            // _AnisotropyShiftNoiseMask: LIL_SAMPLE_2D_ST → .r only (anisotropyShiftNoise = .r - 0.5)
            SingleR("_AnisotropyShiftNoiseMask",  "Includes/lil_common_frag.hlsl:1368");
            // _DitherTex: lilSamplePointRepeat(_DitherTex, ...) → .r only (* 255 + 1)
            //   Confirmed all three OVERRIDE_DITHER definitions use .r only.
            //   (lil_common_frag.hlsl:531/538/544)
            SingleR("_DitherTex",                 "Includes/lil_common_frag.hlsl:531");

            // -----------------------------------------------------------------------
            // Fur / FurOnly variant-specific props
            // These textures are only declared and sampled in fur/furonly shader variants.
            // All fur variants (lts_fur, lts_fur_cutout, lts_fur_two,
            //   lts_furonly, lts_furonly_cutout, lts_furonly_two, ltsmulti_fur)
            // share the same HLSL paths (lil_common_vert_fur.hlsl + lil_common_frag.hlsl).
            // -----------------------------------------------------------------------
            string[] furVariants = {
                "lts_fur", "lts_fur_cutout", "lts_fur_two",
                "lts_furonly", "lts_furonly_cutout", "lts_furonly_two",
                "ltsmulti_fur"
            };

            void FurEntry(string prop, LilToonPropertyInfo info)
            {
                foreach (var v in furVariants)
                    Table.Add((v, prop), info);
            }

            // _FurNoiseMask: LIL_SAMPLE_2D_ST(_FurNoiseMask, sampler_MainTex, fd.uv0).r
            //   → furNoiseMask (float scalar). Only .r channel consumed.
            //   (lil_common_frag.hlsl:432)
            FurEntry("_FurNoiseMask",
                new LilToonPropertyInfo(ShaderUsage.SingleChannel, R,
                    "Includes/lil_common_frag.hlsl:432 (_FurNoiseMask sampled .r → furNoiseMask)"));

            // _FurMask: LIL_SAMPLE_2D(_FurMask, sampler_MainTex, fd.uvMain).r
            //   → furAlpha *= tex.r. Only .r channel consumed.
            //   (lil_common_frag.hlsl:438)
            FurEntry("_FurMask",
                new LilToonPropertyInfo(ShaderUsage.SingleChannel, R,
                    "Includes/lil_common_frag.hlsl:438 (_FurMask sampled .r → furAlpha *= ...)"));

            // _FurLengthMask: LIL_SAMPLE_2D_LOD(_FurLengthMask, lil_sampler_linear_repeat, uv, 0).r
            //   → furVectors[i] *= tex.r (geometry shader). Only .r channel consumed.
            //   (lil_common_vert_fur.hlsl:472-474)
            FurEntry("_FurLengthMask",
                new LilToonPropertyInfo(ShaderUsage.SingleChannel, R,
                    "Includes/lil_common_vert_fur.hlsl:472 (_FurLengthMask sampled .r → furVectors[i] *=)"));

            // _FurVectorTex: LIL_SAMPLE_2D_LOD(_FurVectorTex, ...) → lilUnpackNormalScale(tex, _FurVectorScale)
            //   lilUnpackNormalScale: DXT5nm path → .ag used (normalTex.a *= normalTex.r → .a and .r involved;
            //     normalTex.ag * 2 - 1). Non-DXT5nm path → .rgb used.
            //   Union across both paths: A|G (same treatment as _BumpMap → NormalAG).
            //   (lil_common_vert_fur.hlsl:172 + lil_common_functions.hlsl:166-181)
            FurEntry("_FurVectorTex",
                new LilToonPropertyInfo(ShaderUsage.Normal, A | G,
                    "Includes/lil_common_vert_fur.hlsl:172 → lilUnpackNormalScale .ag (DXT5nm, same as _BumpMap); lil_common_functions.hlsl:176"));

            // -----------------------------------------------------------------------
            // Lite variant audit (Task 17)
            //
            // Target variants (all use lil_pass_forward_lite.hlsl with LIL_LITE defined):
            //   ltsl, ltsl_cutout, ltsl_cutout_o, ltsl_o,
            //   ltsl_onetrans, ltsl_onetrans_o, ltsl_overlay, ltsl_overlay_one,
            //   ltsl_trans, ltsl_trans_o, ltsl_twotrans, ltsl_twotrans_o (12 variants)
            //
            // lil_pass_forward_lite.hlsl:169 (unconditional in non-outline branch):
            //   fd.triMask = LIL_SAMPLE_2D(_TriMask, sampler_MainTex, fd.uvMain);
            //   fd.triMask.r → matcap blend weight  (lil_common_frag.hlsl:1572)
            //   fd.triMask.g → rim blend weight      (lil_common_frag.hlsl:1740)
            //   fd.triMask.b → emission blend weight (lil_common_frag.hlsl:1881)
            //   fd.triMask.a → never referenced → alpha NOT used
            //   → Color + RGB (no alpha)
            //
            // All other 2D textures sampled in lite paths:
            //   _MainTex     : OVERRIDE_MAIN → LIL_GET_MAIN_TEX → same RGBA as common entry
            //   _ShadowColorTex / _Shadow2ndColorTex : lit path uses LIL_SAMPLE_2D (no _ST),
            //     but channel usage is identical (.rgb color + .a blend weight) → common RGBA entry covers
            //   _MatCapTex   : lite uses .rgb only (lil_common_frag.hlsl:1571), common entry is RGBA;
            //     conservative superset is safe — no per-variant override needed
            //   _EmissionMap : lite uses LIL_GET_EMITEX → float4 then only .rgb used; common RGBA is conservative
            //   _OutlineTex / _OutlineWidthMask : outline pass uses same paths as normal variants → covered by common
            //
            // Conclusion: only _TriMask requires a new lite-specific entry.
            //   ltspass_lite_* hidden pass shaders are rendering infrastructure (UsePass targets),
            //   not user-facing variant IDs → excluded from catalog.
            // -----------------------------------------------------------------------
            string[] liteVariants = {
                "ltsl", "ltsl_cutout", "ltsl_cutout_o", "ltsl_o",
                "ltsl_onetrans", "ltsl_onetrans_o", "ltsl_overlay", "ltsl_overlay_one",
                "ltsl_trans", "ltsl_trans_o", "ltsl_twotrans", "ltsl_twotrans_o"
            };

            void LiteEntry(string prop, LilToonPropertyInfo info)
            {
                foreach (var v in liteVariants)
                    Table.Add((v, prop), info);
            }

            // _TriMask: LIL_SAMPLE_2D(_TriMask, sampler_MainTex, fd.uvMain) → fd.triMask (float4)
            //   .r used for matcap blend, .g for rim, .b for emission. .a never read.
            //   (lil_pass_forward_lite.hlsl:169 ; lil_common_frag.hlsl:1572/1740/1881)
            LiteEntry("_TriMask",
                new LilToonPropertyInfo(ShaderUsage.Color, RGB,
                    "Includes/lil_pass_forward_lite.hlsl:169 ; .r=matcap .g=rim .b=emission .a=unused"));

            // -----------------------------------------------------------------------
            // Gem / Refraction variant audit (Task 15)
            //
            // Target variants:
            //   lts_gem, ltsmulti_gem   → lil_pass_forward_gem.hlsl  (define LIL_GEM)
            //   lts_ref, ltsmulti_ref   → lil_pass_forward.hlsl      (define LIL_REFRACTION)
            //   lts_ref_blur            → lil_pass_forward_refblur.hlsl (define LIL_REFRACTION + LIL_REFRACTION_BLUR2)
            //
            // 2D texture sampling audit result:
            //
            // lil_pass_forward_gem.hlsl:229
            //   LIL_SAMPLE_2D(_SmoothnessTex, sampler_MainTex, fd.uvMain).r
            //   → already covered by common entry (null, "_SmoothnessTex") SingleChannel+R
            //
            // lil_pass_forward_refblur.hlsl:47
            //   LIL_SAMPLE_2D_ST(_SmoothnessTex, lil_sampler_linear_repeat, fd.uvMain).r
            //   → already covered by common entry (null, "_SmoothnessTex") SingleChannel+R
            //
            // lts_ref / ltsmulti_ref: use lil_pass_forward.hlsl (standard path, no extra 2D samples)
            //   All sampled textures fall through to common entries.
            //
            // Gem-specific properties that are NOT 2D textures (excluded from catalog):
            //   _GemChromaticAberration, _GemEnvContrast, _GemEnvColor, _GemParticleLoop,
            //   _GemParticleColor, _GemVRParallaxStrength → all float/Color uniforms, not textures
            //
            // Refraction-specific properties that are NOT 2D textures (excluded from catalog):
            //   _RefractionStrength, _RefractionFresnelPower → float uniforms
            //   _RefractionColor                            → Color uniform (no 2D texture)
            //   _RefractionColorFromMain                    → bool uniform
            //
            // Screen-space grab texture (_lilBackgroundTexture):
            //   Used via LIL_GET_BG_TEX() / LIL_GET_GRAB_TEX() macros — runtime screen grab,
            //   not a material 2D texture property → excluded from catalog.
            //
            // Cubemap (_ReflectionCubeTex):
            //   Cube texture sampled via LIL_SAMPLE_CUBE → excluded from 2D catalog per spec.
            //
            // Conclusion: NO variant-specific catalog entries needed for gem/refraction variants.
            //   All 2D textures sampled by these variants are covered by existing common entries.
            // -----------------------------------------------------------------------
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
