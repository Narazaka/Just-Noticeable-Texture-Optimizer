using System.Collections.Generic;
using nadena.dev.ndmf;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Shared;

namespace Narazaka.VRChat.Jnto.Editor.Phase1
{
    public class AlphaStripPass : Pass<AlphaStripPass>
    {
        protected override void Execute(BuildContext ctx)
        {
            var root = ctx.AvatarRootObject;
            if (root.GetComponentInChildren<TextureOptimizer>(true) == null) return;

            var graph = TextureReferenceCollector.Collect(root);

            var stripped = new Dictionary<Texture, Texture2D>();
            foreach (var kv in graph.Map)
            {
                if (!(kv.Key is Texture2D src)) continue;
                if (IsAlphaFreeFormat(src.format)) continue;

                bool anyAlpha = false;
                foreach (var r in kv.Value)
                {
                    if (r.Material == null) continue;
                    if (LilTexAlphaUsageAnalyzer.IsAlphaUsed(r.Material, r.PropertyName)) { anyAlpha = true; break; }
                }
                if (anyAlpha) continue;

                var rgb = AlphaStripper.StripAlpha(src);
                ObjectRegistry.RegisterReplacedObject(src, rgb);
                stripped[src] = rgb;
            }

            if (stripped.Count == 0) return;

            var affectedMaterials = new HashSet<Material>();
            foreach (var kv in stripped)
                if (graph.Map.TryGetValue(kv.Key, out var refs))
                    foreach (var r in refs)
                        if (r.Material != null) affectedMaterials.Add(r.Material);

            var cloneMap = new Dictionary<Material, Material>();
            MaterialCloner.ReplaceOnRenderers(root, cloneMap, m => affectedMaterials.Contains(m));

            SafeSetTextures(root, cloneMap, stripped);
        }

        static bool IsAlphaFreeFormat(TextureFormat fmt)
        {
            switch (fmt)
            {
                case TextureFormat.RGB24:
                case TextureFormat.RGB565:
                case TextureFormat.DXT1:
                case TextureFormat.DXT1Crunched:
                case TextureFormat.BC4:
                case TextureFormat.BC5:
                case TextureFormat.R8:
                case TextureFormat.R16:
                case TextureFormat.RG16:
                case TextureFormat.RFloat:
                case TextureFormat.RGFloat:
                case TextureFormat.RHalf:
                case TextureFormat.RGHalf:
                    return true;
                default:
                    return false;
            }
        }

        internal static void SafeSetTextures<T>(GameObject root, Dictionary<Material, Material> cloneMap, Dictionary<T, Texture2D> replacements) where T : Texture
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool matArrayDirty = false;
                for (int mi = 0; mi < mats.Length; mi++)
                {
                    var m = mats[mi];
                    if (m == null || m.shader == null) continue;
                    int count = UnityEditor.ShaderUtil.GetPropertyCount(m.shader);
                    for (int i = 0; i < count; i++)
                    {
                        if (UnityEditor.ShaderUtil.GetPropertyType(m.shader, i) != UnityEditor.ShaderUtil.ShaderPropertyType.TexEnv) continue;
                        var name = UnityEditor.ShaderUtil.GetPropertyName(m.shader, i);
                        var tex = m.GetTexture(name) as T;
                        if (tex != null && replacements.TryGetValue(tex, out var replacement))
                        {
                            if (cloneMap.TryGetValue(m, out var existingClone))
                            {
                                mats[mi] = existingClone;
                                m = existingClone;
                                matArrayDirty = true;
                            }
                            else if (!cloneMap.ContainsValue(m))
                            {
                                Debug.LogWarning($"[JNTO] Failsafe clone: {m.name} on {r.name} (prop={name})");
                                var clone = Object.Instantiate(m);
                                clone.name = m.name;
                                cloneMap[m] = clone;
                                mats[mi] = clone;
                                m = clone;
                                matArrayDirty = true;
                            }
                            m.SetTexture(name, replacement);
                        }
                    }
                }
                if (matArrayDirty) r.sharedMaterials = mats;
            }
        }
    }
}
