using System.Collections.Generic;
using Narazaka.VRChat.Jnto.Editor.Phase2.Compression;
using static Narazaka.VRChat.Jnto.Editor.Shared.ChannelMask;

namespace Narazaka.VRChat.Jnto.Editor.Shared
{
    /// <summary>
    /// lilToon シェーダーのテクスチャプロパティ分類辞書。
    /// Phase1 (alpha strip) と Phase2 (フォーマット選択) で重複していた property table を一元化する。
    ///
    /// Usage は <see cref="ShaderUsage"/> と一致 (Color / Normal / SingleChannel)。
    /// AlphaUsed は alpha チャネルが意味的に参照されるか (DXT5nm の X 成分含む)。
    /// </summary>
    public static class LilToonTextureCatalog
    {
        const string SEED = "(seed from prev catalog)";

        static readonly Dictionary<string, LilToonPropertyInfo> Table = new Dictionary<string, LilToonPropertyInfo>
        {
            // --- Color, alpha 使用 (RGBA / .a がブレンド係数等) ---
            { "_MainTex",              new LilToonPropertyInfo(ShaderUsage.Color, RGBA, SEED) },
            { "_Main2ndTex",           new LilToonPropertyInfo(ShaderUsage.Color, RGBA, SEED) },
            { "_Main3rdTex",           new LilToonPropertyInfo(ShaderUsage.Color, RGBA, SEED) },
            { "_Main2ndBlendMask",     new LilToonPropertyInfo(ShaderUsage.Color, RGBA, SEED) },
            { "_Main3rdBlendMask",     new LilToonPropertyInfo(ShaderUsage.Color, RGBA, SEED) },
            { "_ShadowColorTex",       new LilToonPropertyInfo(ShaderUsage.Color, RGBA, SEED) },
            { "_Shadow2ndColorTex",    new LilToonPropertyInfo(ShaderUsage.Color, RGBA, SEED) },
            { "_Shadow3rdColorTex",    new LilToonPropertyInfo(ShaderUsage.Color, RGBA, SEED) },
            { "_MatCapTex",            new LilToonPropertyInfo(ShaderUsage.Color, RGBA, SEED) },
            { "_MatCap2ndTex",         new LilToonPropertyInfo(ShaderUsage.Color, RGBA, SEED) },
            { "_RimColorTex",          new LilToonPropertyInfo(ShaderUsage.Color, RGBA, SEED) },
            { "_ReflectionColorTex",   new LilToonPropertyInfo(ShaderUsage.Color, RGBA, SEED) },
            { "_EmissionMap",          new LilToonPropertyInfo(ShaderUsage.Color, RGBA, SEED) },
            { "_EmissionGradTex",      new LilToonPropertyInfo(ShaderUsage.Color, RGBA, SEED) },
            { "_Emission2ndMap",       new LilToonPropertyInfo(ShaderUsage.Color, RGBA, SEED) },
            { "_Emission2ndGradTex",   new LilToonPropertyInfo(ShaderUsage.Color, RGBA, SEED) },
            { "_BacklightColorTex",    new LilToonPropertyInfo(ShaderUsage.Color, RGBA, SEED) },
            { "_GlitterColorTex",      new LilToonPropertyInfo(ShaderUsage.Color, RGBA, SEED) },
            { "_MainGradationTex",     new LilToonPropertyInfo(ShaderUsage.Color, RGBA, SEED) },
            { "_AudioLinkMask",        new LilToonPropertyInfo(ShaderUsage.Color, RGBA, SEED) },
            { "_RimShadeMask",         new LilToonPropertyInfo(ShaderUsage.Color, RGBA, SEED) },

            // --- Color, alpha 不使用 (RGB のみ) ---
            { "_OutlineTex",           new LilToonPropertyInfo(ShaderUsage.Color, RGB, SEED) },
            { "_MatCapBlendMask",      new LilToonPropertyInfo(ShaderUsage.Color, RGB, SEED) },
            { "_MatCap2ndBlendMask",   new LilToonPropertyInfo(ShaderUsage.Color, RGB, SEED) },
            { "_GlitterShapeTex",      new LilToonPropertyInfo(ShaderUsage.Color, RGB, SEED) },

            // --- Normal map ---
            // DXT5nm 系: UnpackNormal が .ag のみ参照 → AlphaUsed=true 派生
            { "_BumpMap",              new LilToonPropertyInfo(ShaderUsage.Normal, A | G, SEED) },
            { "_Bump2ndMap",           new LilToonPropertyInfo(ShaderUsage.Normal, A | G, SEED) },
            { "_MatCapBumpMap",        new LilToonPropertyInfo(ShaderUsage.Normal, A | G, SEED) },
            { "_MatCap2ndBumpMap",     new LilToonPropertyInfo(ShaderUsage.Normal, A | G, SEED) },
            // RGB のみ使用する Normal (アウトライン用ベクトル)
            { "_OutlineBumpMap",       new LilToonPropertyInfo(ShaderUsage.Normal, RGB, SEED) },
            { "_OutlineVectorTex",     new LilToonPropertyInfo(ShaderUsage.Normal, RGB, SEED) },

            // --- SingleChannel mask (R チャネルのみ参照) ---
            { "_ShadowStrengthMask",   new LilToonPropertyInfo(ShaderUsage.SingleChannel, R, SEED) },
            { "_ShadowBorderMask",     new LilToonPropertyInfo(ShaderUsage.SingleChannel, R, SEED) },
            { "_ShadowBlurMask",       new LilToonPropertyInfo(ShaderUsage.SingleChannel, R, SEED) },
            { "_OutlineWidthMask",     new LilToonPropertyInfo(ShaderUsage.SingleChannel, R, SEED) },
            { "_MainColorAdjustMask",  new LilToonPropertyInfo(ShaderUsage.SingleChannel, R, SEED) },
            { "_SmoothnessTex",        new LilToonPropertyInfo(ShaderUsage.SingleChannel, R, SEED) },
            { "_MetallicGlossMap",     new LilToonPropertyInfo(ShaderUsage.SingleChannel, R, SEED) },
            { "_AlphaMask",            new LilToonPropertyInfo(ShaderUsage.SingleChannel, R, SEED) },
            { "_Bump2ndScaleMask",     new LilToonPropertyInfo(ShaderUsage.SingleChannel, R, SEED) },
            { "_ParallaxMap",          new LilToonPropertyInfo(ShaderUsage.SingleChannel, R, SEED) },
            { "_DissolveMask",         new LilToonPropertyInfo(ShaderUsage.SingleChannel, R, SEED) },
            { "_DissolveNoiseMask",    new LilToonPropertyInfo(ShaderUsage.SingleChannel, R, SEED) },
            { "_Main2ndDissolveMask",  new LilToonPropertyInfo(ShaderUsage.SingleChannel, R, SEED) },
            { "_Main2ndDissolveNoiseMask",  new LilToonPropertyInfo(ShaderUsage.SingleChannel, R, SEED) },
            { "_Main3rdDissolveMask",  new LilToonPropertyInfo(ShaderUsage.SingleChannel, R, SEED) },
            { "_Main3rdDissolveNoiseMask",  new LilToonPropertyInfo(ShaderUsage.SingleChannel, R, SEED) },
            { "_AudioLinkLocalMap",    new LilToonPropertyInfo(ShaderUsage.SingleChannel, R, SEED) },

            // _EmissionBlendMask / _Emission2ndBlendMask:
            // lil_common_frag.hlsl で `emissionColor *= LIL_SAMPLE_2D_ST(_EmissionBlendMask, ...)`
            // と RGBA 全チャネルが乗算される (emissionColor は float4)。Color 扱いが正しい。
            { "_EmissionBlendMask",    new LilToonPropertyInfo(ShaderUsage.Color, RGBA, SEED) },
            { "_Emission2ndBlendMask", new LilToonPropertyInfo(ShaderUsage.Color, RGBA, SEED) },
        };

        public static bool TryGet(string propName, out LilToonPropertyInfo info)
            => Table.TryGetValue(propName ?? string.Empty, out info);
    }
}
