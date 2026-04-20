using UnityEditor;
using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor
{
    [CustomEditor(typeof(TextureOptimizer))]
    public class TextureOptimizerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var tc = (TextureOptimizer)target;
            bool hasAncestor = HasAncestorOptimizer(tc);

            serializedObject.Update();
            var modeProp = serializedObject.FindProperty("Mode");
            using (new EditorGUI.DisabledScope(!hasAncestor))
            {
                EditorGUILayout.PropertyField(modeProp);
                if (!hasAncestor && modeProp.enumValueIndex == (int)TextureOptimizerMode.Override)
                {
                    modeProp.enumValueIndex = (int)TextureOptimizerMode.Root;
                }
            }

            bool isRoot = modeProp.enumValueIndex == (int)TextureOptimizerMode.Root;
            DrawOverrideField("Preset", isRoot);
            DrawOverrideField("ViewDistanceCm", isRoot);
            DrawOverrideField("BoneWeights", isRoot);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("ComplexityStrategy"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("ExcludeList"), true);

            serializedObject.ApplyModifiedProperties();
        }

        void DrawOverrideField(string name, bool forceHasValue)
        {
            var p = serializedObject.FindProperty(name);
            var hv = p.FindPropertyRelative("HasValue");
            if (forceHasValue) hv.boolValue = true;

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(forceHasValue))
            {
                EditorGUILayout.PropertyField(hv, new GUIContent(name), GUILayout.Width(EditorGUIUtility.labelWidth + 20));
            }
            using (new EditorGUI.DisabledScope(!hv.boolValue))
            {
                EditorGUILayout.PropertyField(p.FindPropertyRelative("Value"), GUIContent.none);
            }
            EditorGUILayout.EndHorizontal();
        }

        static bool HasAncestorOptimizer(TextureOptimizer self)
        {
            for (var t = self.transform.parent; t != null; t = t.parent)
                if (t.GetComponent<TextureOptimizer>() != null) return true;
            return false;
        }
    }
}
