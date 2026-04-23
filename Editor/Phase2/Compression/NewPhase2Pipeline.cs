using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Profiling;
using Narazaka.VRChat.Jnto;
using Narazaka.VRChat.Jnto.Editor.Phase2.Gate;
using Narazaka.VRChat.Jnto.Editor.Phase2.GpuPipeline;
using Narazaka.VRChat.Jnto.Editor.Phase2.Tiling;
using Narazaka.VRChat.Jnto.Editor.Resolution;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Compression
{
    public class NewPhase2Result
    {
        public Texture2D Final;
        public int Size;    // = max(Width, Height)
        public int Width;
        public int Height;
        public TextureFormat Format;
        public GateVerdict FinalVerdict;
        public string DecisionReason;
        public bool CacheHit;
        public float ProcessingMs;
    }

    /// <summary>
    /// R-D4-2 容量最小化探索パイプライン。
    ///
    /// アルゴリズム:
    ///   1. FormatCandidateSelector.Select で候補 fmt 集合を得る
    ///   2. CompressionCandidateEnumerator.Enumerate で (size × fmt) 全候補を列挙
    ///      (必ず no-op を含み、bytes ≤ origBytes、bytes ASC + bpp DESC でソート済み)
    ///   3. 順に gate 評価して、最初に pass したものを採用
    ///      - no-op は自明 pass でスキップ
    ///      - downscale 必要 → downscale gate
    ///      - fmt 変更必要 → compression gate
    ///      - 両方必要なら両方通す
    ///   4. 全 fail でも no-op が必ずあるので orig を返す
    ///
    /// 旧: BinarySearchStrategy + FormatPredictor + EnforceRoleConstraint + BC7Fallback は
    /// FormatCandidateSelector + 容量最小化探索に統合された。
    /// </summary>
    public class NewPhase2Pipeline
    {
        readonly DegradationCalibration _calib;
        readonly PerceptualGate _gate;
        readonly IMetric[] _downscaleMetrics;
        readonly IMetric[] _compressionMetrics;
        readonly ShaderUsage _usage;
        readonly bool _alphaUsed;
        readonly bool _isLinear;

        public NewPhase2Pipeline(DegradationCalibration calib, ShaderUsage usage, bool alphaUsed)
        {
            _calib = calib;
            _gate = new PerceptualGate(calib);
            _usage = usage;
            _alphaUsed = alphaUsed;
            _downscaleMetrics = BuildMetrics(usage, alphaUsed, MetricContext.Downscale);
            _compressionMetrics = BuildMetrics(usage, alphaUsed, MetricContext.Compression);
            // バグ#2 回帰防止: NormalMap / SingleChannel は linear color space で扱う。
            _isLinear = usage == ShaderUsage.Normal || usage == ShaderUsage.SingleChannel;
        }

        static IMetric[] BuildMetrics(ShaderUsage usage, bool alphaUsed, MetricContext ctx)
        {
            var list = new List<IMetric>();
            list.Add(new MsslMetric());
            if (ctx == MetricContext.Downscale || ctx == MetricContext.Both)
            {
                list.Add(new RidgeMetric());
            }
            if (ctx == MetricContext.Compression || ctx == MetricContext.Both)
            {
                list.Add(new BandingMetric());
                list.Add(new BlockBoundaryMetric());
                if (usage == ShaderUsage.Color && alphaUsed) list.Add(new AlphaQuantizationMetric());
            }
            if (usage == ShaderUsage.Normal) list.Add(new NormalAngleMetric());
            return list.ToArray();
        }

        public NewPhase2Result Find(
            Texture2D orig, GpuTextureContext origCtx,
            UvTileGrid grid, float[] rPerTile,
            ResolvedSettings settings)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int origSize = Mathf.Max(orig.width, orig.height);

            int coveredCount = 0;
            for (int i = 0; i < grid.Tiles.Length; i++)
                if (grid.Tiles[i].HasCoverage && rPerTile[i] > 0f) coveredCount++;

            if (coveredCount == 0)
            {
                sw.Stop();
                UnityEngine.Debug.Log($"[JNTO/pipeline] {orig.name}: no tile coverage, keeping original");
                return new NewPhase2Result
                {
                    Final = orig,
                    Size = origSize,
                    Width = orig.width,
                    Height = orig.height,
                    Format = orig.format,
                    FinalVerdict = new GateVerdict { Pass = false, TextureScore = 0f, WorstTileIndex = -1, DominantMetric = null, DominantMipLevel = -1 },
                    DecisionReason = "skipped: no tile coverage (cannot evaluate)",
                    ProcessingMs = (float)sw.Elapsed.TotalMilliseconds,
                };
            }

            var fmts = FormatCandidateSelector.Select(orig.format, _usage, _alphaUsed);
            var candidates = CompressionCandidateEnumerator.Enumerate(
                orig.width, orig.height, orig.format, fmts, DensityCalculator.MinSize);

            NewPhase2Result result = null;
            var failLog = new StringBuilder();
            int tried = 0;

            Profiler.BeginSample("JNTO.CandidateLoop");
            try
            {
                foreach (var cand in candidates)
                {
                    tried++;

                    // No-op: 自明 pass、常に最後の保険として採用可能
                    if (cand.IsNoOp)
                    {
                        sw.Stop();
                        result = new NewPhase2Result
                        {
                            Final = orig,
                            Size = Mathf.Max(orig.width, orig.height),
                            Width = orig.width,
                            Height = orig.height,
                            Format = orig.format,
                            FinalVerdict = new GateVerdict { Pass = true, TextureScore = 0f },
                            DecisionReason =
                                $"accepted NO-OP (size={cand.Width}x{cand.Height}, fmt={cand.Format}, bytes={cand.Bytes}) "
                                + $"after {tried}/{candidates.Count} candidates. Fails: [{failLog}]",
                            ProcessingMs = (float)sw.Elapsed.TotalMilliseconds,
                        };
                        return result;
                    }

                    bool sizeDiffers = (cand.Width != orig.width || cand.Height != orig.height);
                    bool fmtDiffers = (cand.Format != orig.format);

                    // 1) downscale gate if sizeDiffers
                    GateVerdict downVerdict = new GateVerdict { Pass = true, TextureScore = 0f };
                    RenderTexture referenceRt = null;
                    if (sizeDiffers)
                    {
                        referenceRt = PyramidBuilder.CreatePyramid(
                            origCtx.Original, cand.Width, cand.Height,
                            $"Jnto_Cand_{cand.Width}x{cand.Height}", _isLinear);
                        downVerdict = _gate.Evaluate(
                            origCtx.Original, referenceRt, grid, rPerTile,
                            settings.Preset, _downscaleMetrics);
                        if (!downVerdict.Pass)
                        {
                            referenceRt.Release();
                            Object.DestroyImmediate(referenceRt);
                            AppendFail(failLog, cand, "downscale", downVerdict);
                            continue;
                        }
                    }

                    // 2) compression gate if fmtDiffers
                    GateVerdict compVerdict = new GateVerdict { Pass = true, TextureScore = 0f };
                    if (fmtDiffers)
                    {
                        // reference = downsampled Texture2D (CompressTexture に必要)
                        var downsampled = ResolutionReducer.Resize(orig, Mathf.Max(cand.Width, cand.Height));
                        Texture2D candidateTex = null;
                        try
                        {
                            candidateTex = TextureEncodeDecode.EncodeAndDecode(downsampled, cand.Format);
                            using (var candCtx = GpuTextureContext.FromTexture2D(candidateTex, _isLinear))
                            using (var refDownCtx = GpuTextureContext.FromTexture2D(downsampled, _isLinear))
                            {
                                compVerdict = _gate.Evaluate(refDownCtx.Original, candCtx.Original,
                                    grid, rPerTile, settings.Preset, _compressionMetrics);
                            }
                        }
                        finally
                        {
                            if (candidateTex != null) Object.DestroyImmediate(candidateTex);
                            Object.DestroyImmediate(downsampled);
                        }
                        if (!compVerdict.Pass)
                        {
                            if (referenceRt != null)
                            {
                                referenceRt.Release();
                                Object.DestroyImmediate(referenceRt);
                            }
                            AppendFail(failLog, cand, "compression", compVerdict);
                            continue;
                        }
                    }

                    // 両 gate pass → 採用、encode して return
                    if (referenceRt != null)
                    {
                        referenceRt.Release();
                        Object.DestroyImmediate(referenceRt);
                    }

                    Texture2D final;
                    Profiler.BeginSample("JNTO.FinalEncode");
                    try
                    {
                        final = Encode(orig, cand.Width, cand.Height, cand.Format);
                    }
                    finally { Profiler.EndSample(); }

                    sw.Stop();
                    var finalVerdict = fmtDiffers ? compVerdict : downVerdict;
                    var reason = new StringBuilder();
                    reason.Append("accepted ");
                    reason.Append(cand.Format).Append('@').Append(cand.Width).Append('x').Append(cand.Height);
                    reason.Append(" (bytes=").Append(cand.Bytes).Append(')');
                    reason.Append($" after {tried}/{candidates.Count} candidates.");
                    if (sizeDiffers) reason.Append($" downscale score={downVerdict.TextureScore:F3}.");
                    if (fmtDiffers) reason.Append($" compression score={compVerdict.TextureScore:F3}.");
                    if (failLog.Length > 0) reason.Append(" Fails: [").Append(failLog).Append(']');
                    result = new NewPhase2Result
                    {
                        Final = final,
                        Size = Mathf.Max(final.width, final.height),
                        Width = final.width,
                        Height = final.height,
                        Format = cand.Format,
                        FinalVerdict = finalVerdict,
                        DecisionReason = reason.ToString(),
                        ProcessingMs = (float)sw.Elapsed.TotalMilliseconds,
                    };
                    return result;
                }
            }
            finally { Profiler.EndSample(); }

            // 通常ここに到達しない (no-op が必ずある)。フェイルセーフ: orig を返す。
            sw.Stop();
            return new NewPhase2Result
            {
                Final = orig,
                Size = origSize,
                Width = orig.width,
                Height = orig.height,
                Format = orig.format,
                FinalVerdict = new GateVerdict { Pass = false, TextureScore = 0f },
                DecisionReason = $"fallback (no candidate passed, {tried} tried). Fails: [{failLog}]",
                ProcessingMs = (float)sw.Elapsed.TotalMilliseconds,
            };
        }

        static void AppendFail(StringBuilder sb, CompressionCandidate cand, string gate, GateVerdict v)
        {
            if (sb.Length > 0) sb.Append(" | ");
            sb.Append(cand.Format).Append('@').Append(cand.Width).Append('x').Append(cand.Height)
              .Append(' ').Append(gate).Append(" fail score=").Append(v.TextureScore.ToString("F3"));
            if (!string.IsNullOrEmpty(v.DominantMetric))
                sb.Append(" metric=").Append(v.DominantMetric);
        }

        static Texture2D Encode(Texture2D src, int width, int height, TextureFormat fmt)
        {
            int targetMaxDim = Mathf.Max(width, height);
            var resized = ResolutionReducer.Resize(src, targetMaxDim);
            try
            {
                var tex = new Texture2D(resized.width, resized.height, TextureFormat.RGBA32, true);
                tex.name = $"{src.name}_{resized.width}x{resized.height}_{fmt}";
                tex.SetPixels(resized.GetPixels());
                tex.Apply(updateMipmaps: true);
                UnityEditor.EditorUtility.CompressTexture(tex, fmt,
                    UnityEditor.TextureCompressionQuality.Normal);
                return tex;
            }
            finally
            {
                Object.DestroyImmediate(resized);
            }
        }
    }
}
