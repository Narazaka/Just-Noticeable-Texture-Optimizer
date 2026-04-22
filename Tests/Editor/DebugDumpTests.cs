using NUnit.Framework;
using System.IO;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.Reporting;
using Narazaka.VRChat.Jnto.Editor.Phase2.Tiling;

public class DebugDumpTests
{
    string _tmpDir;

    [SetUp]
    public void Setup()
    {
        _tmpDir = Path.Combine(Application.temporaryCachePath, "JntoDebugDumpTest");
        if (Directory.Exists(_tmpDir)) Directory.Delete(_tmpDir, true);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tmpDir)) Directory.Delete(_tmpDir, true);
    }

    [Test]
    public void DumpTileScores_WritesCsv()
    {
        var grid = UvTileGrid.Create(32, 32);
        for (int i = 0; i < grid.Tiles.Length; i++)
            grid.Tiles[i] = new TileStats { HasCoverage = true, Density = 100f, BoneWeight = 1f };

        var scoreA = new float[grid.Tiles.Length];
        var scoreB = new float[grid.Tiles.Length];
        for (int i = 0; i < scoreA.Length; i++) { scoreA[i] = 0.5f; scoreB[i] = 0.8f; }

        DebugDump.DumpTileScores(_tmpDir, "test", grid,
            new[] { scoreA, scoreB }, new[] { "MSSL", "Ridge" });

        var csvPath = Path.Combine(_tmpDir, "test_tiles.csv");
        Assert.IsTrue(File.Exists(csvPath));
        var content = File.ReadAllText(csvPath);
        StringAssert.Contains("tx,ty,hasCoverage,density,boneW,MSSL,Ridge", content);
        StringAssert.Contains("0.5000", content);
        StringAssert.Contains("0.8000", content);
    }

    [Test]
    public void DumpHeatmapPng_WritesPng()
    {
        var grid = UvTileGrid.Create(64, 64);
        for (int i = 0; i < grid.Tiles.Length; i++)
            grid.Tiles[i] = new TileStats { HasCoverage = true };
        var scores = new float[grid.Tiles.Length];
        for (int i = 0; i < scores.Length; i++) scores[i] = i / (float)scores.Length;

        DebugDump.DumpHeatmapPng(_tmpDir, "tex", "MSSL", grid, scores);

        var pngPath = Path.Combine(_tmpDir, "tex_MSSL.png");
        Assert.IsTrue(File.Exists(pngPath));
        var bytes = File.ReadAllBytes(pngPath);
        Assert.Greater(bytes.Length, 8);
        // PNG magic
        Assert.AreEqual(0x89, bytes[0]);
        Assert.AreEqual(0x50, bytes[1]);  // 'P'
        Assert.AreEqual(0x4E, bytes[2]);  // 'N'
        Assert.AreEqual(0x47, bytes[3]);  // 'G'
    }

    [Test]
    public void EmptyOutputDir_NoOp()
    {
        var grid = UvTileGrid.Create(16, 16);
        Assert.DoesNotThrow(() =>
        {
            DebugDump.DumpTileScores(null, "x", grid, new float[0][], new string[0]);
            DebugDump.DumpTileScores("", "x", grid, new float[0][], new string[0]);
            DebugDump.DumpHeatmapPng(null, "x", "y", grid, new float[grid.Tiles.Length]);
        });
    }

    [Test]
    public void TextureName_WithInvalidChars_Sanitized()
    {
        var grid = UvTileGrid.Create(16, 16);
        for (int i = 0; i < grid.Tiles.Length; i++) grid.Tiles[i] = new TileStats { HasCoverage = true };
        var scores = new float[grid.Tiles.Length];

        DebugDump.DumpHeatmapPng(_tmpDir, "tex/with:bad*chars", "Met", grid, scores);

        // ファイルが何かしら作成されていること (sanitize された名前で)
        var files = Directory.GetFiles(_tmpDir);
        Assert.Greater(files.Length, 0);
    }
}
