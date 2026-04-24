using System.Collections.Generic;
using Narazaka.VRChat.Jnto.Editor.Phase2.Compression;

namespace Narazaka.VRChat.Jnto.Editor.Shared
{
    public readonly struct LilToonPropertyInfo
    {
        public readonly ShaderUsage Usage;
        public readonly bool AlphaUsed;
        public LilToonPropertyInfo(ShaderUsage usage, bool alphaUsed)
        {
            Usage = usage;
            AlphaUsed = alphaUsed;
        }
    }

    /// <summary>
    /// lilToon シェーダーのテクスチャプロパティ分類辞書。
    /// Phase1 (alpha strip) と Phase2 (フォーマット選択) で重複していた property table を一元化する。
    ///
    /// Usage は <see cref="ShaderUsage"/> と一致 (Color / Normal / SingleChannel)。
    /// AlphaUsed は alpha チャネルが意味的に参照されるか (DXT5nm の X 成分含む)。
    /// </summary>
    public static class LilToonTextureCatalog
    {
        static readonly Dictionary<string, LilToonPropertyInfo> Table = new Dictionary<string, LilToonPropertyInfo>
        {
            // --- Color, alpha 使用 (RGBA / .a がブレンド係数等) ---
            { "_MainTex",              new LilToonPropertyInfo(ShaderUsage.Color, true) },
            { "_Main2ndTex",           new LilToonPropertyInfo(ShaderUsage.Color, true) },
            { "_Main3rdTex",           new LilToonPropertyInfo(ShaderUsage.Color, true) },
            { "_Main2ndBlendMask",     new LilToonPropertyInfo(ShaderUsage.Color, true) },
            { "_Main3rdBlendMask",     new LilToonPropertyInfo(ShaderUsage.Color, true) },
            { "_ShadowColorTex",       new LilToonPropertyInfo(ShaderUsage.Color, true) },
            { "_Shadow2ndColorTex",    new LilToonPropertyInfo(ShaderUsage.Color, true) },
            { "_Shadow3rdColorTex",    new LilToonPropertyInfo(ShaderUsage.Color, true) },
            { "_MatCapTex",            new LilToonPropertyInfo(ShaderUsage.Color, true) },
            { "_MatCap2ndTex",         new LilToonPropertyInfo(ShaderUsage.Color, true) },
            { "_RimColorTex",          new LilToonPropertyInfo(ShaderUsage.Color, true) },
            { "_ReflectionColorTex",   new LilToonPropertyInfo(ShaderUsage.Color, true) },
            { "_EmissionMap",          new LilToonPropertyInfo(ShaderUsage.Color, true) },
            { "_EmissionGradTex",      new LilToonPropertyInfo(ShaderUsage.Color, true) },
            { "_Emission2ndMap",       new LilToonPropertyInfo(ShaderUsage.Color, true) },
            { "_Emission2ndGradTex",   new LilToonPropertyInfo(ShaderUsage.Color, true) },
            { "_BacklightColorTex",    new LilToonPropertyInfo(ShaderUsage.Color, true) },
            { "_GlitterColorTex",      new LilToonPropertyInfo(ShaderUsage.Color, true) },
            { "_MainGradationTex",     new LilToonPropertyInfo(ShaderUsage.Color, true) },
            { "_AudioLinkMask",        new LilToonPropertyInfo(ShaderUsage.Color, true) },
            { "_RimShadeMask",         new LilToonPropertyInfo(ShaderUsage.Color, true) },

            // --- Color, alpha 不使用 (RGB のみ) ---
            { "_OutlineTex",           new LilToonPropertyInfo(ShaderUsage.Color, false) },
            { "_MatCapBlendMask",      new LilToonPropertyInfo(ShaderUsage.Color, false) },
            { "_MatCap2ndBlendMask",   new LilToonPropertyInfo(ShaderUsage.Color, false) },
            { "_GlitterShapeTex",      new LilToonPropertyInfo(ShaderUsage.Color, false) },

            // --- Normal map ---
            // DXT5nm 系: alpha に法線 X 成分 → strip 禁止
            { "_BumpMap",              new LilToonPropertyInfo(ShaderUsage.Normal, true) },
            { "_Bump2ndMap",           new LilToonPropertyInfo(ShaderUsage.Normal, true) },
            { "_MatCapBumpMap",        new LilToonPropertyInfo(ShaderUsage.Normal, true) },
            { "_MatCap2ndBumpMap",     new LilToonPropertyInfo(ShaderUsage.Normal, true) },
            // RGB のみ使用する Normal (アウトライン用ベクトル)
            { "_OutlineBumpMap",       new LilToonPropertyInfo(ShaderUsage.Normal, false) },
            { "_OutlineVectorTex",     new LilToonPropertyInfo(ShaderUsage.Normal, false) },

            // --- SingleChannel mask (R チャネルのみ参照) ---
            { "_ShadowStrengthMask",   new LilToonPropertyInfo(ShaderUsage.SingleChannel, false) },
            { "_ShadowBorderMask",     new LilToonPropertyInfo(ShaderUsage.SingleChannel, false) },
            { "_ShadowBlurMask",       new LilToonPropertyInfo(ShaderUsage.SingleChannel, false) },
            { "_OutlineWidthMask",     new LilToonPropertyInfo(ShaderUsage.SingleChannel, false) },
            { "_MainColorAdjustMask",  new LilToonPropertyInfo(ShaderUsage.SingleChannel, false) },
            { "_SmoothnessTex",        new LilToonPropertyInfo(ShaderUsage.SingleChannel, false) },
            { "_MetallicGlossMap",     new LilToonPropertyInfo(ShaderUsage.SingleChannel, false) },
            { "_AlphaMask",            new LilToonPropertyInfo(ShaderUsage.SingleChannel, false) },
            { "_Bump2ndScaleMask",     new LilToonPropertyInfo(ShaderUsage.SingleChannel, false) },
            { "_ParallaxMap",          new LilToonPropertyInfo(ShaderUsage.SingleChannel, false) },
            { "_DissolveMask",         new LilToonPropertyInfo(ShaderUsage.SingleChannel, false) },
            { "_DissolveNoiseMask",    new LilToonPropertyInfo(ShaderUsage.SingleChannel, false) },
            { "_Main2ndDissolveMask",  new LilToonPropertyInfo(ShaderUsage.SingleChannel, false) },
            { "_Main2ndDissolveNoiseMask",  new LilToonPropertyInfo(ShaderUsage.SingleChannel, false) },
            { "_Main3rdDissolveMask",  new LilToonPropertyInfo(ShaderUsage.SingleChannel, false) },
            { "_Main3rdDissolveNoiseMask",  new LilToonPropertyInfo(ShaderUsage.SingleChannel, false) },
            { "_AudioLinkLocalMap",    new LilToonPropertyInfo(ShaderUsage.SingleChannel, false) },

            // --- 異種: SingleChannel mask だが lilToon shader 内で .a も参照される ---
            // Phase1 旧実装は alphaUsed=true、Phase2 旧実装は usage=SingleChannel。
            // 両方の挙動を保つため (usage, alpha) = (SingleChannel, true) として登録。
            { "_EmissionBlendMask",    new LilToonPropertyInfo(ShaderUsage.SingleChannel, true) },
            { "_Emission2ndBlendMask", new LilToonPropertyInfo(ShaderUsage.SingleChannel, true) },
        };

        public static bool TryGet(string propName, out LilToonPropertyInfo info)
            => Table.TryGetValue(propName ?? string.Empty, out info);
    }
}
