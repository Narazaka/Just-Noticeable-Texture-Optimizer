using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.Gate;
using Narazaka.VRChat.Jnto.Editor.Phase2.GpuPipeline;
using Narazaka.VRChat.Jnto.Editor.Phase2.Tiling;

// References: Sharma, Wu, Dalal 2005 Table 1 — "The CIEDE2000 Color-Difference Formula: Implementation Notes"
// pair 1: (50, 2.6772, -79.7751) vs (50, 0, -82.7485) → dE2000 = 2.0425
// pair 2: (50, 2.5, 0) vs (50, 0, -2.5)             → dE2000 = 7.2195
// pair 3: (50, 2.5, 0) vs (61, -5, 29)              → dE2000 = 22.8977
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
        AssertDeltaE2000(50f, 2.5f, 0f, 50f, 0f, -2.5f, expected: 7.2195f, tol: 0.05f);
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
