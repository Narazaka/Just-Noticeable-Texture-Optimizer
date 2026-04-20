using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Resolution
{
    public static class SettingsResolver
    {
        public static ResolvedSettings Resolve(Transform leaf)
        {
            var chain = new System.Collections.Generic.List<TextureOptimizer>();
            for (var t = leaf; t != null; t = t.parent)
            {
                var c = t.GetComponent<TextureOptimizer>();
                if (c != null) chain.Add(c);
            }
            if (chain.Count == 0) return null;
            chain.Reverse();

            var r = new ResolvedSettings();
            foreach (var c in chain)
            {
                if (c.Preset.HasValue) r.Preset = c.Preset.Value;
                if (c.ViewDistanceCm.HasValue) r.ViewDistanceCm = c.ViewDistanceCm.Value;
                if (c.BoneWeights.HasValue) r.BoneWeights = c.BoneWeights.Value;
            }
            return r;
        }
    }
}
