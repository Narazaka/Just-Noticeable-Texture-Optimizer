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
                if (src.format == TextureFormat.RGB24 || src.format == TextureFormat.RGB565) continue;

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

            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                foreach (var m in mats)
                {
                    if (m == null || m.shader == null) continue;
                    int count = UnityEditor.ShaderUtil.GetPropertyCount(m.shader);
                    for (int i = 0; i < count; i++)
                    {
                        if (UnityEditor.ShaderUtil.GetPropertyType(m.shader, i) != UnityEditor.ShaderUtil.ShaderPropertyType.TexEnv) continue;
                        var name = UnityEditor.ShaderUtil.GetPropertyName(m.shader, i);
                        var tex = m.GetTexture(name);
                        if (tex != null && stripped.TryGetValue(tex, out var rgb))
                            m.SetTexture(name, rgb);
                    }
                }
            }
        }
    }
}
