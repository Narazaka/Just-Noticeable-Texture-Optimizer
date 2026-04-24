# JNTO Perceptual Quality Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix 6 bugs/gaps in the JNTO texture optimizer's perceptual quality evaluation pipeline to improve optimization accuracy and coverage.

**Architecture:** Each fix is an independent, separately committable change. Tasks are ordered by impact: channel mapping bug fix first, then metric consistency, then pipeline behavior improvements, then coverage expansion. All changes preserve the existing IMetric interface.

**Tech Stack:** Unity 2022.3+, HLSL Compute Shaders, C# (NUnit tests), NDMF

---

### Task 1: NormalAngle DXT5nm Channel Mapping Fix

DXT5nm stores normal X in Alpha, Y in Green, but `NormalAngle.compute` reads from R,G. This causes incorrect evaluation for DXT5nm normal maps (the most common format in VRChat).

**Fix approach:** Add per-texture channel mapping flags to the compute shader and C# wrapper. The pipeline sets the correct mapping before each gate evaluation based on the texture format and whether remapping has occurred.

**Files:**
- Modify: `Editor/Phase2/GpuPipeline/Shaders/NormalAngle.compute`
- Modify: `Editor/Phase2/Gate/NormalAngleMetric.cs`
- Modify: `Editor/Phase2/Compression/NewPhase2Pipeline.cs`
- Modify: `Tests/Editor/NormalAngleMetricTests.cs`

- [ ] **Step 1: Add channel mapping to NormalAngle.compute**

```hlsl
// Add after line 16 (_NormalAngleScale)
int _OrigChannelMapping;  // 0 = standard RG, 1 = DXT5nm AG
int _CandChannelMapping;

// Replace decodeNormal function (lines 18-27)
float3 decodeNormalMapped(float4 c, int mapping)
{
    float nx = (mapping == 1) ? c.a * 2.0 - 1.0 : c.r * 2.0 - 1.0;
    float ny = c.g * 2.0 - 1.0;
    float nz2 = max(0.0, 1.0 - nx * nx - ny * ny);
    float nz = sqrt(nz2);
    float3 n = float3(nx, ny, nz);
    float len = length(n);
    return len > 1e-6 ? n / len : float3(0, 0, 1);
}
```

Update the sampling calls in CSEvaluate (around lines 78-79):
```hlsl
float3 nO = decodeNormalMapped(_Orig.SampleLevel(sampler_Orig, uv, origMip), _OrigChannelMapping);
float3 nC = decodeNormalMapped(_Candidate.SampleLevel(sampler_Candidate, uv, candMip), _CandChannelMapping);
```

- [ ] **Step 2: Add channel mapping fields to NormalAngleMetric.cs**

Replace the full file content:

```csharp
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.GpuPipeline;
using Narazaka.VRChat.Jnto.Editor.Phase2.Tiling;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Gate
{
    public class NormalAngleMetric : IMetric
    {
        public string Name => "NormalAngle";
        public MetricContext Context => MetricContext.Both;

        // 0 = standard RG, 1 = DXT5nm AG
        public int OrigChannelMapping;
        public int CandChannelMapping;

        public void Evaluate(
            RenderTexture orig, RenderTexture candidate,
            UvTileGrid grid, float[] rPerTile,
            DegradationCalibration calib, float[] scoresOut)
        {
            var cs = ComputeResources.Load("NormalAngle");
            int k = cs.FindKernel("CSEvaluate");

            int tileCount = grid.Tiles.Length;
            var scoreBuf = new ComputeBuffer(tileCount, sizeof(float));
            scoreBuf.SetData(new float[tileCount]);
            var rBuf = new ComputeBuffer(tileCount, sizeof(float));
            rBuf.SetData(rPerTile);

            try
            {
                cs.SetTexture(k, "_Orig", orig);
                cs.SetTexture(k, "_Candidate", candidate);
                cs.SetBuffer(k, "_Scores", scoreBuf);
                cs.SetBuffer(k, "_RPerTile", rBuf);
                cs.SetInt("_TilesX", grid.TilesX);
                cs.SetInt("_TilesY", grid.TilesY);
                cs.SetInt("_TileSize", grid.TileSize);
                cs.SetInt("_TextureWidth", grid.TextureWidth);
                cs.SetInt("_TextureHeight", grid.TextureHeight);
                cs.SetFloat("_NormalAngleScale", calib.NormalAngleScale);
                cs.SetInt("_OrigChannelMapping", OrigChannelMapping);
                cs.SetInt("_CandChannelMapping", CandChannelMapping);

                int gx = Mathf.CeilToInt(grid.TilesX / 8f);
                int gy = Mathf.CeilToInt(grid.TilesY / 8f);
                cs.Dispatch(k, gx, gy, 1);

                scoreBuf.GetData(scoresOut);
            }
            finally
            {
                scoreBuf.Release();
                rBuf.Release();
            }
        }
    }
}
```

- [ ] **Step 3: Add helper method and update BuildMetrics in NewPhase2Pipeline.cs**

Add after the `_isLinear` field declaration (around line 53):

```csharp
readonly TextureFormat _origFormat;
```

Update the constructor signature to accept `TextureFormat origFormat`:

```csharp
public NewPhase2Pipeline(DegradationCalibration calib, ShaderUsage usage, bool alphaUsed,
    bool enableChromaDrift = true, TextureFormat origFormat = TextureFormat.RGBA32)
{
    _calib = calib;
    _gate = new PerceptualGate(calib);
    _usage = usage;
    _alphaUsed = alphaUsed;
    _origFormat = origFormat;
    _downscaleMetrics = BuildMetrics(usage, alphaUsed, MetricContext.Downscale, false);
    _compressionMetrics = BuildMetrics(usage, alphaUsed, MetricContext.Compression, enableChromaDrift);
    _jointMetrics = BuildMetrics(usage, alphaUsed, MetricContext.Both, enableChromaDrift);
    _isLinear = usage == ShaderUsage.Normal || usage == ShaderUsage.SingleChannel;
}
```

Add a static helper:

```csharp
static bool IsDxt5nmFormat(TextureFormat fmt) =>
    fmt == TextureFormat.DXT5 || fmt == TextureFormat.DXT5Crunched;
```

Add a method to set channel mapping on NormalAngle metrics in an array:

```csharp
static void SetNormalChannelMappings(IMetric[] metrics, int origMapping, int candMapping)
{
    foreach (var m in metrics)
    {
        if (m is NormalAngleMetric n)
        {
            n.OrigChannelMapping = origMapping;
            n.CandChannelMapping = candMapping;
        }
    }
}
```

- [ ] **Step 4: Set channel mappings at each gate in Find()**

In the `Find` method, after `coveredCount` calculation (around line 117), add:

```csharp
bool origIsDxt5nm = _usage == ShaderUsage.Normal && IsDxt5nmFormat(_origFormat);
int dxt5nmMapping = origIsDxt5nm ? 1 : 0;
```

Before the downscale gate evaluation (before line 215), add:

```csharp
SetNormalChannelMappings(_downscaleMetrics, dxt5nmMapping, dxt5nmMapping);
```

Inside the compression gate section, after `needsRemap` is computed (around line 237), add:

```csharp
int compOrigMapping = needsRemap ? 0 : dxt5nmMapping;
int compCandMapping = needsRemap ? 0 : dxt5nmMapping;
SetNormalChannelMappings(_compressionMetrics, compOrigMapping, compCandMapping);
```

Before the joint gate evaluation (before line 295), add:

```csharp
bool jointCandRemapped = TextureEncodeDecode.NeedsDxt5nmToBC5Remap(orig.format, cand.Format);
SetNormalChannelMappings(_jointMetrics, dxt5nmMapping, jointCandRemapped ? 0 : dxt5nmMapping);
```

- [ ] **Step 5: Update pipeline construction in NewResolutionReducePass.cs**

In `ProcessTexture`, update the pipeline construction (around line 181):

```csharp
var pipeline = new NewPhase2Pipeline(calib, usage, alphaUsed, settings.EnableChromaDrift, tex.format);
```

- [ ] **Step 6: Add DXT5nm-layout test to NormalAngleMetricTests.cs**

Add at the end of the class:

```csharp
[Test]
public void Dxt5nmLayout_DetectsAngleDiff()
{
    // DXT5nm layout: normalX in Alpha, normalY in Green, R=1(dummy)
    var a = MakeDxt5nmNormal(128, nx: 0f, ny: 0f);   // flat (0,0,1)
    var b = MakeDxt5nmNormal(128, nx: 0.5f, ny: 0.5f); // tilted

    var grid = UvTileGrid.Create(128, 128);
    MarkAllCovered(grid);
    var r = FullR(grid);
    var calib = DegradationCalibration.Default();

    using (var ctxA = GpuTextureContext.FromTexture2D(a))
    using (var ctxB = GpuTextureContext.FromTexture2D(b))
    {
        var metric = new NormalAngleMetric { OrigChannelMapping = 1, CandChannelMapping = 1 };
        var scores = new float[grid.Tiles.Length];
        metric.Evaluate(ctxA.Original, ctxB.Original, grid, r, calib, scores);

        float maxScore = 0f;
        foreach (var s in scores) if (s > maxScore) maxScore = s;
        Assert.Greater(maxScore, 0.5f, "DXT5nm tilted normal vs flat should produce score via A,G channels");
    }

    Object.DestroyImmediate(a);
    Object.DestroyImmediate(b);
    Object.DestroyImmediate(calib);
}

[Test]
public void Dxt5nmLayout_WithStandardMapping_MissesXComponent()
{
    // Demonstrates the bug: if we read R,G from DXT5nm, R is always 1.0 (dummy)
    // so changing only the X component (in alpha) goes undetected
    var a = MakeDxt5nmNormal(128, nx: 0f, ny: 0f);
    var b = MakeDxt5nmNormal(128, nx: 0.7f, ny: 0f); // X changed, Y same

    var grid = UvTileGrid.Create(128, 128);
    MarkAllCovered(grid);
    var r = FullR(grid);
    var calib = DegradationCalibration.Default();

    using (var ctxA = GpuTextureContext.FromTexture2D(a))
    using (var ctxB = GpuTextureContext.FromTexture2D(b))
    {
        // Correct mapping: should detect the X difference
        var correctMetric = new NormalAngleMetric { OrigChannelMapping = 1, CandChannelMapping = 1 };
        var correctScores = new float[grid.Tiles.Length];
        correctMetric.Evaluate(ctxA.Original, ctxB.Original, grid, r, calib, correctScores);

        // Wrong mapping (old behavior): reads R which is always 1.0
        var wrongMetric = new NormalAngleMetric { OrigChannelMapping = 0, CandChannelMapping = 0 };
        var wrongScores = new float[grid.Tiles.Length];
        wrongMetric.Evaluate(ctxA.Original, ctxB.Original, grid, r, calib, wrongScores);

        float maxCorrect = 0f, maxWrong = 0f;
        for (int i = 0; i < correctScores.Length; i++)
        {
            if (correctScores[i] > maxCorrect) maxCorrect = correctScores[i];
            if (wrongScores[i] > maxWrong) maxWrong = wrongScores[i];
        }

        Assert.Greater(maxCorrect, 0.3f, "correct mapping should detect X-only change");
        Assert.Less(maxWrong, 0.05f, "wrong mapping misses X change (reads dummy R=1.0)");
    }

    Object.DestroyImmediate(a);
    Object.DestroyImmediate(b);
    Object.DestroyImmediate(calib);
}

static Texture2D MakeDxt5nmNormal(int n, float nx, float ny)
{
    // DXT5nm layout: R=1(dummy), G=encodeY, B=0(dummy), A=encodeX
    var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
    var px = new Color[n * n];
    float encX = nx * 0.5f + 0.5f;
    float encY = ny * 0.5f + 0.5f;
    for (int i = 0; i < px.Length; i++)
        px[i] = new Color(1f, encY, 0f, encX);
    t.SetPixels(px);
    t.Apply();
    return t;
}
```

- [ ] **Step 7: Compile and run tests**

Run: `./AIBridgeCache/CLI/AIBridgeCLI.exe compile unity`

Then run tests via Unity Test Runner for `NormalAngleMetricTests` and `BuildMetricsTests`.

- [ ] **Step 8: Commit**

```
git add Packages/net.narazaka.vrchat.jnto/Editor/Phase2/GpuPipeline/Shaders/NormalAngle.compute
git add Packages/net.narazaka.vrchat.jnto/Editor/Phase2/Gate/NormalAngleMetric.cs
git add Packages/net.narazaka.vrchat.jnto/Editor/Phase2/Compression/NewPhase2Pipeline.cs
git add Packages/net.narazaka.vrchat.jnto/Editor/Phase2/NewResolutionReducePass.cs
git add Packages/net.narazaka.vrchat.jnto/Tests/Editor/NormalAngleMetricTests.cs
git commit -m "fix: NormalAngle metric reads correct channels for DXT5nm normal maps"
```

---

### Task 2: Luminance Coefficient Unification

`Banding.compute` and `BlockBoundary.compute` use NTSC luma (0.299, 0.587, 0.114) while all other metrics use BT.709 (0.2126, 0.7152, 0.0722). Unify to BT.709.

**Files:**
- Modify: `Editor/Phase2/GpuPipeline/Shaders/Banding.compute:18`
- Modify: `Editor/Phase2/GpuPipeline/Shaders/BlockBoundary.compute:18`

- [ ] **Step 1: Update Banding.compute**

Change line 18 from:
```hlsl
float lum(float3 c) { return dot(c, float3(0.299, 0.587, 0.114)); }
```
to:
```hlsl
float lum(float3 c) { return dot(c, float3(0.2126, 0.7152, 0.0722)); }
```

- [ ] **Step 2: Update BlockBoundary.compute**

Change line 18 from:
```hlsl
float lum(float3 c) { return dot(c, float3(0.299, 0.587, 0.114)); }
```
to:
```hlsl
float lum(float3 c) { return dot(c, float3(0.2126, 0.7152, 0.0722)); }
```

- [ ] **Step 3: Compile and run tests**

Run: `./AIBridgeCache/CLI/AIBridgeCLI.exe compile unity`

Run tests for `BandingMetricTests` and `BlockBoundaryMetricTests`.

- [ ] **Step 4: Commit**

```
git add Packages/net.narazaka.vrchat.jnto/Editor/Phase2/GpuPipeline/Shaders/Banding.compute
git add Packages/net.narazaka.vrchat.jnto/Editor/Phase2/GpuPipeline/Shaders/BlockBoundary.compute
git commit -m "fix: unify luminance coefficients to BT.709 across all compute shaders"
```

---

### Task 3: SharedTexture Usage Aggregation

When a texture is referenced from multiple material properties (e.g., `_MainTex` and `_BumpMap`), only the first reference's usage is used. Fix by aggregating across all references and using the most restrictive (conservative) usage.

**Files:**
- Modify: `Editor/Phase2/NewResolutionReducePass.cs:80-98`
- Create: `Tests/Editor/ShaderUsageAggregationTests.cs`

- [ ] **Step 1: Refactor ProcessTexture usage inference**

In `NewResolutionReducePass.ProcessTexture`, replace lines 80-98 with:

```csharp
bool alphaUsed = false;
ShaderUsage aggregatedUsage = ShaderUsage.Color;
Material repMat = null; string repProp = null;
foreach (var r in refs)
{
    if (r.Material == null) continue;

    if (LilTexAlphaUsageAnalyzer.IsAlphaUsed(r.Material, r.PropertyName))
        alphaUsed = true;

    var refUsage = ShaderUsageInferrer.Infer(r.Material, r.PropertyName, tex);
    // Normal is most restrictive, then SingleChannel, then Color
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
```

- [ ] **Step 2: Compile and run tests**

Run: `./AIBridgeCache/CLI/AIBridgeCLI.exe compile unity`

Run tests for `NewPhase2PipelineTests`, `BuildMetricsTests`.

- [ ] **Step 3: Commit**

```
git add Packages/net.narazaka.vrchat.jnto/Editor/Phase2/NewResolutionReducePass.cs
git commit -m "fix: aggregate ShaderUsage across all texture references, use most restrictive"
```

---

### Task 4: Banding Flatness Threshold Relaxation

The banding detector's flatness threshold (3x3 neighborhood range < 0.02) is too strict. Gradients with slight noise/texture (range 0.02-0.04) aren't detected as flat, causing banding artifacts in those regions to go undetected.

**Files:**
- Modify: `Editor/Phase2/GpuPipeline/Shaders/Banding.compute:62`
- Modify: `Tests/Editor/BandingMetricTests.cs`

- [ ] **Step 1: Relax flatness threshold**

In `Banding.compute`, change line 62 from:
```hlsl
if (mx - mn > 0.02) continue;
```
to:
```hlsl
if (mx - mn > 0.04) continue;
```

- [ ] **Step 2: Add test for noisy gradient banding detection**

Add to `BandingMetricTests.cs`:

```csharp
[Test]
public void NoisyGradient_VsQuantized_StillDetectsBanding()
{
    // Original gradient with slight noise (range ~0.03 per 3x3 neighborhood)
    // Previous threshold 0.02 would miss this; relaxed threshold 0.04 catches it
    var noisy = MakeNoisyGradient(128, noiseAmplitude: 0.012f);
    var banded = MakeGradient(128, quantize: 8);

    var grid = UvTileGrid.Create(128, 128);
    MarkAllCovered(grid);
    var r = FullR(grid);
    var calib = DegradationCalibration.Default();

    using (var ctxA = GpuTextureContext.FromTexture2D(noisy))
    using (var ctxB = GpuTextureContext.FromTexture2D(banded))
    {
        var metric = new BandingMetric();
        var scores = new float[grid.Tiles.Length];
        metric.Evaluate(ctxA.Original, ctxB.Original, grid, r, calib, scores);

        float maxScore = 0f;
        foreach (var s in scores) if (s > maxScore) maxScore = s;
        Assert.Greater(maxScore, 0.05f,
            "banding should be detected even with slightly noisy original gradient");
    }

    Object.DestroyImmediate(noisy);
    Object.DestroyImmediate(banded);
    Object.DestroyImmediate(calib);
}

static Texture2D MakeNoisyGradient(int n, float noiseAmplitude)
{
    var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
    var px = new Color[n * n];
    // Deterministic noise via simple hash
    for (int y = 0; y < n; y++)
    for (int x = 0; x < n; x++)
    {
        float v = x / (float)(n - 1);
        int hash = (x * 73856093) ^ (y * 19349663);
        float noise = ((hash & 0xFFFF) / 65535f - 0.5f) * 2f * noiseAmplitude;
        v = Mathf.Clamp01(v + noise);
        px[y * n + x] = new Color(v, v, v, 1f);
    }
    t.SetPixels(px);
    t.Apply();
    return t;
}
```

- [ ] **Step 3: Compile and run tests**

Run: `./AIBridgeCache/CLI/AIBridgeCLI.exe compile unity`

Run tests for `BandingMetricTests`.

- [ ] **Step 4: Commit**

```
git add Packages/net.narazaka.vrchat.jnto/Editor/Phase2/GpuPipeline/Shaders/Banding.compute
git add Packages/net.narazaka.vrchat.jnto/Tests/Editor/BandingMetricTests.cs
git commit -m "fix: relax banding flatness threshold from 0.02 to 0.04 for noisy gradients"
```

---

### Task 5: Compression Gate Viewing Distance

The compression gate currently evaluates at pixel level (`compRPerTile = tileSize`) regardless of viewing distance. This over-rejects format changes for distant textures. Use the actual effective resolution with a floor at half tile size.

**Files:**
- Modify: `Editor/Phase2/Compression/NewPhase2Pipeline.cs:250-253`

- [ ] **Step 1: Update compression gate r(T) calculation**

In `NewPhase2Pipeline.Find`, replace lines 250-253:

```csharp
var compGrid = RemapGrid(grid, rPerTile, cand.Width, cand.Height,
    settings, out var compRPerTile);
for (int ti = 0; ti < compRPerTile.Length; ti++)
    if (compGrid.Tiles[ti].HasCoverage) compRPerTile[ti] = compGrid.TileSize;
```

with:

```csharp
var compGrid = RemapGrid(grid, rPerTile, cand.Width, cand.Height,
    settings, out var compRPerTile);
float compRFloor = compGrid.TileSize * 0.5f;
for (int ti = 0; ti < compRPerTile.Length; ti++)
    if (compGrid.Tiles[ti].HasCoverage)
        compRPerTile[ti] = Mathf.Max(compRFloor, compRPerTile[ti]);
```

- [ ] **Step 2: Compile and run tests**

Run: `./AIBridgeCache/CLI/AIBridgeCLI.exe compile unity`

Run tests for `NewPhase2PipelineTests`, `NewPhase2PipelineE2ETests`.

- [ ] **Step 3: Commit**

```
git add Packages/net.narazaka.vrchat.jnto/Editor/Phase2/Compression/NewPhase2Pipeline.cs
git commit -m "fix: compression gate respects viewing distance with tileSize*0.5 floor"
```

---

### Task 6: UV Tiling/Repeat Support

Triangles with UV coordinates outside [0,1] (tiling/repeat textures) are silently clipped, causing their tiles to have no coverage. Fix by wrapping tile indices modulo grid dimensions.

**Files:**
- Modify: `Editor/Phase2/Tiling/TileRasterizer.cs:71-91`
- Modify: `Tests/Editor/TileRasterizerTests.cs`

- [ ] **Step 1: Update Rasterize to wrap tile indices**

In `TileRasterizer.cs`, replace the `Rasterize` method (lines 71-91):

```csharp
static void Rasterize(UvTileGrid g, Vector2 uv0, Vector2 uv1, Vector2 uv2, float density, float bw)
{
    float tw = g.TilesX, th = g.TilesY;
    float x0 = uv0.x * tw, y0 = uv0.y * th;
    float x1 = uv1.x * tw, y1 = uv1.y * th;
    float x2 = uv2.x * tw, y2 = uv2.y * th;

    int minX = Mathf.FloorToInt(Mathf.Min(x0, Mathf.Min(x1, x2)));
    int maxX = Mathf.FloorToInt(Mathf.Max(x0, Mathf.Max(x1, x2)));
    int minY = Mathf.FloorToInt(Mathf.Min(y0, Mathf.Min(y1, y2)));
    int maxY = Mathf.FloorToInt(Mathf.Max(y0, Mathf.Max(y1, y2)));

    // Skip degenerate or excessively large UV spans (> 2 periods)
    if (maxX - minX > g.TilesX * 2 || maxY - minY > g.TilesY * 2) return;

    for (int ty = minY; ty <= maxY; ty++)
    for (int tx = minX; tx <= maxX; tx++)
    {
        if (!TriangleIntersectsTile(x0, y0, x1, y1, x2, y2, tx, ty)) continue;
        int wtx = ((tx % g.TilesX) + g.TilesX) % g.TilesX;
        int wty = ((ty % g.TilesY) + g.TilesY) % g.TilesY;
        ref var tile = ref g.GetTile(wtx, wty);
        tile.HasCoverage = true;
        if (density > tile.Density) tile.Density = density;
        if (bw > tile.BoneWeight) tile.BoneWeight = bw;
    }
}
```

- [ ] **Step 2: Add UV wrapping tests to TileRasterizerTests.cs**

Add at the end of the class:

```csharp
[Test]
public void TilingUvs_WrappedCoverage()
{
    // UVs entirely in [1,2] range (one period offset)
    var go = new GameObject("r");
    var mf = go.AddComponent<MeshFilter>();
    var mr = go.AddComponent<MeshRenderer>();
    var mesh = new Mesh
    {
        vertices = new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0) },
        uv = new[] { new Vector2(1.0f, 1.0f), new Vector2(2.0f, 1.0f), new Vector2(1.0f, 2.0f) },
        triangles = new[] { 0, 1, 2 },
    };
    mesh.RecalculateBounds();
    mf.sharedMesh = mesh;

    var grid = UvTileGrid.Create(128, 128);
    TileRasterizer.Accumulate(grid, mr, mesh, null, null);

    int covered = 0;
    foreach (var t in grid.Tiles) if (t.HasCoverage) covered++;
    Assert.Greater(covered, 0,
        "tiling UVs in [1,2] should wrap and produce coverage in [0,1] tiles");

    Object.DestroyImmediate(go);
    Object.DestroyImmediate(mesh);
}

[Test]
public void NegativeUvs_WrappedCoverage()
{
    // UVs in [-1, 0] range
    var go = new GameObject("r");
    var mf = go.AddComponent<MeshFilter>();
    var mr = go.AddComponent<MeshRenderer>();
    var mesh = new Mesh
    {
        vertices = new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0) },
        uv = new[] { new Vector2(-1f, -1f), new Vector2(0f, -1f), new Vector2(-1f, 0f) },
        triangles = new[] { 0, 1, 2 },
    };
    mesh.RecalculateBounds();
    mf.sharedMesh = mesh;

    var grid = UvTileGrid.Create(64, 64);
    TileRasterizer.Accumulate(grid, mr, mesh, null, null);

    int covered = 0;
    foreach (var t in grid.Tiles) if (t.HasCoverage) covered++;
    Assert.Greater(covered, 0,
        "negative UVs should wrap and produce coverage");

    Object.DestroyImmediate(go);
    Object.DestroyImmediate(mesh);
}
```

- [ ] **Step 3: Compile and run tests**

Run: `./AIBridgeCache/CLI/AIBridgeCLI.exe compile unity`

Run tests for `TileRasterizerTests`.

- [ ] **Step 4: Commit**

```
git add Packages/net.narazaka.vrchat.jnto/Editor/Phase2/Tiling/TileRasterizer.cs
git add Packages/net.narazaka.vrchat.jnto/Tests/Editor/TileRasterizerTests.cs
git commit -m "fix: wrap UV coordinates for tiling/repeat texture support"
```
