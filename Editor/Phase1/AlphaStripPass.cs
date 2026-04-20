using System.Collections.Generic;
using nadena.dev.ndmf;
using UnityEngine;

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

            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool dirty = false;
                foreach (var m in mats)
                {
                    if (m == null || m.shader == null) continue;
                    var sh = m.shader;
                    int count = UnityEditor.ShaderUtil.GetPropertyCount(sh);
                    for (int i = 0; i < count; i++)
                    {
                        if (UnityEditor.ShaderUtil.GetPropertyType(sh, i) != UnityEditor.ShaderUtil.ShaderPropertyType.TexEnv) continue;
                        var name = UnityEditor.ShaderUtil.GetPropertyName(sh, i);
                        var tex = m.GetTexture(name);
                        if (tex != null && stripped.TryGetValue(tex, out var rgb))
                        {
                            m.SetTexture(name, rgb);
                            dirty = true;
                        }
                    }
                }
                if (dirty) r.sharedMaterials = mats;
            }
        }
    }
}
