using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;
using Narazaka.VRChat.Jnto;
using Narazaka.VRChat.Jnto.Editor.Phase2;
using Narazaka.VRChat.Jnto.Editor.Phase2.Tiling;
using Narazaka.VRChat.Jnto.Editor.Resolution;

public class TileGridBuilderTests
{
    [Test]
    public void NoSources_ReturnsEmptyGrid()
    {
        var grid = TileGridBuilder.Build(128, 128, null, null, null);
        Assert.AreEqual(128 / 16, grid.TilesX);  // small texture → tileSize=16
        foreach (var t in grid.Tiles) Assert.IsFalse(t.HasCoverage);
    }

    [Test]
    public void SingleRenderer_BuildsCoverage()
    {
        var go = new GameObject("r1");
        var mf = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        var mesh = MakeQuad(1f, 1f, uScale: 1f, vScale: 1f);
        mf.sharedMesh = mesh;

        var settings = new Dictionary<Renderer, ResolvedSettings>
        {
            { mr, new ResolvedSettings() },
        };

        var grid = TileGridBuilder.Build(128, 128,
            new[] { ((Renderer)mr, mesh) },
            null, settings);

        int covered = 0;
        foreach (var t in grid.Tiles) if (t.HasCoverage) covered++;
        Assert.Greater(covered, 0);

        Object.DestroyImmediate(go);
        Object.DestroyImmediate(mesh);
    }

    [Test]
    public void MultipleRenderers_MergeMaxDensity()
    {
        var go1 = new GameObject("r1");
        var mf1 = go1.AddComponent<MeshFilter>();
        var mr1 = go1.AddComponent<MeshRenderer>();
        // world 面積小 (1m 級), UV は (0,0)-(0.5,0.5)
        var mesh1 = MakeQuad(1f, 1f, uScale: 0.5f, vScale: 0.5f);
        mf1.sharedMesh = mesh1;

        var go2 = new GameObject("r2");
        var mf2 = go2.AddComponent<MeshFilter>();
        var mr2 = go2.AddComponent<MeshRenderer>();
        // world 面積大 (10m 級), 同じ UV 領域に被せる → 密度が大きい
        var mesh2 = MakeQuad(10f, 10f, uScale: 0.5f, vScale: 0.5f);
        mf2.sharedMesh = mesh2;

        var settings = new Dictionary<Renderer, ResolvedSettings>
        {
            { mr1, new ResolvedSettings() },
            { mr2, new ResolvedSettings() },
        };

        var grid = TileGridBuilder.Build(128, 128,
            new[] { ((Renderer)mr1, mesh1), ((Renderer)mr2, mesh2) },
            null, settings);

        float maxDensity = 0f;
        foreach (var t in grid.Tiles) if (t.Density > maxDensity) maxDensity = t.Density;
        // mesh2 (world 100 m² = 1e6 cm² / uv 0.125) ≈ 8e6 cm²/uv²
        Assert.Greater(maxDensity, 1e6f);

        Object.DestroyImmediate(go1);
        Object.DestroyImmediate(go2);
        Object.DestroyImmediate(mesh1);
        Object.DestroyImmediate(mesh2);
    }

    [Test]
    public void RendererWithoutSettings_IsSkipped()
    {
        var go = new GameObject("r");
        var mf = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        var mesh = MakeQuad(1f, 1f, uScale: 1f, vScale: 1f);
        mf.sharedMesh = mesh;

        var emptySettings = new Dictionary<Renderer, ResolvedSettings>();
        var grid = TileGridBuilder.Build(128, 128,
            new[] { ((Renderer)mr, mesh) },
            null, emptySettings);

        foreach (var t in grid.Tiles)
            Assert.IsFalse(t.HasCoverage, "renderer without settings should be skipped");

        Object.DestroyImmediate(go);
        Object.DestroyImmediate(mesh);
    }

    [Test]
    public void NullMesh_IsSkipped()
    {
        var go = new GameObject("r");
        var mr = go.AddComponent<MeshRenderer>();
        var settings = new Dictionary<Renderer, ResolvedSettings>
        {
            { mr, new ResolvedSettings() },
        };
        var grid = TileGridBuilder.Build(128, 128,
            new[] { ((Renderer)mr, (Mesh)null) },
            null, settings);

        foreach (var t in grid.Tiles) Assert.IsFalse(t.HasCoverage);

        Object.DestroyImmediate(go);
    }

    static Mesh MakeQuad(float worldX, float worldY, float uScale, float vScale)
    {
        var m = new Mesh
        {
            vertices = new[]
            {
                new Vector3(0, 0, 0),
                new Vector3(worldX, 0, 0),
                new Vector3(0, worldY, 0),
                new Vector3(worldX, worldY, 0),
            },
            uv = new[]
            {
                new Vector2(0, 0),
                new Vector2(uScale, 0),
                new Vector2(0, vScale),
                new Vector2(uScale, vScale),
            },
            triangles = new[] { 0, 1, 2, 2, 1, 3 },
        };
        return m;
    }
}
