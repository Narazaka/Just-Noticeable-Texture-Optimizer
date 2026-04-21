using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class MaterialCloneTests
{
    [Test]
    public void Instantiate_PreservesAllSerializedProperties()
    {
        var shader = Shader.Find("lilToon");
        if (shader == null) Assert.Ignore("lilToon not installed");

        var original = new Material(shader);
        original.renderQueue = 2500;
        original.SetFloat("_Cutoff", 0.3f);
        original.SetColor("_Color", Color.red);
        original.enableInstancing = true;

        var clone = Object.Instantiate(original);

        var soOrig = new SerializedObject(original);
        var soClone = new SerializedObject(clone);

        var propOrig = soOrig.GetIterator();
        var propClone = soClone.GetIterator();

        var diffs = new System.Collections.Generic.List<string>();
        while (propOrig.NextVisible(true))
        {
            var cloneProp = soClone.FindProperty(propOrig.propertyPath);
            if (cloneProp == null) { diffs.Add($"MISSING in clone: {propOrig.propertyPath}"); continue; }

            if (propOrig.propertyPath == "m_Name") continue; // name can differ

            if (!SerializedProperty.DataEquals(propOrig, cloneProp))
                diffs.Add($"DIFF: {propOrig.propertyPath} (type={propOrig.propertyType})");
        }

        if (diffs.Count > 0)
            Assert.Fail("Object.Instantiate produced differences:\n" + string.Join("\n", diffs));
    }

    [Test]
    public void NewMaterial_PreservesAllSerializedProperties()
    {
        var shader = Shader.Find("lilToon");
        if (shader == null) Assert.Ignore("lilToon not installed");

        var original = new Material(shader);
        original.renderQueue = 2500;
        original.SetFloat("_Cutoff", 0.3f);
        original.SetColor("_Color", Color.red);
        original.enableInstancing = true;

        var clone = new Material(original);

        var soOrig = new SerializedObject(original);
        var soClone = new SerializedObject(clone);

        var propOrig = soOrig.GetIterator();

        var diffs = new System.Collections.Generic.List<string>();
        while (propOrig.NextVisible(true))
        {
            var cloneProp = soClone.FindProperty(propOrig.propertyPath);
            if (cloneProp == null) { diffs.Add($"MISSING in clone: {propOrig.propertyPath}"); continue; }

            if (propOrig.propertyPath == "m_Name") continue;

            if (!SerializedProperty.DataEquals(propOrig, cloneProp))
                diffs.Add($"DIFF: {propOrig.propertyPath} (type={propOrig.propertyType})");
        }

        if (diffs.Count > 0)
            Assert.Fail("new Material() produced differences:\n" + string.Join("\n", diffs));
    }
}
