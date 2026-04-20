using UnityEditor;
using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Phase1
{
    public static class TextureReferenceCollector
    {
        public static TextureReferenceGraph Collect(GameObject avatarRoot)
        {
            var g = new TextureReferenceGraph();

            foreach (var r in avatarRoot.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var mat in r.sharedMaterials)
                {
                    if (mat == null || mat.shader == null) continue;
                    CollectFromMaterial(g, mat, r);
                }
            }

            foreach (var anim in avatarRoot.GetComponentsInChildren<Animator>(true))
            {
                if (anim.runtimeAnimatorController == null) continue;
                CollectFromController(g, anim.runtimeAnimatorController);
            }

            return g;
        }

        static void CollectFromMaterial(TextureReferenceGraph g, Material mat, Renderer ctx)
        {
            int count = ShaderUtil.GetPropertyCount(mat.shader);
            for (int i = 0; i < count; i++)
            {
                if (ShaderUtil.GetPropertyType(mat.shader, i) != ShaderUtil.ShaderPropertyType.TexEnv) continue;
                var name = ShaderUtil.GetPropertyName(mat.shader, i);
                var tex = mat.GetTexture(name);
                g.Add(tex, new TextureReference { Material = mat, PropertyName = name, RendererContext = ctx });
            }
        }

        static void CollectFromController(TextureReferenceGraph g, RuntimeAnimatorController rac)
        {
            foreach (var clip in rac.animationClips)
            {
                var objBindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
                foreach (var b in objBindings)
                {
                    var keys = AnimationUtility.GetObjectReferenceCurve(clip, b);
                    foreach (var k in keys)
                    {
                        if (k.value is Material m) CollectFromMaterial(g, m, null);
                        if (k.value is Texture t)
                        {
                            var propName = ExtractMaterialProperty(b.propertyName);
                            if (propName != null)
                            {
                                g.Add(t, new TextureReference { Material = null, PropertyName = propName, RendererContext = null });
                            }
                        }
                    }
                }
            }
        }

        static string ExtractMaterialProperty(string bindingPropName)
        {
            if (string.IsNullOrEmpty(bindingPropName)) return null;
            const string prefix = "material.";
            if (!bindingPropName.StartsWith(prefix)) return null;
            var tail = bindingPropName.Substring(prefix.Length);
            var dot = tail.IndexOf('.');
            return dot < 0 ? tail : tail.Substring(0, dot);
        }
    }
}
