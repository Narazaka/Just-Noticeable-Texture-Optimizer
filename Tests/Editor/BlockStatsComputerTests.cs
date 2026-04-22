using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.Compression;
using Narazaka.VRChat.Jnto.Editor.Phase2.GpuPipeline;

public class BlockStatsComputerTests
{
    [Test]
    public void FlatColor_LowNonlinearity()
    {
        var t = Solid(64, 64, Color.red);
        using (var ctx = GpuTextureContext.FromTexture2D(t))
        {
            var stats = BlockStatsComputer.Compute(ctx.Original, 64, 64);
            Assert.Greater(stats.Length, 0);
            foreach (var s in stats)
            {
                Assert.Less(s.Planarity, 0.1f, "flat color should have near-zero planarity");
                Assert.Less(s.Nonlinearity, 0.1f);
            }
        }
        Object.DestroyImmediate(t);
    }

    [Test]
    public void RandomColors_HasNonZeroPlanarity()
    {
        var t = Random64(64);
        using (var ctx = GpuTextureContext.FromTexture2D(t))
        {
            var stats = BlockStatsComputer.Compute(ctx.Original, 64, 64);
            int nonFlatBlocks = 0;
            foreach (var s in stats) if (s.Planarity > 0.15f) nonFlatBlocks++;
            Assert.Greater(nonFlatBlocks, stats.Length / 4,
                "random colors should produce many high-planarity blocks");
        }
        Object.DestroyImmediate(t);
    }

    [Test]
    public void BlockCount_MatchesTextureDivision()
    {
        var t = Solid(64, 32, Color.green);
        using (var ctx = GpuTextureContext.FromTexture2D(t))
        {
            var stats = BlockStatsComputer.Compute(ctx.Original, 64, 32);
            Assert.AreEqual((64 / 4) * (32 / 4), stats.Length);
        }
        Object.DestroyImmediate(t);
    }

    [Test]
    public void MeanColor_ReportedCorrectly()
    {
        // Note: GpuTextureContext uses sRGB RenderTexture; compute shader samples linear values.
        // Use pure red (sRGB=1.0, linear=1.0) so gamma conversion does not affect expectations.
        var c = new Color(1f, 0f, 0f, 1f);
        var t = Solid(16, 16, c);
        using (var ctx = GpuTextureContext.FromTexture2D(t))
        {
            var stats = BlockStatsComputer.Compute(ctx.Original, 16, 16);
            var expected = c.linear;
            Assert.AreEqual(expected.r, stats[0].MeanR, 0.05f);
            Assert.AreEqual(expected.g, stats[0].MeanG, 0.05f);
            Assert.AreEqual(expected.b, stats[0].MeanB, 0.05f);
        }
        Object.DestroyImmediate(t);
    }

    static Texture2D Solid(int w, int h, Color c)
    {
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var px = new Color[w * h];
        for (int i = 0; i < px.Length; i++) px[i] = c;
        t.SetPixels(px);
        t.Apply();
        return t;
    }

    static Texture2D Random64(int n)
    {
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
        var px = new Color[n * n];
        var rng = new System.Random(12345);
        for (int i = 0; i < px.Length; i++)
            px[i] = new Color((float)rng.NextDouble(), (float)rng.NextDouble(), (float)rng.NextDouble(), 1f);
        t.SetPixels(px);
        t.Apply();
        return t;
    }
}
