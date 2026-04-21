using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Phase1
{
    /// <summary>
    /// lilToon テクスチャプロパティごとに、アルファチャネルがシェーダーで意味を持つかを判定する。
    /// true = アルファに意味あり (strip禁止)
    /// false = アルファ不使用 (strip可)
    ///
    /// 判定基準: lilToon シェーダーソース (lil_common_frag.hlsl 等) で .a が参照されるか。
    /// ノーマルマップは DXT5nm 形式でアルファに法線X成分を格納するため、常に true。
    /// R チャネルのみ使用するマスクは false (アルファ無関係)。
    /// </summary>
    public static class LilToonAlphaRules
    {
        public static bool IsAlphaUsed(string propertyName)
        {
            switch (propertyName)
            {
                // --- アルファ不使用 (strip可) ---

                // ノーマルマップ: DXT5nm ではアルファに X 成分格納 → strip 禁止に見えるが、
                // AlphaStripper は RGB24 に変換するため DXT5nm の場合は対象外
                // (DXT5nm は RGB24/RGB565 ではないため format チェックで弾かれない)
                // → 安全側で true (strip禁止) にする
                // ※ ノーマルマップは下記で true 返却

                // R チャネルのみ使用するマスクテクスチャ
                case "_ShadowStrengthMask":
                case "_ShadowBorderMask":
                case "_ShadowBlurMask":
                case "_OutlineWidthMask":
                case "_MainColorAdjustMask":
                case "_SmoothnessTex":
                case "_MetallicGlossMap":
                case "_AlphaMask": // R チャネルのみ使用
                case "_Bump2ndScaleMask":
                case "_ParallaxMap": // R チャネル = 高さマップ
                case "_DissolveMask":
                case "_DissolveNoiseMask":
                case "_Main2ndDissolveMask":
                case "_Main2ndDissolveNoiseMask":
                case "_Main3rdDissolveMask":
                case "_Main3rdDissolveNoiseMask":
                case "_AudioLinkLocalMap": // R チャネルのみ
                    return false;

                // RGB のみ使用 (アルファ無視)
                case "_MatCapBlendMask":
                case "_MatCap2ndBlendMask":
                case "_OutlineTex": // RGB のみ、alpha は _OutlineColor.a で上書き
                case "_OutlineVectorTex": // 法線マップ形式、RGB のみ
                case "_OutlineBumpMap": // 法線マップ形式
                case "_GlitterShapeTex": // RGB のみ
                    return false;

                // --- アルファ使用 (strip禁止) ---

                // ノーマルマップ: DXT5nm でアルファに法線 X 成分格納
                case "_BumpMap":
                case "_Bump2ndMap":
                case "_MatCapBumpMap":
                case "_MatCap2ndBumpMap":
                    return true;

                // _MainTex: 常にアルファ使用 (fd.col.a として全体のアルファに影響)
                case "_MainTex":
                    return true;

                // RGBA 全体使用 (アルファがブレンド係数等)
                case "_Main2ndTex": // AlphaMode でアルファ合成
                case "_Main3rdTex":
                case "_Main2ndBlendMask": // R→alpha乗算
                case "_Main3rdBlendMask":
                case "_ShadowColorTex": // .a がブレンド係数
                case "_Shadow2ndColorTex":
                case "_Shadow3rdColorTex":
                case "_MatCapTex": // .a が MatCap ブレンド強度
                case "_MatCap2ndTex":
                case "_RimColorTex": // .a が Rim ブレンド強度
                case "_ReflectionColorTex": // .a が反射ブレンド
                case "_EmissionMap": // .a がエミッション強度
                case "_EmissionBlendMask":
                case "_EmissionGradTex":
                case "_Emission2ndMap":
                case "_Emission2ndBlendMask":
                case "_Emission2ndGradTex":
                case "_BacklightColorTex": // .a がバックライト強度
                case "_GlitterColorTex": // .a がグリッター強度
                case "_MainGradationTex": // RGBA グラデーション
                case "_AudioLinkMask": // R/B チャネル使用
                case "_RimShadeMask": // RGB 使用
                    return true;

                // 未知プロパティ: 安全側で alpha 使用扱い
                default:
                    return true;
            }
        }
    }
}
