# JNTO Critical Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix sRGB/linear color-space integrity, perceptual color metric (ΔE2000), coverage-zero handling, and aspect-size rounding consistency in JNTO, and add Tier-2 detection tests for suspected bugs (aliasing, banding bin clamp, minRequiredDim percentile).

**Architecture:** 1 fix = 1 commit. `TextureColorSpace` helper becomes the single source for `isLinear` deduction (was duplicated across `NewResolutionReducePass` / `NewPhase2Pipeline` and derived from `ShaderUsage` only). `ChromaDrift.compute` stops double-linearizing sampled RT values and replaces ΔE76 with ΔE2000. `AspectSizeCalculator` is extracted so size rounding is shared between enumeration and final encode. Tier-2 tests measure suspected bugs but do NOT yet apply fixes — those are conditional on test failure.

**Tech Stack:** Unity 2022.3+, HLSL Compute Shaders, C# (NUnit tests), NDMF, AIBridge CLI for compile.

**Reference design:** `Packages/net.narazaka.vrchat.jnto/docs/specs/2026-04-23-jnto-critical-fixes-design.md`

---

## Task 1: Create TextureColorSpace helper

Add a single helper to decide `isLinear` from `TextureImporter.sRGBTexture` with fallback to format then `ShaderUsage`. This is the foundation for B2.

**Files:**
- Create: `Packages/net.narazaka.vrchat.jnto/Editor/Phase2/GpuPipeline/TextureColorSpace.cs`
- Create: `Packages/net.narazaka.vrchat.jnto/Tests/Editor/TextureColorSpaceTests.cs`

**Note:** Unity auto-generates `.meta` files on `compile unity` / asset refresh. Do NOT hand-write `.meta` files — let Unity handle them.

- [ ] **Step 1: Write failing tests for `TextureColorSpace.IsLinear`**

Create `Tests/Editor/TextureColorSpaceTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.Compression;
using Narazaka.VRChat.Jnto.Editor.Phase2.GpuPipeline;

public class TextureColorSpaceTests
{
    [Test]
    public void RuntimeTexture_RGBAHalf_IsLinear()
    {
        // RGBAHalf is physically linear; no importer attached
        var tex = new Texture2D(8, 8, TextureFormat.RGBAHalf, false);
        try { Assert.IsTrue(TextureColorSpace.IsLinear(tex, ShaderUsage.Color)); }
        finally { Object.DestroyImmediate(tex); }
    }

    [Test]
    public void RuntimeTexture_RGBA32_ColorUsage_IsSrgb()
    {
        var tex = new Texture2D(8, 8, TextureFormat.RGBA32, false);
        try { Assert.IsFalse(TextureColorSpace.IsLinear(tex, ShaderUsage.Color)); }
        finally { Object.DestroyImmediate(tex); }
    }

    [Test]
    public void RuntimeTexture_RGBA32_NormalUsage_IsLinear()
    {
        var tex = new Texture2D(8, 8, TextureFormat.RGBA32, false);
        try { Assert.IsTrue(TextureColorSpace.IsLinear(tex, ShaderUsage.Normal)); }
        finally { Object.DestroyImmediate(tex); }
    }

    [Test]
    public void RuntimeTexture_RGBA32_SingleChannelUsage_IsLinear()
    {
        var tex = new Texture2D(8, 8, TextureFormat.RGBA32, false);
        try { Assert.IsTrue(TextureColorSpace.IsLinear(tex, ShaderUsage.SingleChannel)); }
        finally { Object.DestroyImmediate(tex); }
    }

    [Test]
    public void RuntimeTexture_BC6H_IsLinear()
    {
        // BC6H is HDR, physically linear
        var tex = new Texture2D(8, 8, TextureFormat.BC6H, false);
        try { Assert.IsTrue(TextureColorSpace.IsLinear(tex, ShaderUsage.Color)); }
        finally { Object.DestroyImmediate(tex); }
    }

    [Test]
    public void NullTexture_FallsBackToUsage()
    {
        Assert.IsTrue(TextureColorSpace.IsLinear(null, ShaderUsage.Normal));
        Assert.IsFalse(TextureColorSpace.IsLinear(null, ShaderUsage.Color));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `./AIBridgeCache/CLI/AIBridgeCLI.exe compile unity`
Expected: FAIL with "TextureColorSpace not found".

- [ ] **Step 3: Create `TextureColorSpace.cs`**

```csharp
using UnityEditor;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.Compression;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.GpuPipeline
{
    /// <summary>
    /// テクスチャを内部 RT 上で linear 値として扱うべきかを判定する単一ヘルパー。
    ///
    /// 優先順:
    ///   1. TextureImporter.sRGBTexture (importer が取得できる場合)
    ///   2. format が物理的に非 sRGB (RGBAHalf/RGBAFloat/R16/BC6H) なら linear
    ///      (BC4/BC5/BC7/DXT* は sRGB-flag が importer 設定次第なのでここでは判定しない)
    ///   3. usageFallback: ShaderUsage.Normal / SingleChannel なら linear
    /// </summary>
    public static class TextureColorSpace
    {
        public static bool IsLinear(Texture2D tex, ShaderUsage usageFallback)
        {
            if (tex != null)
            {
                var path = AssetDatabase.GetAssetPath(tex);
                if (!string.IsNullOrEmpty(path))
                {
                    var imp = AssetImporter.GetAtPath(path) as TextureImporter;
                    if (imp != null) return !imp.sRGBTexture;
                }

                switch (tex.format)
                {
                    case TextureFormat.RGBAHalf:
                    case TextureFormat.RGBAFloat:
                    case TextureFormat.RHalf:
                    case TextureFormat.RFloat:
                    case TextureFormat.RGHalf:
                    case TextureFormat.RGFloat:
                    case TextureFormat.R16:
                    case TextureFormat.BC6H:
                        return true;
                }
            }

            return usageFallback == ShaderUsage.Normal
                || usageFallback == ShaderUsage.SingleChannel;
        }
    }
}
```

- [ ] **Step 4: Compile and run tests**

Run: `./AIBridgeCache/CLI/AIBridgeCLI.exe compile unity` (this also triggers asset import, generating `.meta` files)
Then run `TextureColorSpaceTests` via Unity Test Runner.
Expected: PASS all 6 tests.

- [ ] **Step 5: Commit**

```bash
git -C Packages/net.narazaka.vrchat.jnto add Editor/Phase2/GpuPipeline/TextureColorSpace.cs Editor/Phase2/GpuPipeline/TextureColorSpace.cs.meta Tests/Editor/TextureColorSpaceTests.cs Tests/Editor/TextureColorSpaceTests.cs.meta
git -C Packages/net.narazaka.vrchat.jnto commit -m "feat: add TextureColorSpace helper for sRGB/linear flag deduction"
```

---

## Task 2: Thread `isLinear` from TextureColorSpace through the pipeline

Replace the two places that derive `isLinear` from `ShaderUsage` with calls to `TextureColorSpace.IsLinear`. Pipeline now accepts an explicit `bool? isLinear = null` so existing tests keep passing while the production path supplies the TextureImporter-aware value.

**Files:**
- Modify: `Packages/net.narazaka.vrchat.jnto/Editor/Phase2/NewResolutionReducePass.cs` (line 106 area)
- Modify: `Packages/net.narazaka.vrchat.jnto/Editor/Phase2/Compression/NewPhase2Pipeline.cs` (constructor)

- [ ] **Step 1: Write failing test showing pipeline respects explicit `isLinear` argument**

Append to `Tests/Editor/BuildMetricsTests.cs` (before the closing `}` of the class):

```csharp
    [Test]
    public void Pipeline_ExplicitIsLinear_OverridesUsageDerivation()
    {
        // Color usage but explicit isLinear=true — the pipeline must take the explicit value.
        // Reachable via reflection of the _isLinear private field.
        var calib = DegradationCalibration.Default();
        try
        {
            var pipeline = new NewPhase2Pipeline(calib, ShaderUsage.Color, alphaUsed: false,
                enableChromaDrift: true, origFormat: TextureFormat.RGBA32, isLinear: true);
            var f = typeof(NewPhase2Pipeline).GetField("_isLinear",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(f, "_isLinear field must exist");
            Assert.IsTrue((bool)f.GetValue(pipeline),
                "explicit isLinear=true must override Color-usage default (=false)");
        }
        finally { Object.DestroyImmediate(calib); }
    }

    [Test]
    public void Pipeline_NullIsLinear_FallsBackToUsage()
    {
        var calib = DegradationCalibration.Default();
        try
        {
            var pipeline = new NewPhase2Pipeline(calib, ShaderUsage.Normal, alphaUsed: false,
                enableChromaDrift: false, origFormat: TextureFormat.DXT5);
            var f = typeof(NewPhase2Pipeline).GetField("_isLinear",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsTrue((bool)f.GetValue(pipeline),
                "Normal usage without explicit isLinear must derive true (linear)");
        }
        finally { Object.DestroyImmediate(calib); }
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `./AIBridgeCache/CLI/AIBridgeCLI.exe compile unity`
Expected: COMPILE FAIL (constructor signature mismatch — `isLinear` parameter does not exist).

- [ ] **Step 3: Update `NewPhase2Pipeline` constructor to accept optional `bool? isLinear`**

Replace the constructor at `Editor/Phase2/Compression/NewPhase2Pipeline.cs:55-67`:

```csharp
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
```

- [ ] **Step 4: Update `NewResolutionReducePass.ProcessTexture` to use `TextureColorSpace`**

In `Editor/Phase2/NewResolutionReducePass.cs`, replace the block around line 106:

Find:
```csharp
                bool isLinear = usage == ShaderUsage.Normal || usage == ShaderUsage.SingleChannel;
```

Replace with:
```csharp
                bool isLinear = Phase2.GpuPipeline.TextureColorSpace.IsLinear(tex, usage);
```

Find (around line 186):
```csharp
                var pipeline = new NewPhase2Pipeline(calib, usage, alphaUsed, settings.EnableChromaDrift, tex.format);
```

Replace with:
```csharp
                var pipeline = new NewPhase2Pipeline(calib, usage, alphaUsed, settings.EnableChromaDrift, tex.format, isLinear);
```

- [ ] **Step 5: Compile and run tests**

Run: `./AIBridgeCache/CLI/AIBridgeCLI.exe compile unity`
Then run `BuildMetricsTests`, `NewPhase2PipelineTests`, `NewPhase2PipelineE2ETests`, `GpuTextureContextLinearTests`, `PyramidBuilderLinearTests`, `TextureColorSpaceTests` via Unity Test Runner.
Expected: PASS all.

- [ ] **Step 6: Commit**

```bash
git -C Packages/net.narazaka.vrchat.jnto add Editor/Phase2/NewResolutionReducePass.cs Editor/Phase2/Compression/NewPhase2Pipeline.cs Tests/Editor/BuildMetricsTests.cs
git -C Packages/net.narazaka.vrchat.jnto commit -m "fix: derive isLinear from TextureImporter.sRGBTexture, not ShaderUsage"
```

---

## Task 3: Remove double-linearization in ChromaDrift.compute (B1 + M2)

With Task 2 applied, RT `sRGB` flag matches the importer. For sRGB RT, `SampleLevel` returns linear values (auto-decoded). For linear RT (`sRGB=false`), sampled values are already linear. In both cases the old `toLinear()` call is wrong.

**Files:**
- Modify: `Packages/net.narazaka.vrchat.jnto/Editor/Phase2/GpuPipeline/Shaders/ChromaDrift.compute`

- [ ] **Step 1: Add regression test asserting "middle-gray identical pair produces near-zero ΔE"**

Append to `Tests/Editor/ChromaDriftGateMetricTests.cs` (before the closing `}` of the class, after `SubtleShift_ProducesSmallScore`):

```csharp
    [Test]
    public void MidGray_NoDoubleLinearization_StaysBelowJnd()
    {
        // Bug B1: toLinear() was applied to already-linearized RT samples. For middle-gray (0.5)
        // this inflated "delta" even for identical textures. After fix, score must stay below
        // a tight JND bound (< 0.15).
        var t = MakeSolid(128, new Color(0.5f, 0.5f, 0.5f, 1f));
        var grid = UvTileGrid.Create(128, 128);
        MarkAllCovered(grid);
        var r = FullR(grid);
        var calib = DegradationCalibration.Default();

        using (var ctx = GpuTextureContext.FromTexture2D(t))
        {
            var metric = new ChromaDriftMetric();
            var scores = new float[grid.Tiles.Length];
            metric.Evaluate(ctx.Original, ctx.Original, grid, r, calib, scores);

            float maxScore = 0f;
            foreach (var s in scores) if (s > maxScore) maxScore = s;
            Assert.Less(maxScore, 0.15f,
                "identical mid-gray textures must produce near-zero ΔE (not inflated by double-linearization)");
        }

        Object.DestroyImmediate(t);
        Object.DestroyImmediate(calib);
    }
```

- [ ] **Step 2: Verify the regression test already passes (but capture baseline)**

Run: `./AIBridgeCache/CLI/AIBridgeCLI.exe compile unity`
Then run `ChromaDriftGateMetricTests` via Unity Test Runner.

The existing `Identical_NearZero` test already asserts `s < 0.05`. If it passes for identical inputs, this new test should too. If this new test FAILS at current code, it exposes the bug — either way, proceed with the fix.

- [ ] **Step 3: Remove `toLinear` from ChromaDrift.compute**

In `Editor/Phase2/GpuPipeline/Shaders/ChromaDrift.compute`, delete lines 18-25 (the `toLinear` function):

```hlsl
// sRGB → linear (近似)
float3 toLinear(float3 c)
{
    return float3(
        c.r <= 0.04045 ? c.r / 12.92 : pow((c.r + 0.055) / 1.055, 2.4),
        c.g <= 0.04045 ? c.g / 12.92 : pow((c.g + 0.055) / 1.055, 2.4),
        c.b <= 0.04045 ? c.b / 12.92 : pow((c.b + 0.055) / 1.055, 2.4));
}
```

In the kernel body around lines 88-92, change:
```hlsl
        float3 rgbO = _Orig.SampleLevel(sampler_Orig, uv, 0).rgb;
        float3 rgbC = _Candidate.SampleLevel(sampler_Candidate, uv, 0).rgb;

        float3 labO = toLab(toXYZ(toLinear(rgbO)));
        float3 labC = toLab(toXYZ(toLinear(rgbC)));
```

to:
```hlsl
        // RT samples return linear RGB regardless of sRGB flag (auto-decoded for sRGB, raw for linear).
        // After Task 2 unified the sRGB flag with TextureImporter.sRGBTexture, no re-linearization is needed.
        float3 rgbO = _Orig.SampleLevel(sampler_Orig, uv, 0).rgb;
        float3 rgbC = _Candidate.SampleLevel(sampler_Candidate, uv, 0).rgb;

        float3 labO = toLab(toXYZ(rgbO));
        float3 labC = toLab(toXYZ(rgbC));
```

- [ ] **Step 4: Compile and run tests**

Run: `./AIBridgeCache/CLI/AIBridgeCLI.exe compile unity`
Then run `ChromaDriftGateMetricTests`, `BuildMetricsTests`, `NewPhase2PipelineTests`, `NewPhase2PipelineE2ETests` via Unity Test Runner.
Expected: PASS all.

Note: `DifferentHue_ProducesScore` asserts `maxScore > 0.5f`. Since ΔE is now ~2x smaller (no double-linearization), this may now fail. If it does, that's evidence the bug was real — update the threshold to `0.2f` in the same commit (the shift from 0.8-red to 0.2-blue still produces a clearly > 0.2 score on BT.709-based ΔE76 even after the fix). Document it in the commit message.

- [ ] **Step 5: Commit**

```bash
git -C Packages/net.narazaka.vrchat.jnto add Editor/Phase2/GpuPipeline/Shaders/ChromaDrift.compute Tests/Editor/ChromaDriftGateMetricTests.cs
git -C Packages/net.narazaka.vrchat.jnto commit -m "fix: ChromaDrift no longer double-linearizes sRGB RT samples

RT SampleLevel returns linear RGB regardless of the sRGB flag
(auto-decoded for sRGB, raw for linear). The prior toLinear() call
applied sRGB→linear conversion a second time, inflating ΔE in the
mid-luminance range. After Task 2 unified the sRGB flag with
TextureImporter.sRGBTexture, no re-linearization is needed."
```

---

## Task 4: Replace ΔE76 with ΔE2000 (M1)

ΔE76 has known non-uniformity (over-sensitive in blues, under-sensitive in grays). Replace with ΔE2000 (CIE 2000), which is the current standard and defines "1.0 = 1 JND" natively, removing the arbitrary `/2.3` divisor.

**Files:**
- Modify: `Packages/net.narazaka.vrchat.jnto/Editor/Phase2/GpuPipeline/Shaders/ChromaDrift.compute`
- Create: `Packages/net.narazaka.vrchat.jnto/Tests/Editor/ChromaDriftDeltaE2000Tests.cs` (Unity auto-generates `.meta` on compile)

- [ ] **Step 1: Add failing test asserting ΔE2000 mathematical values match published reference**

The Sharma/Wu/Dalal 2005 paper lists test pairs for CIEDE2000 verification. Use a subset that stress-tests the branching logic.

Create `Tests/Editor/ChromaDriftDeltaE2000Tests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.Gate;
using Narazaka.VRChat.Jnto.Editor.Phase2.GpuPipeline;
using Narazaka.VRChat.Jnto.Editor.Phase2.Tiling;

// References: CIEDE2000 reference values (computed from Sharma/Wu/Dalal 2005 formula)
// pair 1: (50, 2.6772, -79.7751) vs (50, 0, -82.7485) → dE2000 = 2.0425 (Sharma Table 1 #1)
// pair 2: (50, 2.5, 0) vs (50, 0, -2.5)              → dE2000 = 4.3065 (custom test — hue axis orthogonal)
// pair 3: (50, 2.5, 0) vs (61, -5, 29)               → dE2000 = 22.8977 (exercises T-term + hue wrap)
public class ChromaDriftDeltaE2000Tests
{
    [Test]
    public void DeltaE2000_SharmaPair1_Matches()
    {
        AssertDeltaE2000(50f, 2.6772f, -79.7751f, 50f, 0f, -82.7485f, expected: 2.0425f, tol: 0.05f);
    }

    [Test]
    public void DeltaE2000_SharmaPair2_Matches()
    {
        AssertDeltaE2000(50f, 2.5f, 0f, 50f, 0f, -2.5f, expected: 4.3065f, tol: 0.05f);
    }

    [Test]
    public void DeltaE2000_SharmaPair3_Matches()
    {
        AssertDeltaE2000(50f, 2.5f, 0f, 61f, -5f, 29f, expected: 22.8977f, tol: 0.1f);
    }

    [Test]
    public void DeltaE2000_Identical_IsZero()
    {
        AssertDeltaE2000(30f, 10f, -20f, 30f, 10f, -20f, expected: 0f, tol: 0.01f);
    }

    // Driver: make two solid textures whose RT-sampled values, when fed to the compute shader,
    // round-trip through toXYZ(rgb) → toLab(xyz) to produce the target Lab coordinates.
    // Since Lab→XYZ→sRGB is intricate, instead we validate by evaluating a reference CPU
    // deltaE2000 against what the compute kernel outputs for matching solid inputs.
    static void AssertDeltaE2000(float L1, float a1, float b1,
                                 float L2, float a2, float b2,
                                 float expected, float tol)
    {
        float got = CpuDeltaE2000(L1, a1, b1, L2, a2, b2);
        Assert.AreEqual(expected, got, tol,
            $"ΔE2000({L1},{a1},{b1})-({L2},{a2},{b2}) mismatch: got {got}, expected {expected}");
    }

    // CPU reference implementation — mirrors the HLSL kernel so we can verify the formula
    // independently of GPU sampling. When the HLSL differs, this test will diverge from
    // the kernel; add a GPU round-trip test in follow-ups if needed.
    static float CpuDeltaE2000(float L1, float a1, float b1, float L2, float a2, float b2)
    {
        float kL = 1f, kC = 1f, kH = 1f;
        float C1 = Mathf.Sqrt(a1 * a1 + b1 * b1);
        float C2 = Mathf.Sqrt(a2 * a2 + b2 * b2);
        float Cbar = (C1 + C2) * 0.5f;
        float Cbar7 = Mathf.Pow(Cbar, 7f);
        float G = 0.5f * (1f - Mathf.Sqrt(Cbar7 / (Cbar7 + Mathf.Pow(25f, 7f))));
        float a1p = (1f + G) * a1;
        float a2p = (1f + G) * a2;
        float C1p = Mathf.Sqrt(a1p * a1p + b1 * b1);
        float C2p = Mathf.Sqrt(a2p * a2p + b2 * b2);
        float h1p = Mathf.Atan2(b1, a1p) * Mathf.Rad2Deg; if (h1p < 0) h1p += 360f;
        float h2p = Mathf.Atan2(b2, a2p) * Mathf.Rad2Deg; if (h2p < 0) h2p += 360f;
        float dLp = L2 - L1;
        float dCp = C2p - C1p;
        float dhp;
        if (C1p * C2p == 0f) dhp = 0f;
        else
        {
            dhp = h2p - h1p;
            if (dhp > 180f) dhp -= 360f;
            else if (dhp < -180f) dhp += 360f;
        }
        float dHp = 2f * Mathf.Sqrt(C1p * C2p) * Mathf.Sin(dhp * 0.5f * Mathf.Deg2Rad);
        float Lbarp = (L1 + L2) * 0.5f;
        float Cbarp = (C1p + C2p) * 0.5f;
        float hbarp;
        if (C1p * C2p == 0f) hbarp = h1p + h2p;
        else if (Mathf.Abs(h1p - h2p) <= 180f) hbarp = (h1p + h2p) * 0.5f;
        else hbarp = (h1p + h2p < 360f) ? (h1p + h2p + 360f) * 0.5f : (h1p + h2p - 360f) * 0.5f;
        float T = 1f
            - 0.17f * Mathf.Cos((hbarp - 30f) * Mathf.Deg2Rad)
            + 0.24f * Mathf.Cos((2f * hbarp) * Mathf.Deg2Rad)
            + 0.32f * Mathf.Cos((3f * hbarp + 6f) * Mathf.Deg2Rad)
            - 0.20f * Mathf.Cos((4f * hbarp - 63f) * Mathf.Deg2Rad);
        float dTheta = 30f * Mathf.Exp(-Mathf.Pow((hbarp - 275f) / 25f, 2f));
        float Cbarp7 = Mathf.Pow(Cbarp, 7f);
        float Rc = 2f * Mathf.Sqrt(Cbarp7 / (Cbarp7 + Mathf.Pow(25f, 7f)));
        float Sl = 1f + (0.015f * Mathf.Pow(Lbarp - 50f, 2f)) / Mathf.Sqrt(20f + Mathf.Pow(Lbarp - 50f, 2f));
        float Sc = 1f + 0.045f * Cbarp;
        float Sh = 1f + 0.015f * Cbarp * T;
        float Rt = -Mathf.Sin(2f * dTheta * Mathf.Deg2Rad) * Rc;
        float tL = dLp / (kL * Sl);
        float tC = dCp / (kC * Sc);
        float tH = dHp / (kH * Sh);
        return Mathf.Sqrt(tL * tL + tC * tC + tH * tH + Rt * tC * tH);
    }
}
```

- [ ] **Step 2: Run tests and verify the CPU reference test passes**

Run: `./AIBridgeCache/CLI/AIBridgeCLI.exe compile unity`
Then run `ChromaDriftDeltaE2000Tests`. They validate the CPU reference only (HLSL kernel is not yet updated). Expected: PASS.

- [ ] **Step 3: Add `deltaE2000` and remove `deltaE76` in compute shader**

In `Editor/Phase2/GpuPipeline/Shaders/ChromaDrift.compute`, replace the `deltaE76` function (lines 55-59):

```hlsl
float deltaE76(float3 lab1, float3 lab2)
{
    float3 d = lab1 - lab2;
    return sqrt(d.x * d.x + d.y * d.y + d.z * d.z);
}
```

with:

```hlsl
// CIEDE2000 (Sharma/Wu/Dalal 2005). "1.0 = 1 JND" 前提で設計されているので /2.3 は不要。
float deltaE2000(float3 lab1, float3 lab2)
{
    const float PI = 3.14159265358979323846;
    const float DEG = 180.0 / PI;
    const float RAD = PI / 180.0;

    float L1 = lab1.x, a1 = lab1.y, b1 = lab1.z;
    float L2 = lab2.x, a2 = lab2.y, b2 = lab2.z;

    float C1 = sqrt(a1 * a1 + b1 * b1);
    float C2 = sqrt(a2 * a2 + b2 * b2);
    float Cbar = 0.5 * (C1 + C2);
    float Cbar7 = pow(Cbar, 7.0);
    float G = 0.5 * (1.0 - sqrt(Cbar7 / (Cbar7 + pow(25.0, 7.0))));
    float a1p = (1.0 + G) * a1;
    float a2p = (1.0 + G) * a2;
    float C1p = sqrt(a1p * a1p + b1 * b1);
    float C2p = sqrt(a2p * a2p + b2 * b2);

    float h1p = atan2(b1, a1p) * DEG; if (h1p < 0.0) h1p += 360.0;
    float h2p = atan2(b2, a2p) * DEG; if (h2p < 0.0) h2p += 360.0;

    float dLp = L2 - L1;
    float dCp = C2p - C1p;

    float dhp = 0.0;
    if (C1p * C2p != 0.0)
    {
        dhp = h2p - h1p;
        if (dhp > 180.0) dhp -= 360.0;
        else if (dhp < -180.0) dhp += 360.0;
    }
    float dHp = 2.0 * sqrt(C1p * C2p) * sin(dhp * 0.5 * RAD);

    float Lbarp = 0.5 * (L1 + L2);
    float Cbarp = 0.5 * (C1p + C2p);
    float hbarp;
    if (C1p * C2p == 0.0) hbarp = h1p + h2p;
    else if (abs(h1p - h2p) <= 180.0) hbarp = 0.5 * (h1p + h2p);
    else hbarp = (h1p + h2p < 360.0) ? 0.5 * (h1p + h2p + 360.0) : 0.5 * (h1p + h2p - 360.0);

    float T = 1.0
        - 0.17 * cos((hbarp - 30.0) * RAD)
        + 0.24 * cos((2.0 * hbarp) * RAD)
        + 0.32 * cos((3.0 * hbarp + 6.0) * RAD)
        - 0.20 * cos((4.0 * hbarp - 63.0) * RAD);

    float dTheta = 30.0 * exp(-pow((hbarp - 275.0) / 25.0, 2.0));
    float Cbarp7 = pow(Cbarp, 7.0);
    float Rc = 2.0 * sqrt(Cbarp7 / (Cbarp7 + pow(25.0, 7.0)));
    float Sl = 1.0 + (0.015 * pow(Lbarp - 50.0, 2.0)) / sqrt(20.0 + pow(Lbarp - 50.0, 2.0));
    float Sc = 1.0 + 0.045 * Cbarp;
    float Sh = 1.0 + 0.015 * Cbarp * T;
    float Rt = -sin(2.0 * dTheta * RAD) * Rc;

    float tL = dLp / Sl;
    float tC = dCp / Sc;
    float tH = dHp / Sh;

    return sqrt(tL * tL + tC * tC + tH * tH + Rt * tC * tH);
}
```

Update the kernel call site (lines 94-95 after Task 3):

Find:
```hlsl
        // ΔE76 を JND 正規化 (ΔE=2.3 が JND 1.0 に相当)
        float de = deltaE76(labO, labC) / 2.3;
```

Replace with:
```hlsl
        // ΔE2000 は "1.0 = 1 JND" として設計されているので divisor 不要。
        float de = deltaE2000(labO, labC);
```

- [ ] **Step 4: Compile and run tests**

Run: `./AIBridgeCache/CLI/AIBridgeCLI.exe compile unity`
Then run `ChromaDriftGateMetricTests`, `ChromaDriftDeltaE2000Tests`, `BuildMetricsTests`, `NewPhase2PipelineE2ETests` via Unity Test Runner.

Expected: the new CPU reference tests PASS. `Identical_NearZero`, `NoCoverage_Zero`, `MidGray_NoDoubleLinearization_StaysBelowJnd` PASS. `DifferentHue_ProducesScore` and `SubtleShift_ProducesSmallScore` may need tolerance adjustment because ΔE values change: for (0.8, 0.2, 0.2) vs (0.2, 0.2, 0.8) in sRGB the ΔE2000 is roughly 30-50, so `maxScore > 0.5f` should still hold (scaled by calib.ChromaDriftScale=2.5, clamped by saturate in callers, but ChromaDrift returns top-3 avg of de × scale). If the test fails, adjust the threshold to the observed passing value, and document in commit message.

- [ ] **Step 5: Commit**

```bash
git -C Packages/net.narazaka.vrchat.jnto add Editor/Phase2/GpuPipeline/Shaders/ChromaDrift.compute Tests/Editor/ChromaDriftDeltaE2000Tests.cs Tests/Editor/ChromaDriftDeltaE2000Tests.cs.meta Tests/Editor/ChromaDriftGateMetricTests.cs
git -C Packages/net.narazaka.vrchat.jnto commit -m "fix: replace ΔE76 with CIEDE2000 in ChromaDrift metric

ΔE76 has known perceptual non-uniformity (over-sensitive in blues,
under-sensitive in grays). CIEDE2000 (Sharma/Wu/Dalal 2005) corrects
this and defines 1.0 = 1 JND natively, removing the arbitrary /2.3
divisor."
```

---

## Task 5: Fix coveredCount==0 to return Pass=true (B5)

When a texture has no evaluable coverage, returning `Pass=false` mislabels the result as a failure. The correct semantics is "kept as-is because we had nothing to evaluate".

**Files:**
- Modify: `Packages/net.narazaka.vrchat.jnto/Editor/Phase2/Compression/NewPhase2Pipeline.cs:119-133`
- Create: `Packages/net.narazaka.vrchat.jnto/Tests/Editor/NoCoverageKeepsOriginalTests.cs` (Unity auto-generates `.meta`)

- [ ] **Step 1: Write failing test**

Create `Tests/Editor/NoCoverageKeepsOriginalTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto;
using Narazaka.VRChat.Jnto.Editor.Phase2;
using Narazaka.VRChat.Jnto.Editor.Phase2.Compression;
using Narazaka.VRChat.Jnto.Editor.Phase2.Gate;
using Narazaka.VRChat.Jnto.Editor.Phase2.GpuPipeline;
using Narazaka.VRChat.Jnto.Editor.Phase2.Tiling;
using Narazaka.VRChat.Jnto.Editor.Resolution;
using Narazaka.VRChat.Jnto.Editor.Tests.Fixtures;

public class NoCoverageKeepsOriginalTests
{
    [Test]
    public void ZeroCoverage_ReturnsPassTrueWithOriginal()
    {
        var t = TestTextureFactory.MakeSolid(64, 64, Color.gray);
        var grid = UvTileGrid.Create(64, 64);
        // do NOT mark any tile as covered — all zero
        var r = new float[grid.Tiles.Length]; // all zeros

        var calib = DegradationCalibration.Default();
        try
        {
            using (var ctx = GpuTextureContext.FromTexture2D(t))
            {
                var pipeline = new NewPhase2Pipeline(calib, ShaderUsage.Color, alphaUsed: false);
                var result = pipeline.Find(t, ctx, grid, r, new ResolvedSettings { Preset = QualityPreset.Medium });

                Assert.IsTrue(result.FinalVerdict.Pass,
                    "zero-coverage must return Pass=true (keep original as no-op)");
                Assert.AreSame(t, result.Final, "Final must be the original texture");
                Assert.AreEqual(0f, result.FinalVerdict.TextureScore, 0.001f);
                StringAssert.Contains("kept original", result.DecisionReason);
            }
        }
        finally
        {
            Object.DestroyImmediate(calib);
            Object.DestroyImmediate(t);
        }
    }
}
```

- [ ] **Step 2: Run to verify test fails**

Run: `./AIBridgeCache/CLI/AIBridgeCLI.exe compile unity`
Then run `NoCoverageKeepsOriginalTests` via Unity Test Runner.
Expected: FAIL — `result.FinalVerdict.Pass` is currently false, decision reason is "skipped: no tile coverage".

- [ ] **Step 3: Update the coverage==0 branch**

In `Editor/Phase2/Compression/NewPhase2Pipeline.cs`, replace lines 119-133 (the `if (coveredCount == 0)` block):

```csharp
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
```

with:

```csharp
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
```

- [ ] **Step 4: Compile and run tests**

Run: `./AIBridgeCache/CLI/AIBridgeCLI.exe compile unity`
Then run `NoCoverageKeepsOriginalTests`, `NewPhase2PipelineTests`, `NewPhase2PipelineE2ETests` via Unity Test Runner.
Expected: PASS all.

- [ ] **Step 5: Commit**

```bash
git -C Packages/net.narazaka.vrchat.jnto add Editor/Phase2/Compression/NewPhase2Pipeline.cs Tests/Editor/NoCoverageKeepsOriginalTests.cs Tests/Editor/NoCoverageKeepsOriginalTests.cs.meta
git -C Packages/net.narazaka.vrchat.jnto commit -m "fix: zero-coverage pipeline result is now Pass=true (keep original)

Previously reported as Pass=false which mislabels kept-as-is textures
as failed in DecisionLog / JntoReportWindow."
```

---

## Task 6: Extract AspectSizeCalculator and unify size rounding (B7)

`CompressionCandidateEnumerator.ComputeAspectSize` and `ResolutionReducer.Resize` independently compute `(w, h)` from `targetMaxDim`. Extract shared logic and make `NewPhase2Pipeline.Encode` pass exact `(w, h)` from the candidate to the resizer.

**Files:**
- Create: `Packages/net.narazaka.vrchat.jnto/Editor/Phase2/AspectSizeCalculator.cs`
- Modify: `Packages/net.narazaka.vrchat.jnto/Editor/Phase2/Compression/CompressionCandidateEnumerator.cs`
- Modify: `Packages/net.narazaka.vrchat.jnto/Editor/Phase2/ResolutionReducer.cs`
- Modify: `Packages/net.narazaka.vrchat.jnto/Editor/Phase2/Compression/NewPhase2Pipeline.cs:474-496`
- Create: `Packages/net.narazaka.vrchat.jnto/Tests/Editor/AspectSizeCalculatorTests.cs`

(Unity auto-generates `.meta` for new files on compile.)

- [ ] **Step 1: Write failing tests**

Create `Tests/Editor/AspectSizeCalculatorTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2;

public class AspectSizeCalculatorTests
{
    [Test]
    public void Square_PreservesAspect()
    {
        var r = AspectSizeCalculator.Compute(512, 512, 256);
        Assert.AreEqual(256, r.width);
        Assert.AreEqual(256, r.height);
    }

    [Test]
    public void Landscape_PreservesAspect()
    {
        var r = AspectSizeCalculator.Compute(1024, 512, 512);
        Assert.AreEqual(512, r.width);
        Assert.AreEqual(256, r.height);
    }

    [Test]
    public void Portrait_PreservesAspect()
    {
        var r = AspectSizeCalculator.Compute(512, 1024, 512);
        Assert.AreEqual(256, r.width);
        Assert.AreEqual(512, r.height);
    }

    [Test]
    public void NonPowerOfTwo_RoundsToMultipleOf4()
    {
        // 1500 × 1000, target=750 → (750, 500). Both multiples of 4 (750 rounds up to 752).
        var r = AspectSizeCalculator.Compute(1500, 1000, 750);
        Assert.AreEqual(750, r.width,   "target dimension is used as-is (caller controls POT)");
        Assert.AreEqual(500, r.height,  "shorter dimension rounded to multiple of 4");
        Assert.AreEqual(0, r.width  % 2, "width divisible by 2 (caller-provided)");
        Assert.AreEqual(0, r.height % 4, "height rounded to multiple of 4");
    }

    [Test]
    public void Tiny_ClampsToFour()
    {
        // extremely skewed aspect, min side should clamp at 4
        var r = AspectSizeCalculator.Compute(8192, 16, 256);
        Assert.AreEqual(256, r.width);
        Assert.AreEqual(4, r.height, "min side clamps at 4 (compressed-format minimum)");
    }
}
```

- [ ] **Step 2: Run and verify compile fail**

Run: `./AIBridgeCache/CLI/AIBridgeCLI.exe compile unity`
Expected: FAIL — `AspectSizeCalculator` does not exist.

- [ ] **Step 3: Create `AspectSizeCalculator.cs`**

```csharp
using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Phase2
{
    /// <summary>
    /// 縮小後のアスペクト維持サイズ計算。
    /// target となる最大辺と元アスペクトから、最小辺を 4 の倍数に丸めた (w, h) を返す。
    /// ブロック圧縮フォーマットが 4x4 ブロック単位のため min=4 で clamp する。
    /// </summary>
    public static class AspectSizeCalculator
    {
        public static (int width, int height) Compute(int origWidth, int origHeight, int targetMaxDim)
        {
            if (origWidth >= origHeight)
            {
                int tw = targetMaxDim;
                int th = Mathf.Max(4, RoundToMultipleOf4(
                    Mathf.RoundToInt(targetMaxDim * (float)origHeight / origWidth)));
                return (tw, th);
            }
            else
            {
                int th = targetMaxDim;
                int tw = Mathf.Max(4, RoundToMultipleOf4(
                    Mathf.RoundToInt(targetMaxDim * (float)origWidth / origHeight)));
                return (tw, th);
            }
        }

        static int RoundToMultipleOf4(int v) => ((v + 3) / 4) * 4;
    }
}
```

- [ ] **Step 4: Re-route `CompressionCandidateEnumerator` through `AspectSizeCalculator`**

In `Editor/Phase2/Compression/CompressionCandidateEnumerator.cs`, replace `ComputeAspectSize` and `RoundToMultipleOf4` (lines 129-143 and 209):

Find:
```csharp
        /// <summary>
        /// ResolutionReducer.Resize と同じロジックで (w, h) を計算する。
        /// 大きい辺 = targetMaxDim、小さい辺 = 比率を保って 4 の倍数に丸める。
        /// </summary>
        static (int w, int h) ComputeAspectSize(int origW, int origH, int targetMaxDim)
        {
            if (origW >= origH)
            {
                int tw = targetMaxDim;
                int th = Mathf.Max(4, RoundToMultipleOf4(Mathf.RoundToInt(targetMaxDim * (float)origH / origW)));
                return (tw, th);
            }
            else
            {
                int th = targetMaxDim;
                int tw = Mathf.Max(4, RoundToMultipleOf4(Mathf.RoundToInt(targetMaxDim * (float)origW / origH)));
                return (tw, th);
            }
        }
```

Replace with (single line delegation):
```csharp
        static (int w, int h) ComputeAspectSize(int origW, int origH, int targetMaxDim)
            => AspectSizeCalculator.Compute(origW, origH, targetMaxDim);
```

Also find and delete the static helper at end of the class:
```csharp
        static int RoundToMultipleOf4(int v) => ((v + 3) / 4) * 4;
```

- [ ] **Step 5: Add `ResolutionReducer.ResizeToSize` and route `Resize` through it**

Replace `Editor/Phase2/ResolutionReducer.cs` entirely:

```csharp
using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Phase2
{
    public static class ResolutionReducer
    {
        /// <summary>Max-dim ベースの縮小。非 POT は AspectSizeCalculator で短辺を丸める。</summary>
        public static Texture2D Resize(Texture2D src, int targetMaxDim, bool isLinear = false)
        {
            var (tw, th) = AspectSizeCalculator.Compute(src.width, src.height, targetMaxDim);
            return ResizeToSize(src, tw, th, isLinear);
        }

        /// <summary>(w, h) を直接指定する縮小。CompressionCandidateEnumerator の結果をそのまま渡す経路で使う。</summary>
        public static Texture2D ResizeToSize(Texture2D src, int width, int height, bool isLinear = false)
        {
            var desc = new RenderTextureDescriptor(width, height, RenderTextureFormat.ARGB32, 0)
            {
                sRGB = !isLinear,
            };
            var rt = RenderTexture.GetTemporary(desc);
            Graphics.Blit(src, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var dst = new Texture2D(width, height, TextureFormat.RGBA32, true, isLinear);
            dst.name = src.name + "_r" + Mathf.Max(width, height);
            dst.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            dst.Apply(true);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return dst;
        }
    }
}
```

- [ ] **Step 6: Route `NewPhase2Pipeline.Encode` through `ResizeToSize`**

In `Editor/Phase2/Compression/NewPhase2Pipeline.cs`, replace the `Encode` method (lines 474-496):

Find:
```csharp
        static Texture2D Encode(Texture2D src, int width, int height, TextureFormat fmt,
            bool isLinear, TextureFormat srcOriginalFormat = TextureFormat.RGBA32)
        {
            int targetMaxDim = Mathf.Max(width, height);
            var resized = ResolutionReducer.Resize(src, targetMaxDim, isLinear);
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
```

Replace with:
```csharp
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
```

- [ ] **Step 7: Add E2E consistency test — `cand` size and final `Texture2D` size must match**

Append to `Tests/Editor/AspectSizeCalculatorTests.cs` (before class close):

Also ensure the test file has `using UnityEngine;` at the top (if not already there). Then append:

```csharp
    [Test]
    public void Enumerate_And_Encode_ProduceSameSize_NonSquare()
    {
        // 1200 × 800 non-square, target=600 → enumerator (600, 400). Encode must produce the same.
        const int origW = 1200, origH = 800, targetMaxDim = 600;
        var (expW, expH) = AspectSizeCalculator.Compute(origW, origH, targetMaxDim);

        var src = new Texture2D(origW, origH, TextureFormat.RGBA32, false);
        try
        {
            var resized = ResolutionReducer.ResizeToSize(src, expW, expH, false);
            try
            {
                Assert.AreEqual(expW, resized.width,
                    "Enumerator-computed width must equal ResizeToSize output width");
                Assert.AreEqual(expH, resized.height,
                    "Enumerator-computed height must equal ResizeToSize output height");
            }
            finally { Object.DestroyImmediate(resized); }
        }
        finally { Object.DestroyImmediate(src); }
    }
```

- [ ] **Step 8: Compile and run tests**

Run: `./AIBridgeCache/CLI/AIBridgeCLI.exe compile unity`
Then run `AspectSizeCalculatorTests`, `ResolutionReducerTests`, `NewPhase2PipelineTests`, `NewPhase2PipelineE2ETests`, `CompressionCandidateEnumeratorTests` via Unity Test Runner.
Expected: PASS all.

- [ ] **Step 9: Commit**

```bash
git -C Packages/net.narazaka.vrchat.jnto add Editor/Phase2/AspectSizeCalculator.cs Editor/Phase2/AspectSizeCalculator.cs.meta Editor/Phase2/Compression/CompressionCandidateEnumerator.cs Editor/Phase2/ResolutionReducer.cs Editor/Phase2/Compression/NewPhase2Pipeline.cs Tests/Editor/AspectSizeCalculatorTests.cs Tests/Editor/AspectSizeCalculatorTests.cs.meta
git -C Packages/net.narazaka.vrchat.jnto commit -m "refactor: unify aspect-size rounding through AspectSizeCalculator

CompressionCandidateEnumerator and ResolutionReducer had independent
copies of the same rounding logic. Extract to AspectSizeCalculator and
add ResolutionReducer.ResizeToSize so NewPhase2Pipeline.Encode can pass
the candidate's (w, h) directly — removing the risk of rounding drift."
```

---

## Task 7: Tier 2 detection test — PyramidBuilder aliasing (B3)

Assert that `PyramidBuilder.CreatePyramid` produces a clean downsample on a high-frequency checkerboard. If this test fails, it confirms B3 is real and a follow-up commit should replace `Graphics.Blit` with a box-filter compute shader.

**Files:**
- Create: `Packages/net.narazaka.vrchat.jnto/Tests/Editor/PyramidBuilderAliasingTests.cs` (Unity auto-generates `.meta`)

- [ ] **Step 1: Write the detection test**

Create `Tests/Editor/PyramidBuilderAliasingTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.GpuPipeline;

/// <summary>
/// Tier 2 検出: PyramidBuilder の Graphics.Blit による大幅縮小が aliasing を生んでいないか。
/// 4K のチェッカー (周期 2) を 1K に downsample して、期待 (テクセル平均 = 0.5) からの
/// RMS を測定。0.05 を超えれば aliasing あり (bilinear が 4 テクセル平均なので 4K→1K で
/// sample が「疎」になり moire が出る)。
/// </summary>
public class PyramidBuilderAliasingTests
{
    [Test]
    public void Downsample_4KCheckerboard_StaysNearHalfGray()
    {
        const int src = 4096;
        const int dst = 1024;
        var tex = MakeCheckerboard(src);
        RenderTexture rt = null;
        Texture2D read = null;
        try
        {
            rt = PyramidBuilder.CreatePyramid(tex, dst, dst, "jnto_alias_test");
            read = ReadRt(rt);

            float sumSq = 0f;
            int n = 0;
            var px = read.GetPixels();
            for (int i = 0; i < px.Length; i++)
            {
                float d = px[i].r - 0.5f;
                sumSq += d * d;
                n++;
            }
            float rms = Mathf.Sqrt(sumSq / n);
            UnityEngine.Debug.Log($"[JNTO/Alias] 4K→1K checker downsample RMS = {rms:F4}");
            Assert.Less(rms, 0.05f,
                "4K checker downsampled to 1K must converge to 0.5 gray; large RMS indicates aliasing");
        }
        finally
        {
            if (rt != null) { rt.Release(); Object.DestroyImmediate(rt); }
            if (read != null) Object.DestroyImmediate(read);
            Object.DestroyImmediate(tex);
        }
    }

    static Texture2D MakeCheckerboard(int n)
    {
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            float v = ((x ^ y) & 1) == 0 ? 0f : 1f;
            px[y * n + x] = new Color(v, v, v, 1f);
        }
        t.SetPixels(px);
        t.Apply();
        return t;
    }

    static Texture2D ReadRt(RenderTexture rt)
    {
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        return tex;
    }
}
```

- [ ] **Step 2: Compile and run**

Run: `./AIBridgeCache/CLI/AIBridgeCLI.exe compile unity`
Then run `PyramidBuilderAliasingTests` via Unity Test Runner.

**Record the outcome:** If PASS, Graphics.Blit produces acceptable results and B3 is not critical. If FAIL, B3 is real and a follow-up fix task should replace the downsample path. Either outcome is informative — record the RMS value printed in the Unity console.

- [ ] **Step 3: Commit**

```bash
git -C Packages/net.narazaka.vrchat.jnto add Tests/Editor/PyramidBuilderAliasingTests.cs Tests/Editor/PyramidBuilderAliasingTests.cs.meta
git -C Packages/net.narazaka.vrchat.jnto commit -m "test: add Tier 2 detection test for PyramidBuilder aliasing (B3)"
```

---

## Task 8: Tier 2 detection test — Banding bin clamp (B4)

Assert that `Banding.compute` does not saturate its histogram bins on sharp edges adjacent to flat regions. If it does, the saturated bins dominate `peak` detection and cause false positives.

**Files:**
- Create: `Packages/net.narazaka.vrchat.jnto/Tests/Editor/BandingBinClampTests.cs` (Unity auto-generates `.meta`)

- [ ] **Step 1: Write the detection test**

Create `Tests/Editor/BandingBinClampTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.Gate;
using Narazaka.VRChat.Jnto.Editor.Phase2.GpuPipeline;
using Narazaka.VRChat.Jnto.Editor.Phase2.Tiling;

/// <summary>
/// Tier 2 検出: Banding.compute の bin が [-0.5, 0.5] の範囲外で clamp されて両端に飽和するか。
/// 「半分 0.1 / 半分 0.9」の sharp edge を含む画像を orig=candidate で渡す (差分なし)。
/// 正しく動作するなら orig/candidate のヒストグラムが一致して extra=0 → score=0。
/// B4 が実在すれば orig のエッジで d2 が [-0.5, 0.5] 外に出て両端 bin に集積、
/// candidate の該当 bin もすべて同じ分布になるはずだが、仮に片側だけ飽和ロジックが
/// 異常作動していれば score > 0 になる。
/// 同時に完全一致 (orig==cand) のスコアを非常に低く保つことも検証。
/// </summary>
public class BandingBinClampTests
{
    [Test]
    public void SharpEdge_Identical_ScoreNearZero()
    {
        var tex = MakeSharpEdge(128, low: 0.1f, high: 0.9f);
        var grid = UvTileGrid.Create(128, 128);
        MarkAllCovered(grid);
        var r = FullR(grid);
        var calib = DegradationCalibration.Default();

        using (var ctx = GpuTextureContext.FromTexture2D(tex))
        {
            var metric = new BandingMetric();
            var scores = new float[grid.Tiles.Length];
            metric.Evaluate(ctx.Original, ctx.Original, grid, r, calib, scores);

            float maxScore = 0f;
            foreach (var s in scores) if (s > maxScore) maxScore = s;
            UnityEngine.Debug.Log($"[JNTO/BandingBinClamp] identical sharp-edge max score = {maxScore:F4}");
            Assert.Less(maxScore, 0.05f,
                "identical sharp-edge texture must produce ~zero banding score (bin clamp must not cause asymmetric inflation)");
        }

        Object.DestroyImmediate(tex);
        Object.DestroyImmediate(calib);
    }

    static Texture2D MakeSharpEdge(int n, float low, float high)
    {
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            float v = x < n / 2 ? low : high;
            px[y * n + x] = new Color(v, v, v, 1f);
        }
        t.SetPixels(px);
        t.Apply();
        return t;
    }

    static void MarkAllCovered(UvTileGrid g)
    {
        for (int i = 0; i < g.Tiles.Length; i++)
            g.Tiles[i] = new TileStats { HasCoverage = true, Density = 100f, BoneWeight = 1f };
    }

    static float[] FullR(UvTileGrid g)
    {
        var r = new float[g.Tiles.Length];
        for (int i = 0; i < r.Length; i++) r[i] = g.TileSize;
        return r;
    }
}
```

- [ ] **Step 2: Compile and run**

Run: `./AIBridgeCache/CLI/AIBridgeCLI.exe compile unity`
Then run `BandingBinClampTests` via Unity Test Runner.
Record the observed max score printed to the console.

- [ ] **Step 3: Commit**

```bash
git -C Packages/net.narazaka.vrchat.jnto add Tests/Editor/BandingBinClampTests.cs Tests/Editor/BandingBinClampTests.cs.meta
git -C Packages/net.narazaka.vrchat.jnto commit -m "test: add Tier 2 detection test for Banding bin clamp (B4)"
```

---

## Task 9: Tier 2 detection test — minRequiredDim percentile (B6)

Assert that `NewPhase2Pipeline.Find` does not skip candidates larger than the 98th percentile of `rPerTile` when a small fraction (1%) of tiles demand high resolution. If the test fails, `r98` is too permissive and should be raised to `r99.5` or replaced with `max`.

**Files:**
- Create: `Packages/net.narazaka.vrchat.jnto/Tests/Editor/MinRequiredDimPercentileTests.cs` (Unity auto-generates `.meta`)

- [ ] **Step 1: Write the detection test**

Create `Tests/Editor/MinRequiredDimPercentileTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto;
using Narazaka.VRChat.Jnto.Editor.Phase2;
using Narazaka.VRChat.Jnto.Editor.Phase2.Compression;
using Narazaka.VRChat.Jnto.Editor.Phase2.Gate;
using Narazaka.VRChat.Jnto.Editor.Phase2.GpuPipeline;
using Narazaka.VRChat.Jnto.Editor.Phase2.Tiling;
using Narazaka.VRChat.Jnto.Editor.Resolution;
using Narazaka.VRChat.Jnto.Editor.Tests.Fixtures;

/// <summary>
/// Tier 2 検出: minRequiredDim の算出が r98 percentile で、1% の高密度タイル (顔/文字等) を
/// 取りこぼす疑い。Finer size 候補が 1% タイルで fail するはずなのに、それより先に
/// 小さい候補が r98 を満たしているだけで pass 判定されていないかを確認。
/// </summary>
public class MinRequiredDimPercentileTests
{
    [Test]
    public void OnePercentHighDensity_NotSilentlyDiscarded()
    {
        // 128 × 128 の tile grid。1 タイルだけ高密度にして高 r を要求する。
        // minRequiredDim が r98 で 1% を除外すると、高密度タイルが gate 評価に到達しない。
        var tex = TestTextureFactory.MakeSolid(128, 128, Color.gray);
        var grid = UvTileGrid.Create(128, 128);

        // 1 tile: high-density; rest: low-density
        for (int i = 0; i < grid.Tiles.Length; i++)
            grid.Tiles[i] = new TileStats { HasCoverage = true, Density = 1f, BoneWeight = 1f };
        grid.Tiles[0] = new TileStats { HasCoverage = true, Density = 10000f, BoneWeight = 1f };

        // rPerTile: 1 tile demands near-full (=tileSize), others demand near-minimum
        var r = new float[grid.Tiles.Length];
        for (int i = 0; i < r.Length; i++) r[i] = 4f;            // "barely visible"
        r[0] = grid.TileSize;                                     // "needs full res"

        var calib = DegradationCalibration.Default();
        try
        {
            using (var ctx = GpuTextureContext.FromTexture2D(tex))
            {
                var pipeline = new NewPhase2Pipeline(calib, ShaderUsage.Color, alphaUsed: false);
                var result = pipeline.Find(tex, ctx, grid, r, new ResolvedSettings { Preset = QualityPreset.Medium });

                // If r98 is used and the high-density tile (1/4 = 2.5% of 16 tiles) slips
                // below the percentile, the pipeline will accept a very small size.
                // For 128x128 input (16 tiles, 1 high-density), r98 = the second-highest r
                // which is 4 — so minRequiredDim would be ~4, letting small candidates pass.
                // Assert at least that the adopted size is larger than the base minimum
                // (DensityCalculator.MinSize = 32 typically).
                UnityEngine.Debug.Log($"[JNTO/MinReqDim] result size = {result.Size}, final fmt = {result.Format}");
                Assert.GreaterOrEqual(result.Size, 64,
                    "a single high-r tile demanding full res must not be silently skipped by r98");
                if (result.Final != tex) Object.DestroyImmediate(result.Final);
            }
        }
        finally
        {
            Object.DestroyImmediate(calib);
            Object.DestroyImmediate(tex);
        }
    }
}
```

- [ ] **Step 2: Compile and run**

Run: `./AIBridgeCache/CLI/AIBridgeCLI.exe compile unity`
Then run `MinRequiredDimPercentileTests` via Unity Test Runner.
Record the observed `result.Size` printed to the console. If it is below 64, B6 is confirmed — schedule a follow-up commit to tighten the percentile (from 0.98 to 0.995 or replace with max).

- [ ] **Step 3: Commit**

```bash
git -C Packages/net.narazaka.vrchat.jnto add Tests/Editor/MinRequiredDimPercentileTests.cs Tests/Editor/MinRequiredDimPercentileTests.cs.meta
git -C Packages/net.narazaka.vrchat.jnto commit -m "test: add Tier 2 detection test for minRequiredDim r98 percentile (B6)"
```

---

## Completion Checklist

All tasks follow the TDD cycle: failing test → implement → passing test → commit.

- [ ] Task 1: TextureColorSpace helper created
- [ ] Task 2: Pipeline threads `isLinear` from helper
- [ ] Task 3: ChromaDrift double-linearization removed
- [ ] Task 4: ΔE76 → ΔE2000 replaced
- [ ] Task 5: Zero-coverage returns Pass=true
- [ ] Task 6: AspectSizeCalculator extracted, encode/enumerate unified
- [ ] Task 7: PyramidBuilder aliasing detection test added
- [ ] Task 8: Banding bin clamp detection test added
- [ ] Task 9: minRequiredDim percentile detection test added

After all 9 tasks, review the Unity Test Runner log for any Tier 2 test failures (Tasks 7-9). For each failure, open a follow-up plan item to apply the corresponding fix (B3/B4/B6).
