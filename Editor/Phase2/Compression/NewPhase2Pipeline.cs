using System.Collections.Generic;
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
        public int Size;
        public TextureFormat Format;
        public GateVerdict FinalVerdict;
        public string DecisionReason;
        public bool CacheHit;
        public float ProcessingMs;
    }

    /// <summary>
    /// 新パイプライン: PerceptualGate + BinarySearch + FormatPredictor を統合。
    /// 旧 Phase2Pipeline と並置し、JntoPlugin の差替えで切替える (M7.2)。
    /// </summary>
    public class NewPhase2Pipeline
    {
        readonly DegradationCalibration _calib;
        readonly PerceptualGate _gate;
        readonly IMetric[] _downscaleMetrics;
        readonly IMetric[] _compressionMetrics;
        readonly bool _isLinear;

        public NewPhase2Pipeline(DegradationCalibration calib, TextureRole role)
        {
            _calib = calib;
            _gate = new PerceptualGate(calib);
            _downscaleMetrics = BuildMetrics(role, MetricContext.Downscale);
            _compressionMetrics = BuildMetrics(role, MetricContext.Compression);
            // バグ#2 回帰防止: NormalMap / SingleChannel は linear color space で扱う。
            _isLinear = role == TextureRole.NormalMap || role == TextureRole.SingleChannel;
        }

        static IMetric[] BuildMetrics(TextureRole role, MetricContext ctx)
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
                if (role == TextureRole.ColorAlpha) list.Add(new AlphaQuantizationMetric());
            }
            if (role == TextureRole.NormalMap) list.Add(new NormalAngleMetric());
            return list.ToArray();
        }

        public NewPhase2Result Find(
            Texture2D orig, GpuTextureContext origCtx,
            UvTileGrid grid, float[] rPerTile,
            TextureRole role, ResolvedSettings settings,
            BlockStats[] origStats)
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
                    Format = orig.format,
                    FinalVerdict = new GateVerdict { Pass = false, TextureScore = 0f, WorstTileIndex = -1, DominantMetric = null, DominantMipLevel = -1 },
                    DecisionReason = "skipped: no tile coverage (cannot evaluate)",
                    ProcessingMs = (float)sw.Elapsed.TotalMilliseconds,
                };
            }

            int minSize = DensityCalculator.MinSize;

            int finalSize;
            Profiler.BeginSample("JNTO.BinarySearch");
            try
            {
                finalSize = BinarySearchStrategy.FindMinPassSize(origSize, minSize, size =>
                {
                    if (size >= origSize) return true;
                    var candidateRt = PyramidBuilder.CreatePyramid(origCtx.Original, size, size, $"Jnto_Cand_{size}", _isLinear);
                    try
                    {
                        var v = _gate.Evaluate(origCtx.Original, candidateRt, grid, rPerTile,
                            settings.Preset, _downscaleMetrics);
                        return v.Pass;
                    }
                    finally
                    {
                        candidateRt.Release();
                        Object.DestroyImmediate(candidateRt);
                    }
                });
            }
            finally
            {
                Profiler.EndSample();
            }

            // Debug dump (final size 確定後の downscale verdict のみ)
            if (!string.IsNullOrEmpty(settings.DebugDumpPath))
            {
                Profiler.BeginSample("JNTO.DebugDump");
                try
                {
                    int dumpSize = finalSize;
                    var dumpRt = PyramidBuilder.CreatePyramid(origCtx.Original, dumpSize, dumpSize, "Jnto_Final_Dbg", _isLinear);
                    try
                    {
                        var debugVerdict = _gate.EvaluateDebug(
                            origCtx.Original, dumpRt, grid, rPerTile,
                            settings.Preset, _downscaleMetrics,
                            out var perMetric, out var names);
                        Reporting.DebugDump.DumpTileScores(
                            settings.DebugDumpPath, orig.name, grid, perMetric, names);
                        for (int i = 0; i < names.Length; i++)
                        {
                            Reporting.DebugDump.DumpHeatmapPng(
                                settings.DebugDumpPath, orig.name, names[i], grid, perMetric[i]);
                        }
                    }
                    finally
                    {
                        dumpRt.Release();
                        Object.DestroyImmediate(dumpRt);
                    }
                }
                finally
                {
                    Profiler.EndSample();
                }
            }

            var lightweight = FormatPredictor.PredictLightweight(origStats, role, settings.Preset);
            TextureFormat finalFmt;
            GateVerdict finalVerdict;
            string reason;
            Profiler.BeginSample("JNTO.ChooseFormat");
            try
            {
                finalFmt = ChooseFormat(orig, origCtx, grid, rPerTile, settings, role,
                                        finalSize, lightweight,
                                        out finalVerdict, out reason);
            }
            finally
            {
                Profiler.EndSample();
            }

            Texture2D final;
            Profiler.BeginSample("JNTO.FinalEncode");
            try
            {
                final = Encode(orig, finalSize, finalFmt);
            }
            finally
            {
                Profiler.EndSample();
            }
            sw.Stop();
            return new NewPhase2Result
            {
                Final = final,
                Size = finalSize,
                Format = finalFmt,
                FinalVerdict = finalVerdict,
                DecisionReason = reason,
                ProcessingMs = (float)sw.Elapsed.TotalMilliseconds,
            };
        }

        TextureFormat ChooseFormat(
            Texture2D orig, GpuTextureContext origCtx,
            UvTileGrid grid, float[] rPerTile,
            ResolvedSettings settings, TextureRole role,
            int size, FormatPrediction lightweight,
            out GateVerdict verdict, out string reason)
        {
            bool skipVerify = settings.EncodePolicy == EncodePolicy.Fast && lightweight.Confidence >= 0.95f;
            if (skipVerify)
            {
                verdict = new GateVerdict { Pass = true, TextureScore = 0f };
                reason = $"Fast-mode skip verify for {lightweight.Format} (conf={lightweight.Confidence:F2})";
                return lightweight.Format;
            }

            var downsampled = ResolutionReducer.Resize(orig, size);
            try
            {
                Texture2D candidate;
                Profiler.BeginSample("JNTO.Encode." + lightweight.Format);
                try
                {
                    candidate = TextureEncodeDecode.EncodeAndDecode(downsampled, lightweight.Format);
                }
                finally
                {
                    Profiler.EndSample();
                }
                using (var candCtx = GpuTextureContext.FromTexture2D(candidate, _isLinear))
                using (var origDownCtx = GpuTextureContext.FromTexture2D(downsampled, _isLinear))
                {
                    verdict = _gate.Evaluate(origDownCtx.Original, candCtx.Original,
                        grid, rPerTile, settings.Preset, _compressionMetrics);
                }
                Object.DestroyImmediate(candidate);

                if (verdict.Pass)
                {
                    reason = $"lightweight {lightweight.Format} verify PASS (score={verdict.TextureScore:F3})";
                    return lightweight.Format;
                }

                var fallback = BC7Fallback(role);
                Texture2D bc7Candidate;
                Profiler.BeginSample("JNTO.Encode." + fallback);
                try
                {
                    bc7Candidate = TextureEncodeDecode.EncodeAndDecode(downsampled, fallback);
                }
                finally
                {
                    Profiler.EndSample();
                }
                using (var bc7Ctx = GpuTextureContext.FromTexture2D(bc7Candidate, _isLinear))
                using (var origDownCtx = GpuTextureContext.FromTexture2D(downsampled, _isLinear))
                {
                    verdict = _gate.Evaluate(origDownCtx.Original, bc7Ctx.Original,
                        grid, rPerTile, settings.Preset, _compressionMetrics);
                }
                Object.DestroyImmediate(bc7Candidate);

                reason = verdict.Pass
                    ? $"{fallback} fallback PASS (score={verdict.TextureScore:F3})"
                    : $"{fallback} fallback FAIL, keeping original";
                return verdict.Pass ? fallback : orig.format;
            }
            finally
            {
                Object.DestroyImmediate(downsampled);
            }
        }

        static TextureFormat BC7Fallback(TextureRole role)
        {
            switch (role)
            {
                case TextureRole.SingleChannel: return TextureFormat.R8;
                default: return TextureFormat.BC7;
            }
        }

        static Texture2D Encode(Texture2D src, int size, TextureFormat fmt)
        {
            var resized = ResolutionReducer.Resize(src, size);
            try
            {
                var tex = new Texture2D(resized.width, resized.height, TextureFormat.RGBA32, true);
                tex.name = $"{src.name}_{resized.width}x{resized.height}_{fmt}";
                tex.SetPixels(resized.GetPixels());
                // バグ#12 回帰防止: CompressTexture の前に mipchain を必ず更新する。
                // Apply() は既定で updateMipmaps=true だが、意図を明示するため引数を与える。
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
