using System.Collections.Generic;
using nadena.dev.ndmf;
using UnityEngine;
using UnityEngine.Profiling;
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
                // バグ#2 回帰防止: NormalMap / SingleChannel は linear texture なので
                // GpuTextureContext/PyramidBuilder 側でも sRGB=false の RT を使う。
                bool isLinear = role == TextureRole.NormalMap || role == TextureRole.SingleChannel;
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
                        grid.Tiles[i], grid.TileSize, settings.ViewDistanceCm,
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
                    $"covered={coveredTileCount}, rNonZero={rNonZeroCount}, role={role}, refs={refs.Count}");

                // 早期スキップ: 評価不能なら処理対象外
                if (rNonZeroCount == 0)
                {
                    UnityEngine.Debug.LogWarning($"[JNTO/grid] {tex.name}: skipped (no evaluable tiles)");
                    return;
                }

                // GPU context + block stats (cached per build)
                if (!cache.Contexts.TryGetValue(tex, out var gpuCtx))
                {
                    gpuCtx = GpuTextureContext.FromTexture2D(tex, isLinear);
                    cache.Contexts[tex] = gpuCtx;
                }
                if (!cache.BlockStats.TryGetValue(tex, out var stats))
                {
                    Profiler.BeginSample("JNTO.BlockStats");
                    try
                    {
                        stats = BlockStatsComputer.Compute(gpuCtx.Original, tex.width, tex.height);
                    }
                    finally
                    {
                        Profiler.EndSample();
                    }
                    cache.BlockStats[tex] = stats;
                }

                var pipeline = new NewPhase2Pipeline(calib, role);
                NewPhase2Result result;
                Profiler.BeginSample("JNTO.PipelineFind");
                try
                {
                    result = pipeline.Find(tex, gpuCtx, grid, rPerTile, role, settings, stats);
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
            }
            finally
            {
                UnityEngine.Profiling.Profiler.EndSample();
            }
        }

        static Texture2D RestoreFromRaw(Texture2D orig, CachedTextureResult cached)
        {
            if (!System.Enum.TryParse<TextureFormat>(cached.FinalFormatName, out var fmt)) return null;
            // バグ#11 回帰防止: 非正方形テクスチャの W/H を別々に復元。
            // レガシー cache (FinalWidth/Height=0) は TryLoad 側で FinalSize から補完済みだが、
            // 念のためここでも fallback。
            int w = cached.FinalWidth > 0 ? cached.FinalWidth : cached.FinalSize;
            int h = cached.FinalHeight > 0 ? cached.FinalHeight : cached.FinalSize;
            try
            {
                var t = new Texture2D(w, h, fmt, true);
                t.name = orig.name + "_cached";
                t.LoadRawTextureData(cached.CompressedRawBytes);
                t.Apply(updateMipmaps: false);  // raw bytes は既に mipchain を含む
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

        static long EstimateSavedBytes(Texture2D orig, NewPhase2Result r)
        {
            long origBytes = BytesFor(orig.width, orig.height, orig.format);
            long newBytes = BytesFor(r.Size, r.Size, r.Format);
            return origBytes - newBytes;
        }

        static long EstimateSavedBytesRaw(Texture2D orig, int finalSize, TextureFormat finalFmt)
        {
            long origBytes = BytesFor(orig.width, orig.height, orig.format);
            long newBytes = BytesFor(finalSize, finalSize, finalFmt);
            return origBytes - newBytes;
        }

        static long BytesFor(int w, int h, TextureFormat fmt)
        {
            int bpp = fmt == TextureFormat.DXT1 ? 4
                    : fmt == TextureFormat.DXT5 ? 8
                    : fmt == TextureFormat.BC5 || fmt == TextureFormat.BC7 ? 8
                    : fmt == TextureFormat.BC4 ? 4
                    : 32;
            long mipped = (long)w * h * bpp / 8;
            return (long)(mipped * 1.33f);
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
