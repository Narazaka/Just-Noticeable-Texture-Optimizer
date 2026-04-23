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
        readonly IMetric[] _jointMetrics;
        readonly ShaderUsage _usage;
        readonly bool _alphaUsed;
        readonly bool _isLinear;
        readonly TextureFormat _origFormat;

        public NewPhase2Pipeline(DegradationCalibration calib, ShaderUsage usage, bool alphaUsed,
            bool enableChromaDrift = true, TextureFormat origFormat = TextureFormat.RGBA32,
            bool? isLinear = null)
        {
            _calib = calib;
            _gate = new PerceptualGate(calib);
            _usage = usage;
            _alphaUsed = alphaUsed;
            _downscaleMetrics = BuildMetrics(usage, alphaUsed, MetricContext.Downscale, false);
            _compressionMetrics = BuildMetrics(usage, alphaUsed, MetricContext.Compression, enableChromaDrift);
            _jointMetrics = BuildMetrics(usage, alphaUsed, MetricContext.Both, enableChromaDrift);
            _isLinear = isLinear ?? (usage == ShaderUsage.Normal || usage == ShaderUsage.SingleChannel);
            _origFormat = origFormat;
        }

        static bool IsDxt5nmFormat(TextureFormat fmt)
            => fmt == TextureFormat.DXT5 || fmt == TextureFormat.DXT5Crunched;

        static void SetNormalChannelMappings(IMetric[] metrics, int origMapping, int candMapping)
        {
            foreach (var m in metrics)
            {
                if (m is NormalAngleMetric nam)
                {
                    nam.OrigChannelMapping = origMapping;
                    nam.CandChannelMapping = candMapping;
                }
            }
        }

        static IMetric[] BuildMetrics(ShaderUsage usage, bool alphaUsed, MetricContext ctx,
            bool enableChromaDrift)
        {
            var list = new List<IMetric>();
            if (ctx == MetricContext.Downscale || ctx == MetricContext.Both)
            {
                list.Add(new MsslMetric());
                list.Add(new RidgeMetric());
            }
            if (ctx == MetricContext.Compression || ctx == MetricContext.Both)
            {
                if (usage != ShaderUsage.Normal)
                {
                    list.Add(new BandingMetric());
                    list.Add(new BlockBoundaryMetric());
                    if (enableChromaDrift) list.Add(new ChromaDriftMetric());
                }
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
                    FinalVerdict = new GateVerdict { Pass = true, TextureScore = 0f, WorstTileIndex = -1, DominantMetric = null, DominantMipLevel = -1 },
                    DecisionReason = "kept original: no evaluable tile coverage",
                    ProcessingMs = (float)sw.Elapsed.TotalMilliseconds,
                };
            }

            // DXT5nm channel mapping for normal maps
            bool origIsDxt5nm = _usage == ShaderUsage.Normal && IsDxt5nmFormat(_origFormat);
            int dxt5nmMapping = origIsDxt5nm ? 1 : 0;

            var fmts = FormatCandidateSelector.Select(orig.format, _usage, _alphaUsed,
                settings.AllowCrunched);
            var candidates = CompressionCandidateEnumerator.Enumerate(
                orig.width, orig.height, orig.format, fmts, DensityCalculator.MinSize,
                settings.OptimizationTarget);

            NewPhase2Result result = null;
            var failLog = new StringBuilder();
            int tried = 0;

            // r(T) が要求する最小解像度を計算: covered タイルの 98th percentile から
            // 全タイルの max だと 1 タイルの外れ値で全体が制約されるため、パーセンタイルを使用
            var rCovered = new List<float>();
            for (int i = 0; i < grid.Tiles.Length; i++)
                if (grid.Tiles[i].HasCoverage && rPerTile[i] > 0f) rCovered.Add(rPerTile[i]);
            rCovered.Sort();
            float effectiveR = rCovered.Count > 0
                ? rCovered[Mathf.Min(rCovered.Count - 1, (int)(rCovered.Count * 0.98f))]
                : 0f;
            int minRequiredDim = Mathf.Max(DensityCalculator.MinSize,
                DensityCalculator.CeilPowerOfTwo(Mathf.CeilToInt(effectiveR * origSize / grid.TileSize)));

            UnityEngine.Debug.Log($"[JNTO/pipeline] {orig.name}: r98={effectiveR:F1}, minRequired={minRequiredDim}, origSize={origSize}");

            // Crunched 最適化: Crunched は非 Crunched より品質がやや劣るため
            // 独自のゲート評価が必要だが、非 Crunched で fail した場合は
            // Crunched も必ず fail するためスキップできる。
            // key = (width, height, baseFmt)
            var passedGate = new HashSet<(int, int, TextureFormat)>();

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

                    // r(T) が要求する解像度に満たない候補はスキップ
                    int candMaxDim = Mathf.Max(cand.Width, cand.Height);
                    if (candMaxDim < minRequiredDim)
                    {
                        AppendFail(failLog, cand, "resolution",
                            new GateVerdict { TextureScore = -1f, DominantMetric = $"need>={minRequiredDim}" });
                        continue;
                    }

                    // Crunched 候補: 非 Crunched で fail したサイズ+フォーマットは
                    // Crunched でも必ず fail (品質は同等か劣る) のでスキップ。
                    // 非 Crunched で pass したことを確認してからゲート評価を行う。
                    bool isCrunched = cand.Format == TextureFormat.DXT1Crunched
                        || cand.Format == TextureFormat.DXT5Crunched;
                    if (isCrunched)
                    {
                        var baseFmt = cand.Format == TextureFormat.DXT1Crunched
                            ? TextureFormat.DXT1 : TextureFormat.DXT5;
                        if (!passedGate.Contains((cand.Width, cand.Height, baseFmt)))
                        {
                            AppendFail(failLog, cand, "crunch-skip",
                                new GateVerdict { TextureScore = -1f, DominantMetric = $"base {baseFmt} not passed" });
                            continue;
                        }
                        // 非 Crunched は pass 済み → 以降の通常ゲート評価に進む
                        // (Crunch 品質劣化がゲートを通るか確認)
                    }

                    bool sizeDiffers = (cand.Width != orig.width || cand.Height != orig.height);
                    bool fmtDiffers = (cand.Format != orig.format);

                    // 1) downscale gate if sizeDiffers
                    // Both orig and candidate are the same texture at different mip levels,
                    // so both use the same channel mapping.
                    SetNormalChannelMappings(_downscaleMetrics, dxt5nmMapping, dxt5nmMapping);

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
                    Texture2D jointCandidateTex = null;
                    bool candidateRemapped = false;
                    if (fmtDiffers)
                    {
                        var downsampled = ResolutionReducer.Resize(orig, Mathf.Max(cand.Width, cand.Height), _isLinear);
                        Texture2D candidateTex = null;
                        Texture2D compRef = null;
                        try
                        {
                            bool needsRemap = TextureEncodeDecode.NeedsDxt5nmToBC5Remap(orig.format, cand.Format);
                            candidateRemapped = needsRemap;
                            candidateTex = TextureEncodeDecode.EncodeAndDecode(
                                downsampled, cand.Format, _isLinear, orig.format);

                            if (needsRemap)
                            {
                                compRef = new Texture2D(downsampled.width, downsampled.height,
                                    TextureFormat.RGBA32, false, _isLinear);
                                compRef.SetPixels(downsampled.GetPixels());
                                compRef.Apply();
                                TextureEncodeDecode.RemapDxt5nmForBC5(compRef);
                            }

                            var compGrid = RemapGrid(grid, rPerTile, cand.Width, cand.Height,
                                settings, out var compRPerTile);
                            float compRFloor = compGrid.TileSize * 0.5f;
                            for (int ti = 0; ti < compRPerTile.Length; ti++)
                                if (compGrid.Tiles[ti].HasCoverage)
                                    compRPerTile[ti] = Mathf.Max(compRFloor, compRPerTile[ti]);

                            // Compression gate channel mappings:
                            // When needsRemap (DXT5nm→BC5), both ref and candidate are
                            // remapped to standard RG layout, so mapping=0.
                            // Otherwise both use the original DXT5nm mapping.
                            int compMapping = needsRemap ? 0 : dxt5nmMapping;
                            SetNormalChannelMappings(_compressionMetrics, compMapping, compMapping);

                            var refTex = compRef != null ? compRef : downsampled;
                            using (var candCtx = GpuTextureContext.FromTexture2D(candidateTex, _isLinear))
                            using (var refDownCtx = GpuTextureContext.FromTexture2D(refTex, _isLinear))
                            {
                                compVerdict = _gate.Evaluate(refDownCtx.Original, candCtx.Original,
                                    compGrid, compRPerTile, settings.Preset, _compressionMetrics);
                            }

                            if (compVerdict.Pass && sizeDiffers)
                                jointCandidateTex = candidateTex;
                            else
                                Object.DestroyImmediate(candidateTex);
                            candidateTex = null;
                        }
                        finally
                        {
                            if (candidateTex != null) Object.DestroyImmediate(candidateTex);
                            if (compRef != null) Object.DestroyImmediate(compRef);
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

                    // 3) joint gate: size+format 両方変更時、累積劣化を orig vs compressed で検証
                    // Joint gate compares original (DXT5nm layout) vs candidate (remapped to RG if BC5, else DXT5nm).
                    int jointCandMapping = candidateRemapped ? 0 : dxt5nmMapping;
                    SetNormalChannelMappings(_jointMetrics, dxt5nmMapping, jointCandMapping);

                    GateVerdict jointVerdict = new GateVerdict { Pass = true, TextureScore = 0f };
                    if (sizeDiffers && fmtDiffers && jointCandidateTex != null)
                    {
                        try
                        {
                            using (var jointCtx = GpuTextureContext.FromTexture2D(jointCandidateTex, _isLinear))
                            {
                                jointVerdict = _gate.Evaluate(
                                    origCtx.Original, jointCtx.Original,
                                    grid, rPerTile, settings.Preset, _jointMetrics);
                            }
                        }
                        finally
                        {
                            Object.DestroyImmediate(jointCandidateTex);
                            jointCandidateTex = null;
                        }
                        if (!jointVerdict.Pass)
                        {
                            if (referenceRt != null)
                            {
                                referenceRt.Release();
                                Object.DestroyImmediate(referenceRt);
                            }
                            AppendFail(failLog, cand, "joint", jointVerdict);
                            continue;
                        }
                    }
                    else if (jointCandidateTex != null)
                    {
                        Object.DestroyImmediate(jointCandidateTex);
                        jointCandidateTex = null;
                    }

                    // 全 gate pass → gateCache に登録して採用
                    if (referenceRt != null)
                    {
                        referenceRt.Release();
                        Object.DestroyImmediate(referenceRt);
                    }

                    var finalVerdict = jointVerdict.TextureScore > 0f ? jointVerdict
                        : fmtDiffers ? compVerdict : downVerdict;
                    passedGate.Add((cand.Width, cand.Height, cand.Format));

                    Texture2D final;
                    Profiler.BeginSample("JNTO.FinalEncode");
                    try
                    {
                        final = Encode(orig, cand.Width, cand.Height, cand.Format, _isLinear, orig.format);
                    }
                    finally { Profiler.EndSample(); }

                    sw.Stop();
                    var reason = new StringBuilder();
                    reason.Append("accepted ");
                    reason.Append(cand.Format).Append('@').Append(cand.Width).Append('x').Append(cand.Height);
                    reason.Append(" (bytes=").Append(cand.Bytes).Append(')');
                    reason.Append($" after {tried}/{candidates.Count} candidates.");
                    if (sizeDiffers) reason.Append($" downscale score={downVerdict.TextureScore:F3}.");
                    if (fmtDiffers) reason.Append($" compression score={compVerdict.TextureScore:F3}.");
                    if (jointVerdict.TextureScore > 0f) reason.Append($" joint score={jointVerdict.TextureScore:F3}.");
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

        static UvTileGrid RemapGrid(
            UvTileGrid srcGrid, float[] srcRPerTile,
            int dstWidth, int dstHeight,
            ResolvedSettings settings, out float[] dstRPerTile)
        {
            var dst = UvTileGrid.Create(dstWidth, dstHeight);
            dstRPerTile = new float[dst.Tiles.Length];

            for (int dy = 0; dy < dst.TilesY; dy++)
            for (int dx = 0; dx < dst.TilesX; dx++)
            {
                float uMin = (float)dx / dst.TilesX;
                float uMax = (float)(dx + 1) / dst.TilesX;
                float vMin = (float)dy / dst.TilesY;
                float vMax = (float)(dy + 1) / dst.TilesY;

                int sxMin = Mathf.Clamp(Mathf.FloorToInt(uMin * srcGrid.TilesX), 0, srcGrid.TilesX - 1);
                int sxMax = Mathf.Clamp(Mathf.FloorToInt(uMax * srcGrid.TilesX - 0.001f), 0, srcGrid.TilesX - 1);
                int syMin = Mathf.Clamp(Mathf.FloorToInt(vMin * srcGrid.TilesY), 0, srcGrid.TilesY - 1);
                int syMax = Mathf.Clamp(Mathf.FloorToInt(vMax * srcGrid.TilesY - 0.001f), 0, srcGrid.TilesY - 1);

                ref var tile = ref dst.GetTile(dx, dy);
                int dstIdx = dy * dst.TilesX + dx;

                for (int sy = syMin; sy <= syMax; sy++)
                for (int sx = sxMin; sx <= sxMax; sx++)
                {
                    int srcIdx = sy * srcGrid.TilesX + sx;
                    var srcTile = srcGrid.Tiles[srcIdx];
                    if (srcTile.HasCoverage) tile.HasCoverage = true;
                    if (srcTile.Density > tile.Density) tile.Density = srcTile.Density;
                    if (srcTile.BoneWeight > tile.BoneWeight) tile.BoneWeight = srcTile.BoneWeight;
                }

                dstRPerTile[dstIdx] = tile.HasCoverage
                    ? EffectiveResolutionCalculator.ComputeR(
                        tile, dst.TileSize, dstWidth, dstHeight,
                        settings.ViewDistanceCm, settings.HMDPixelsPerDegree, settings.Preset)
                    : 0f;
            }
            return dst;
        }

        static Texture2D Encode(Texture2D src, int width, int height, TextureFormat fmt,
            bool isLinear, TextureFormat srcOriginalFormat = TextureFormat.RGBA32)
        {
            // 候補が enumerate した (width, height) をそのまま使う (targetMaxDim 経由の丸め差分を排除)
            var resized = ResolutionReducer.ResizeToSize(src, width, height, isLinear);
            try
            {
                var tex = new Texture2D(resized.width, resized.height, TextureFormat.RGBA32, true, isLinear);
                tex.name = $"{src.name}_{resized.width}x{resized.height}_{fmt}";
                tex.SetPixels(resized.GetPixels());
                tex.Apply(updateMipmaps: true);
                if (TextureEncodeDecode.NeedsDxt5nmToBC5Remap(srcOriginalFormat, fmt))
                    TextureEncodeDecode.RemapDxt5nmForBC5(tex);
                UnityEditor.EditorUtility.CompressTexture(tex, fmt,
                    UnityEditor.TextureCompressionQuality.Normal);
                TextureEncodeDecode.EnableStreamingMipmaps(tex);
                return tex;
            }
            finally
            {
                Object.DestroyImmediate(resized);
            }
        }
    }
}
