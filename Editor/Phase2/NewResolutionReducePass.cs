using System.Collections.Generic;
using nadena.dev.ndmf;
using UnityEngine;
using Narazaka.VRChat.Jnto;
using Narazaka.VRChat.Jnto.Editor.Phase1;
using Narazaka.VRChat.Jnto.Editor.Phase2.Cache;
using Narazaka.VRChat.Jnto.Editor.Phase2.Compression;
using Narazaka.VRChat.Jnto.Editor.Phase2.Gate;
using Narazaka.VRChat.Jnto.Editor.Phase2.GpuPipeline;
using Narazaka.VRChat.Jnto.Editor.Phase2.Tiling;
using Narazaka.VRChat.Jnto.Editor.Resolution;

namespace Narazaka.VRChat.Jnto.Editor.Phase2
{
    /// <summary>
    /// M7 新パイプライン (PerceptualGate + TileGrid + BinarySearch + FormatPredictor + Persistent/InMemory cache)
    /// を束ねる NDMF Pass。旧 <see cref="ResolutionReducePass"/> の完全置換であり、JntoPlugin から本 Pass を
    /// Optimizing フェーズに登録する。
    /// </summary>
    public class NewResolutionReducePass : Pass<NewResolutionReducePass>
    {
        protected override void Execute(BuildContext ctx)
        {
            var root = ctx.AvatarRootObject;
            if (root.GetComponentInChildren<TextureOptimizer>(true) == null) return;

            var animator = root.GetComponent<Animator>();
            var bonemap = BoneClassifier.ClassifyHumanoid(animator);
            var graph = TextureReferenceCollector.Collect(root);

            var rendererSettings = new Dictionary<Renderer, ResolvedSettings>();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var s = SettingsResolver.Resolve(r.transform);
                if (s == null) continue;
                rendererSettings[r] = s;
            }

            using (var cache = new InMemoryCache())
            {
                var replaced = new Dictionary<Texture2D, Texture2D>();
                foreach (var kv in graph.Map)
                {
                    if (!(kv.Key is Texture2D tex)) continue;
                    ProcessTexture(tex, kv.Value, rendererSettings, bonemap, cache, replaced);
                }
                ApplyReplacements(root, graph, replaced);
            }
        }

        void ProcessTexture(
            Texture2D tex, List<TextureReference> refs,
            Dictionary<Renderer, ResolvedSettings> rendererSettings,
            Dictionary<Transform, BoneCategory> bonemap,
            InMemoryCache cache,
            Dictionary<Texture2D, Texture2D> replaced)
        {
            ResolvedSettings settings = null;
            foreach (var r in refs)
            {
                if (r.RendererContext == null) continue;
                if (rendererSettings.TryGetValue(r.RendererContext, out settings)) break;
            }
            if (settings == null) return;

            bool alphaRequired = false;
            Material repMat = null; string repProp = null;
            foreach (var r in refs)
            {
                if (r.Material != null && LilTexAlphaUsageAnalyzer.IsAlphaUsed(r.Material, r.PropertyName))
                {
                    alphaRequired = true;
                    repMat = r.Material;
                    repProp = r.PropertyName;
                    break;
                }
                if (repMat == null && r.Material != null)
                {
                    repMat = r.Material;
                    repProp = r.PropertyName;
                }
            }
            var role = TextureTypeClassifier.Classify(repMat, repProp, tex, alphaRequired);
            var calib = settings.Calibration as DegradationCalibration ?? DegradationCalibration.Default();

            // Persistent cache lookup
            var cacheKey = CacheKeyBuilder.Build(tex, role, refs, settings);
            var cached = PersistentCache.TryLoad(cacheKey, settings.CacheMode);
            if (cached != null && cached.CompressedRawBytes != null && cached.CompressedRawBytes.Length > 0)
            {
                var restored = RestoreFromRaw(tex, cached);
                if (restored != null)
                {
                    ObjectRegistry.RegisterReplacedObject(tex, restored);
                    replaced[tex] = restored;
                    return;
                }
            }

            // Tile grid + r(T)
            var sources = new List<(Renderer renderer, Mesh mesh)>();
            foreach (var r in refs)
            {
                if (r.RendererContext == null) continue;
                Mesh m = GetMesh(r.RendererContext);
                if (m != null) sources.Add((r.RendererContext, m));
            }
            var grid = TileGridBuilder.Build(tex.width, tex.height, sources, bonemap, rendererSettings);
            var rPerTile = new float[grid.Tiles.Length];
            for (int i = 0; i < grid.Tiles.Length; i++)
            {
                rPerTile[i] = EffectiveResolutionCalculator.ComputeR(
                    grid.Tiles[i], grid.TileSize, settings.ViewDistanceCm,
                    settings.HMDPixelsPerDegree, settings.Preset);
            }

            // GPU context + block stats (cached per build)
            if (!cache.Contexts.TryGetValue(tex, out var gpuCtx))
            {
                gpuCtx = GpuTextureContext.FromTexture2D(tex);
                cache.Contexts[tex] = gpuCtx;
            }
            if (!cache.BlockStats.TryGetValue(tex, out var stats))
            {
                stats = BlockStatsComputer.Compute(gpuCtx.Original, tex.width, tex.height);
                cache.BlockStats[tex] = stats;
            }

            var pipeline = new NewPhase2Pipeline(calib, role);
            var result = pipeline.Find(tex, gpuCtx, grid, rPerTile, role, settings, stats);

            if (result.Final != tex)
            {
                ObjectRegistry.RegisterReplacedObject(tex, result.Final);
                replaced[tex] = result.Final;

                PersistentCache.Store(cacheKey, new CachedTextureResult
                {
                    FinalSize = result.Size,
                    FinalFormatName = result.Format.ToString(),
                    CompressedRawBytes = result.Final.GetRawTextureData(),
                }, settings.CacheMode);
            }
        }

        static Texture2D RestoreFromRaw(Texture2D orig, CachedTextureResult cached)
        {
            if (!System.Enum.TryParse<TextureFormat>(cached.FinalFormatName, out var fmt)) return null;
            try
            {
                var t = new Texture2D(cached.FinalSize, cached.FinalSize, fmt, true);
                t.name = orig.name + "_cached";
                t.LoadRawTextureData(cached.CompressedRawBytes);
                t.Apply();
                return t;
            }
            catch
            {
                return null;
            }
        }

        static void ApplyReplacements(GameObject root, TextureReferenceGraph graph, Dictionary<Texture2D, Texture2D> replaced)
        {
            if (replaced.Count == 0) return;
            var affectedMats = new HashSet<Material>();
            foreach (var kv in replaced)
            {
                if (graph.Map.TryGetValue(kv.Key, out var refs))
                {
                    foreach (var r in refs)
                    {
                        if (r.Material != null) affectedMats.Add(r.Material);
                    }
                }
            }

            var cloneMap = new Dictionary<Material, Material>();
            Shared.MaterialCloner.ReplaceOnRenderers(root, cloneMap, m => affectedMats.Contains(m));
            Phase1.AlphaStripPass.SafeSetTextures(root, cloneMap, replaced);
        }

        static Mesh GetMesh(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer smr) return smr.sharedMesh;
            if (renderer is MeshRenderer mr)
            {
                var mf = mr.GetComponent<MeshFilter>();
                return mf != null ? mf.sharedMesh : null;
            }
            return null;
        }
    }
}
