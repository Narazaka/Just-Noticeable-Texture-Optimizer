using System.Collections.Generic;
using nadena.dev.ndmf;
using UnityEngine;
using UnityEngine.Profiling;
using Narazaka.VRChat.Jnto;
using Narazaka.VRChat.Jnto.Editor.Phase1;
using Narazaka.VRChat.Jnto.Editor.Phase2.Cache;
using Narazaka.VRChat.Jnto.Editor.Phase2.Compression;
using Narazaka.VRChat.Jnto.Editor.Phase2.Density;
using Narazaka.VRChat.Jnto.Editor.Phase2.Gate;
using Narazaka.VRChat.Jnto.Editor.Phase2.GpuPipeline;
using Narazaka.VRChat.Jnto.Editor.Phase2.Tiling;
using Narazaka.VRChat.Jnto.Editor.Resolution;
using Narazaka.VRChat.Jnto.Editor.Shared;

namespace Narazaka.VRChat.Jnto.Editor.Phase2
{
    /// <summary>
    /// PerceptualGate + TileGrid + FormatCandidateSelector +
    /// CompressionCandidateEnumerator + Persistent/InMemory cache を束ねる NDMF Pass。
    /// (size × fmt) 容量最小化探索を行う。
    /// </summary>
    public class ResolutionReducePass : Pass<ResolutionReducePass>
    {
        protected override void Execute(BuildContext ctx)
        {
            Profiler.BeginSample("JNTO.Execute");
            try
            {
                Reporting.DecisionLog.Clear();
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

                Reporting.JntoNdmfReport.Emit();
            }
            finally
            {
                Profiler.EndSample();
            }
        }

        void ProcessTexture(
            Texture2D tex, List<TextureReference> refs,
            Dictionary<Renderer, ResolvedSettings> rendererSettings,
            Dictionary<Transform, BoneCategory> bonemap,
            InMemoryCache cache,
            Dictionary<Texture2D, Texture2D> replaced)
        {
            UnityEngine.Profiling.Profiler.BeginSample("JNTO.ProcessTexture." + (tex != null ? tex.name : "?"));
            try
            {
                ResolvedSettings settings = null;
                foreach (var r in refs)
                {
                    if (r.RendererContext == null) continue;
                    if (rendererSettings.TryGetValue(r.RendererContext, out settings)) break;
                }
                if (settings == null) return;

                bool alphaUsed = false;
                ShaderUsage aggregatedUsage = ShaderUsage.Color;
                Material repMat = null; string repProp = null;
                foreach (var r in refs)
                {
                    if (r.Material == null) continue;

                    if (LilTexAlphaUsageAnalyzer.IsAlphaUsed(r.Material, r.PropertyName))
                        alphaUsed = true;

                    var refUsage = ShaderUsageInferrer.Infer(r.Material, r.PropertyName, tex);
                    if (refUsage == ShaderUsage.Normal)
                        aggregatedUsage = ShaderUsage.Normal;
                    else if (refUsage == ShaderUsage.SingleChannel && aggregatedUsage != ShaderUsage.Normal)
                        aggregatedUsage = ShaderUsage.SingleChannel;

                    if (repMat == null)
                    {
                        repMat = r.Material;
                        repProp = r.PropertyName;
                    }
                }
                var usage = aggregatedUsage;
                // role は cache key とバグ#2 (linear/sRGB RT 選択) のために導出する。
                var role = UsageToRole(usage, alphaUsed, tex.format);
                bool isLinear = Phase2.GpuPipeline.TextureColorSpace.IsLinear(tex, usage);
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
                        if (System.Enum.TryParse<TextureFormat>(cached.FinalFormatName, out var cfmt))
                        {
                            Reporting.DecisionLog.Add(new Reporting.DecisionRecord
                            {
                                OriginalTexture = tex,
                                OrigSize = Mathf.Max(tex.width, tex.height),
                                FinalSize = cached.FinalSize,
                                OrigFormat = tex.format,
                                FinalFormat = cfmt,
                                SavedBytes = EstimateSavedBytesRaw(tex, cached.FinalSize, cfmt),
                                TextureScore = 0f,
                                DominantMetric = "-",
                                DominantMipLevel = -1,
                                WorstTileIndex = -1,
                                CacheHit = true,
                                ProcessingMs = 0f,
                                Reason = "cache hit",
                            });
                        }
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
                        grid.Tiles[i], grid.TileSize,
                        grid.TextureWidth, grid.TextureHeight,
                        settings.ViewDistanceCm,
                        settings.HMDPixelsPerDegree, settings.Preset);
                }

                int coveredTileCount = 0;
                int rNonZeroCount = 0;
                for (int i = 0; i < grid.Tiles.Length; i++)
                {
                    if (grid.Tiles[i].HasCoverage) coveredTileCount++;
                    if (rPerTile[i] > 0f) rNonZeroCount++;
                }
                UnityEngine.Debug.Log(
                    $"[JNTO/grid] {tex.name}: tiles={grid.Tiles.Length} ({grid.TilesX}x{grid.TilesY}, ts={grid.TileSize}), " +
                    $"covered={coveredTileCount}, rNonZero={rNonZeroCount}, usage={usage}, alpha={alphaUsed}, refs={refs.Count}");

                // 早期スキップ: 評価不能なら処理対象外
                if (rNonZeroCount == 0)
                {
                    UnityEngine.Debug.LogWarning($"[JNTO/grid] {tex.name}: skipped (no evaluable tiles)");
                    return;
                }

                // GPU context (cached per build)
                if (!cache.Contexts.TryGetValue(tex, out var gpuCtx))
                {
                    gpuCtx = GpuTextureContext.FromTexture2D(tex, isLinear);
                    cache.Contexts[tex] = gpuCtx;
                }

                var pipeline = new Phase2Pipeline(calib, usage, alphaUsed, settings.EnableChromaDrift, tex.format, isLinear);
                Phase2Result result;
                Profiler.BeginSample("JNTO.PipelineFind");
                try
                {
                    result = pipeline.Find(tex, gpuCtx, grid, rPerTile, settings);
                }
                finally
                {
                    Profiler.EndSample();
                }

                if (result.Final != tex)
                {
                    ObjectRegistry.RegisterReplacedObject(tex, result.Final);
                    replaced[tex] = result.Final;

                    Profiler.BeginSample("JNTO.CacheStore");
                    try
                    {
                        PersistentCache.Store(cacheKey, new CachedTextureResult
                        {
                            FinalSize = result.Size,
                            FinalWidth = result.Width,
                            FinalHeight = result.Height,
                            FinalFormatName = result.Format.ToString(),
                            CompressedRawBytes = result.Final.GetRawTextureData(),
                        }, settings.CacheMode);
                    }
                    finally
                    {
                        Profiler.EndSample();
                    }
                }

                // no-op であっても DecisionLog には残す (どのテクスチャが kept か可視化)。
                Reporting.DecisionLog.Add(new Reporting.DecisionRecord
                {
                    OriginalTexture = tex,
                    OrigSize = Mathf.Max(tex.width, tex.height),
                    FinalSize = result.Size,
                    OrigFormat = tex.format,
                    FinalFormat = result.Format,
                    SavedBytes = EstimateSavedBytes(tex, result),
                    TextureScore = result.FinalVerdict.TextureScore,
                    DominantMetric = result.FinalVerdict.DominantMetric ?? "-",
                    DominantMipLevel = result.FinalVerdict.DominantMipLevel,
                    WorstTileIndex = result.FinalVerdict.WorstTileIndex,
                    CacheHit = false,
                    ProcessingMs = result.ProcessingMs,
                    Reason = result.DecisionReason,
                });
            }
            finally
            {
                UnityEngine.Profiling.Profiler.EndSample();
            }
        }

        /// <summary>
        /// (ShaderUsage, alphaUsed, origFmt) から cache key 用の TextureRole を導出する。
        /// 新パイプライン内部は role を持たないが、CacheKeyBuilder の互換のために必要。
        /// </summary>
        static TextureRole UsageToRole(ShaderUsage usage, bool alphaUsed, TextureFormat origFmt)
        {
            if (usage == ShaderUsage.Normal) return TextureRole.NormalMap;
            if (usage == ShaderUsage.SingleChannel) return TextureRole.SingleChannel;

            // Color
            if (origFmt == TextureFormat.BC4 || origFmt == TextureFormat.R8 || origFmt == TextureFormat.Alpha8)
                return TextureRole.SingleChannel;
            if (origFmt == TextureFormat.BC5 || origFmt == TextureFormat.RG16)
                return TextureRole.NormalMap;
            if (origFmt == TextureFormat.DXT1 || origFmt == TextureFormat.DXT1Crunched
                || origFmt == TextureFormat.BC6H || origFmt == TextureFormat.RGB24)
                return TextureRole.ColorOpaque;
            return alphaUsed ? TextureRole.ColorAlpha : TextureRole.ColorOpaque;
        }

        static Texture2D RestoreFromRaw(Texture2D orig, CachedTextureResult cached)
        {
            if (!System.Enum.TryParse<TextureFormat>(cached.FinalFormatName, out var fmt)) return null;
            int w = cached.FinalWidth > 0 ? cached.FinalWidth : cached.FinalSize;
            int h = cached.FinalHeight > 0 ? cached.FinalHeight : cached.FinalSize;
            try
            {
                var t = new Texture2D(w, h, fmt, true);
                t.name = orig.name + "_cached";
                t.LoadRawTextureData(cached.CompressedRawBytes);
                t.Apply(updateMipmaps: false);
                Compression.TextureEncodeDecode.EnableStreamingMipmaps(t);
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

        static long EstimateSavedBytes(Texture2D orig, Phase2Result r)
        {
            long origBytes = BytesEstimator.WithMips(orig.width, orig.height, orig.format);
            long newBytes = BytesEstimator.WithMips(r.Width, r.Height, r.Format);
            return origBytes - newBytes;
        }

        static long EstimateSavedBytesRaw(Texture2D orig, int finalSize, TextureFormat finalFmt)
        {
            long origBytes = BytesEstimator.WithMips(orig.width, orig.height, orig.format);
            // cache 経由で W/H が失われた場合は square 推定
            long newBytes = BytesEstimator.WithMips(finalSize, finalSize, finalFmt);
            return origBytes - newBytes;
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
