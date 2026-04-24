using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Shared;

public class TextureReferenceCollectorTests
{
    GameObject _root;
    [TearDown] public void Cleanup() { if (_root) Object.DestroyImmediate(_root); }

    [Test]
    public void CollectsMainTexFromSingleRenderer()
    {
        _root = new GameObject("avatar");
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.transform.SetParent(_root.transform);

        var sh = Shader.Find("lilToon");
        if (sh == null) Assert.Ignore("lilToon not installed");
        var mat = new Material(sh);
        var tex = new Texture2D(4, 4);
        mat.SetTexture("_MainTex", tex);
        go.GetComponent<Renderer>().sharedMaterial = mat;

        var g = TextureReferenceCollector.Collect(_root);

        Assert.IsTrue(g.Map.ContainsKey(tex));
        Assert.AreEqual(1, g.Map[tex].Count);
        Assert.AreEqual("_MainTex", g.Map[tex][0].PropertyName);
    }
}
