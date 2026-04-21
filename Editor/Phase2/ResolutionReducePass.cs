using System.Collections.Generic;
using nadena.dev.ndmf;
using UnityEngine;
using Narazaka.VRChat.Jnto.Complexity;
using Narazaka.VRChat.Jnto.Editor.Complexity;
using Narazaka.VRChat.Jnto.Editor.Phase1;
using Narazaka.VRChat.Jnto.Editor.Phase2.Compression;
using Narazaka.VRChat.Jnto.Editor.Resolution;

namespace Narazaka.VRChat.Jnto.Editor.Phase2
{
    public class ResolutionReducePass : Pass<ResolutionReducePass>
    {
        protected override void Execute(BuildContext ctx)
        {
            var root = ctx.AvatarRootObject;
            if (root.GetComponentInChildren<TextureOptimizer>(true) == null) return;

            var animator = root.GetComponent<Animator>();
            var bonemap = BoneClassifier.ClassifyHumanoid(animator);

            var graph = TextureReferenceCollector.Collect(root);

            var rendererStats = new Dictionary<Renderer, List<MeshDensityStats>>();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var settings = SettingsResolver.Resolve(r.transform);
                if (settings == null || settings.BoneWeights == null) continue;
                rendererStats[r] = MeshDensityAnalyzer.Analyze(r, bonemap, settings.BoneWeights);
            }

            var targets = new Dictionary<Texture2D, int>();
            foreach (var kv in graph.Map)
            {
                if (!(kv.Key is Texture2D tex)) continue;
                int maxSize = 0;
                foreach (var tref in kv.Value)
                {
                    if (tref.RendererContext == null) continue;
                    if (!rendererStats.TryGetValue(tref.RendererContext, out var statsList)) continue;
                    var settings = SettingsResolver.Resolve(tref.RendererContext.transform);
                    if (settings == null) continue;
                    float complexityFactor = 1f;
                    var strategy = GetAncestorStrategy(tref.RendererContext.transform);
                    if (strategy != null)
                    {
                        var uvRect = ComputeUvBoundingRect(tref.RendererContext);
                        var (px, sw, sh) = ComplexitySampler.Sample(tex, uvRect);
                        float score = strategy.Measure(px, sw, sh);
                        complexityFactor = Mathf.Lerp(0.5f, 1.0f, score);
                    }
                    foreach (var s in statsList)
                    {
                        int t = DensityCalculator.ComputeTargetSize(s, settings.Preset, settings.ViewDistanceCm, complexityFactor);
                        if (t > maxSize) maxSize = t;
                    }
                }
                if (maxSize == 0) continue;
                int orig = Mathf.Max(tex.width, tex.height);
                int capped = Mathf.Min(orig, maxSize);
                if (capped < orig) targets[tex] = capped;
            }

            var pipeline = new Phase2Pipeline();
            var replaced = new Dictionary<Texture2D, Texture2D>();
            foreach (var kv in targets)
            {
                var tex = kv.Key;
                int target = kv.Value;

                bool alphaRequired = false;
                Material repMat = null; string repProp = null;
                foreach (var tref in graph.Map[tex])
                {
                    if (tref.Material != null && LilTexAlphaUsageAnalyzer.IsAlphaUsed(tref.Material, tref.PropertyName))
                    {
                        alphaRequired = true; repMat = tref.Material; repProp = tref.PropertyName; break;
                    }
                    if (repMat == null && tref.Material != null) { repMat = tref.Material; repProp = tref.PropertyName; }
                }
                var role = TextureTypeClassifier.Classify(repMat, repProp, tex, alphaRequired);

                Transform settingsSource = null;
                foreach (var tref in graph.Map[tex]) if (tref.RendererContext != null) { settingsSource = tref.RendererContext.transform; break; }
                var resolvedSettings = SettingsResolver.Resolve(settingsSource ?? root.transform);
                if (resolvedSettings == null) continue;

                var result = pipeline.Find(tex, target, role, resolvedSettings.Preset);
                if (result.Final != tex)
                {
                    ObjectRegistry.RegisterReplacedObject(tex, result.Final);
                    replaced[tex] = result.Final;
                }
            }

            if (replaced.Count == 0) return;

            var affectedMaterials = new HashSet<Material>();
            foreach (var kv in replaced)
                if (graph.Map.TryGetValue(kv.Key, out var refs))
                    foreach (var tref in refs)
                        if (tref.Material != null) affectedMaterials.Add(tref.Material);

            var cloneMap = new Dictionary<Material, Material>();
            Shared.MaterialCloner.ReplaceOnRenderers(root, cloneMap, m => affectedMaterials.Contains(m));

            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null || m.shader == null) continue;
                    int count = UnityEditor.ShaderUtil.GetPropertyCount(m.shader);
                    for (int i = 0; i < count; i++)
                    {
                        if (UnityEditor.ShaderUtil.GetPropertyType(m.shader, i) != UnityEditor.ShaderUtil.ShaderPropertyType.TexEnv) continue;
                        var name = UnityEditor.ShaderUtil.GetPropertyName(m.shader, i);
                        var t = m.GetTexture(name) as Texture2D;
                        if (t != null && replaced.TryGetValue(t, out var small)) m.SetTexture(name, small);
                    }
                }
            }
        }

        static ComplexityStrategyAsset GetAncestorStrategy(Transform leaf)
        {
            for (var t = leaf; t != null; t = t.parent)
            {
                var c = t.GetComponent<TextureOptimizer>();
                if (c != null && c.ComplexityStrategy != null) return c.ComplexityStrategy;
            }
            return null;
        }

        static Rect ComputeUvBoundingRect(Renderer renderer)
        {
            Mesh mesh = null;
            if (renderer is SkinnedMeshRenderer smr) mesh = smr.sharedMesh;
            else if (renderer is MeshRenderer mr) { var mf = mr.GetComponent<MeshFilter>(); if (mf) mesh = mf.sharedMesh; }
            if (mesh == null) return new Rect(0, 0, 1, 1);
            var uv = mesh.uv;
            if (uv == null || uv.Length == 0) return new Rect(0, 0, 1, 1);
            float mnx = 1, mny = 1, mxx = 0, mxy = 0;
            foreach (var u in uv)
            {
                mnx = Mathf.Min(mnx, u.x); mny = Mathf.Min(mny, u.y);
                mxx = Mathf.Max(mxx, u.x); mxy = Mathf.Max(mxy, u.y);
            }
            mnx = Mathf.Clamp01(mnx); mny = Mathf.Clamp01(mny);
            mxx = Mathf.Clamp01(mxx); mxy = Mathf.Clamp01(mxy);
            if (mxx <= mnx || mxy <= mny) return new Rect(0, 0, 1, 1);
            return Rect.MinMaxRect(mnx, mny, mxx, mxy);
        }
    }
}
