using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class MaterialCloneAssetTests
{
    [Test]
    public void Instantiate_RealLilToonAsset_PreservesAllProperties()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Kyubi closet/Airi/Materials/lilToon/lilToon_Body.mat");
        if (mat == null) Assert.Ignore("Test material not found");

        var clone = Object.Instantiate(mat);

        var soOrig = new SerializedObject(mat);
        var soClone = new SerializedObject(clone);

        var diffs = new System.Collections.Generic.List<string>();
        var propOrig = soOrig.GetIterator();
        while (propOrig.NextVisible(true))
        {
            if (propOrig.propertyPath == "m_Name") continue;
            var cloneProp = soClone.FindProperty(propOrig.propertyPath);
            if (cloneProp == null) { diffs.Add($"MISSING: {propOrig.propertyPath}"); continue; }
            if (!SerializedProperty.DataEquals(propOrig, cloneProp))
                diffs.Add($"DIFF: {propOrig.propertyPath} type={propOrig.propertyType}");
        }

        Object.DestroyImmediate(clone);
        if (diffs.Count > 0)
            Assert.Fail($"Object.Instantiate differences ({diffs.Count}):\n" + string.Join("\n", diffs));
    }

    [Test]
    public void SetTexture_OnClone_DoesNotChangeOtherProperties()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Kyubi closet/Airi/Materials/lilToon/lilToon_Body.mat");
        if (mat == null) Assert.Ignore("Test material not found");

        var clone = Object.Instantiate(mat);
        var soBeforeSetTex = new SerializedObject(clone);
        var snapshotBefore = new System.Collections.Generic.Dictionary<string, string>();
        var prop = soBeforeSetTex.GetIterator();
        while (prop.NextVisible(true))
        {
            if (prop.propertyPath.Contains("m_TexEnvs")) continue; // skip texture entries
            if (prop.propertyPath == "m_Name") continue;
            snapshotBefore[prop.propertyPath] = prop.propertyType == SerializedPropertyType.Float
                ? prop.floatValue.ToString("R")
                : prop.propertyType == SerializedPropertyType.Integer
                ? prop.intValue.ToString()
                : prop.propertyType == SerializedPropertyType.Boolean
                ? prop.boolValue.ToString()
                : prop.propertyType == SerializedPropertyType.Color
                ? prop.colorValue.ToString()
                : "?";
        }

        // Do a SetTexture with a dummy texture
        var dummy = new Texture2D(4, 4);
        clone.SetTexture("_MainTex", dummy);

        var soAfter = new SerializedObject(clone);
        var diffs = new System.Collections.Generic.List<string>();
        foreach (var kv in snapshotBefore)
        {
            var afterProp = soAfter.FindProperty(kv.Key);
            if (afterProp == null) continue;
            string afterVal = afterProp.propertyType == SerializedPropertyType.Float
                ? afterProp.floatValue.ToString("R")
                : afterProp.propertyType == SerializedPropertyType.Integer
                ? afterProp.intValue.ToString()
                : afterProp.propertyType == SerializedPropertyType.Boolean
                ? afterProp.boolValue.ToString()
                : afterProp.propertyType == SerializedPropertyType.Color
                ? afterProp.colorValue.ToString()
                : "?";
            if (kv.Value != afterVal && kv.Value != "?")
                diffs.Add($"CHANGED: {kv.Key} {kv.Value} -> {afterVal}");
        }

        Object.DestroyImmediate(clone);
        Object.DestroyImmediate(dummy);
        if (diffs.Count > 0)
            Assert.Fail($"SetTexture changed {diffs.Count} non-texture properties:\n" + string.Join("\n", diffs));
    }
}
