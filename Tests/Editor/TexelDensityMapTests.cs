using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2;

public class TexelDensityMapTests
{
    GameObject _go;

    [TearDown]
    public void Cleanup()
    {
        if (_go) Object.DestroyImmediate(_go);
    }

    [Test]
    public void Build_Quad_FillsCoveredPixels()
    {
        _go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        var renderer = _go.GetComponent<MeshRenderer>();
        var mesh = _go.GetComponent<MeshFilter>().sharedMesh;

        var map = TexelDensityMap.Build(renderer, mesh, 32, 32, null, null);
        Assert.AreEqual(32, map.Width);
        Assert.AreEqual(32, map.Height);

        int nonZero = 0;
        for (int i = 0; i < map.Density.Length; i++)
            if (map.Density[i] > 0f) nonZero++;
        Assert.Greater(nonZero, 0, "Some pixels should have density > 0");
    }

    [Test]
    public void Build_NullMesh_ReturnsEmptyMap()
    {
        _go = new GameObject("empty");
        var renderer = _go.AddComponent<MeshRenderer>();
        var map = TexelDensityMap.Build(renderer, null, 16, 16, null, null);
        Assert.AreEqual(16, map.Width);
        for (int i = 0; i < map.Density.Length; i++)
            Assert.AreEqual(0f, map.Density[i]);
    }

    [Test]
    public void Merge_TakeMaxPerPixel()
    {
        var a = new TexelDensityMap { Width = 4, Height = 4, Density = new float[16] };
        var b = new TexelDensityMap { Width = 4, Height = 4, Density = new float[16] };
        a.Density[0] = 0.5f; a.Density[1] = 0.8f;
        b.Density[0] = 0.9f; b.Density[1] = 0.3f;

        var merged = TexelDensityMap.Merge(a, b);
        Assert.AreEqual(0.9f, merged.Density[0], 0.001f);
        Assert.AreEqual(0.8f, merged.Density[1], 0.001f);
    }

    [Test]
    public void Merge_NullA_ReturnsB()
    {
        var b = new TexelDensityMap { Width = 4, Height = 4, Density = new float[16] };
        var merged = TexelDensityMap.Merge(null, b);
        Assert.AreSame(b, merged);
    }
}
