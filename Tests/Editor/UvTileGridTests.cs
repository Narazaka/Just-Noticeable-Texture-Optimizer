using NUnit.Framework;
using Narazaka.VRChat.Jnto.Editor.Phase2.Tiling;

public class UvTileGridTests
{
    [Test]
    public void DetermineTileSize_ReturnsFixed64_For_512AndAbove()
    {
        Assert.AreEqual(64, UvTileGrid.DetermineTileSize(4096, 4096));
        Assert.AreEqual(64, UvTileGrid.DetermineTileSize(2048, 2048));
        Assert.AreEqual(64, UvTileGrid.DetermineTileSize(1024, 1024));
        Assert.AreEqual(64, UvTileGrid.DetermineTileSize(512, 512));
    }

    [Test]
    public void DetermineTileSize_ScalesDown_For_SmallTextures()
    {
        Assert.AreEqual(32, UvTileGrid.DetermineTileSize(256, 256));
        Assert.AreEqual(16, UvTileGrid.DetermineTileSize(128, 128));
        Assert.AreEqual(16, UvTileGrid.DetermineTileSize(64, 64));
    }

    [Test]
    public void DetermineTileSize_UsesMaxDim_ForRect()
    {
        Assert.AreEqual(64, UvTileGrid.DetermineTileSize(2048, 1024));
        Assert.AreEqual(32, UvTileGrid.DetermineTileSize(256, 128));
    }

    [Test]
    public void Create_HasCorrectTileCount()
    {
        var g = UvTileGrid.Create(4096, 2048);
        Assert.AreEqual(64, g.TileSize);
        Assert.AreEqual(64, g.TilesX);
        Assert.AreEqual(32, g.TilesY);
        Assert.AreEqual(64 * 32, g.Tiles.Length);
    }

    [Test]
    public void Create_InitializesTileStatsToDefaults()
    {
        var g = UvTileGrid.Create(128, 128);
        foreach (var t in g.Tiles)
        {
            Assert.AreEqual(0f, t.Density);
            Assert.AreEqual(0f, t.BoneWeight);
            Assert.IsFalse(t.HasCoverage);
        }
    }

    [Test]
    public void GetTile_ReturnsReference()
    {
        var g = UvTileGrid.Create(128, 128);
        ref var t = ref g.GetTile(0, 0);
        t.Density = 42f;
        Assert.AreEqual(42f, g.Tiles[0].Density, "GetTile should return a ref so writes propagate");
    }
}
