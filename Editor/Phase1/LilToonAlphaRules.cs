using System.Collections.Generic;
using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Phase1
{
    public static class LilToonAlphaRules
    {
        static readonly HashSet<string> NeverAlpha = new HashSet<string>
        {
            "_BumpMap", "_Bump2ndMap", "_MatCapTex", "_MatCap2ndTex",
            "_RimDirTex", "_EmissionGradTex", "_Emission2ndGradTex",
            "_OutlineTex", "_OutlineBumpMap",
        };

        static readonly HashSet<string> AlwaysAlpha = new HashSet<string>
        {
            "_AlphaMask", "_AlphaMaskValue",
            "_EmissionBlendMask", "_Emission2ndBlendMask",
            "_MainGradationTex",
        };

        public static bool IsAlphaUsed(Material mat, string propertyName)
        {
            if (NeverAlpha.Contains(propertyName)) return false;
            if (AlwaysAlpha.Contains(propertyName)) return true;

            switch (propertyName)
            {
                case "_MainTex":
                case "_Main2ndTex":
                case "_Main3rdTex":
                    return IsTransparentish(mat);
                case "_MainColorAdjustMask":
                case "_Main2ndBlendMask":
                case "_Main3rdBlendMask":
                    return true;
            }
            return true;
        }

        static bool IsTransparentish(Material mat)
        {
            if (mat.HasProperty("_TransparentMode"))
            {
                var m = mat.GetFloat("_TransparentMode");
                return m > 0.5f; // lilToonでは _TransparentMode が authoritative
            }
            int q = mat.renderQueue;
            if (q >= 2450 && q < 3000) return true;
            if (q >= 3000) return true;
            if (mat.HasProperty("_Cutoff") && mat.GetFloat("_Cutoff") > 0.0001f) return true;
            return false;
        }
    }
}
