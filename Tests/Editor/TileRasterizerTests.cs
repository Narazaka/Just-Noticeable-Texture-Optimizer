using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;
using Narazaka.VRChat.Jnto;
using Narazaka.VRChat.Jnto.Editor.Phase2;
using Narazaka.VRChat.Jnto.Editor.Phase2.Tiling;

public class TileRasterizerTests
{
    [Test]
    public void SingleTriangle_CoversMultipleTiles()
    {
        var go = new GameObject("r");
        var mf = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        var mesh = new Mesh
        {
            vertices = new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0) },
            uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1) },
            triangles = new[] { 0, 1, 2 },
        };
        mesh.RecalculateBounds();
        mf.sharedMesh = mesh;

        var grid = UvTileGrid.Create(128, 128);
        TileRasterizer.Accumulate(grid, mr, mesh, null, null);

        int covered = 0;
        foreach (var t in grid.Tiles) if (t.HasCoverage) covered++;
        Assert.Greater(covered, 0);
        Assert.Greater(covered, grid.Tiles.Length / 4,
            "triangle covering UV half should mark many tiles");

        Object.DestroyImmediate(go);
        Object.DestroyImmediate(mesh);
    }

    [Test]
    public void DenseTriangle_HasHigherDensity()
    {
        var go = new GameObject("r");
        var mf = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        // 大きい world 三角形 (1m 級), 小さい UV 三角形 (0.1) → 高密度
        var mesh = new Mesh
        {
            vertices = new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0) },
            uv = new[] { new Vector2(0, 0), new Vector2(0.1f, 0), new Vector2(0, 0.1f) },
            triangles = new[] { 0, 1, 2 },
        };
        mf.sharedMesh = mesh;

        var grid = UvTileGrid.Create(128, 128);
        TileRasterizer.Accumulate(grid, mr, mesh, null, null);

        // 左下タイル (UV 0,0 周辺) に高密度が書かれているはず
        var t = grid.GetTile(0, 0);
        Assert.IsTrue(t.HasCoverage);
        Assert.Greater(t.Density, 100f);

        Object.DestroyImmediate(go);
        Object.DestroyImmediate(mesh);
    }

    [Test]
    public void EmptyMesh_DoesNothing()
    {
        var go = new GameObject("r");
        go.AddComponent<MeshRenderer>();
        var mr = go.GetComponent<MeshRenderer>();
        var grid = UvTileGrid.Create(64, 64);

        TileRasterizer.Accumulate(grid, mr, null, null, null);

        foreach (var t in grid.Tiles)
            Assert.IsFalse(t.HasCoverage);

        Object.DestroyImmediate(go);
    }

    [Test]
    public void DegenerateTriangle_Skipped()
    {
        // UV が退化 (面積 0) の三角形は無視される
        var go = new GameObject("r");
        var mf = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        var mesh = new Mesh
        {
            vertices = new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0) },
            uv = new[] { new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 0) },
            triangles = new[] { 0, 1, 2 },
        };
        mf.sharedMesh = mesh;

        var grid = UvTileGrid.Create(64, 64);
        TileRasterizer.Accumulate(grid, mr, mesh, null, null);

        foreach (var t in grid.Tiles)
            Assert.IsFalse(t.HasCoverage);

        Object.DestroyImmediate(go);
        Object.DestroyImmediate(mesh);
    }

    [Test]
    public void MultipleTriangles_MaxDensityWins()
    {
        // 同じタイルに 2 三角形書き込み → 大きい方が残る
        var go = new GameObject("r");
        var mf = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        var mesh = new Mesh
        {
            vertices = new[]
            {
                // 三角形 A: world 面積小
                new Vector3(0, 0, 0), new Vector3(0.1f, 0, 0), new Vector3(0, 0.1f, 0),
                // 三角形 B: world 面積大
                new Vector3(0, 0, 0), new Vector3(2, 0, 0), new Vector3(0, 2, 0),
            },
            uv = new[]
            {
                new Vector2(0, 0), new Vector2(0.1f, 0), new Vector2(0, 0.1f),
                new Vector2(0.2f, 0.2f), new Vector2(0.3f, 0.2f), new Vector2(0.2f, 0.3f),
            },
            triangles = new[] { 0, 1, 2, 3, 4, 5 },
        };
        mf.sharedMesh = mesh;

        var grid = UvTileGrid.Create(64, 64);
        TileRasterizer.Accumulate(grid, mr, mesh, null, null);

        float maxDensity = 0f;
        foreach (var t in grid.Tiles) if (t.Density > maxDensity) maxDensity = t.Density;
        // triangle B (world 4 m² / uv 0.005) ≈ 8e6 cm²/uv² がトップ
        Assert.Greater(maxDensity, 1e5f);

        Object.DestroyImmediate(go);
        Object.DestroyImmediate(mesh);
    }

    [Test]
    public void TilingUvs_WrappedCoverage()
    {
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
}
