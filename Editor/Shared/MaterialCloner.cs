using System.Collections.Generic;
using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Shared
{
    public static class MaterialCloner
    {
        public static void EnsureCloned(Renderer renderer, int materialIndex, Dictionary<Material, Material> cloneMap)
        {
            var mats = renderer.sharedMaterials;
            var orig = mats[materialIndex];
            if (orig == null || cloneMap.ContainsValue(orig)) return;
            if (!cloneMap.TryGetValue(orig, out var clone))
            {
                clone = new Material(orig);
                clone.name = orig.name;
                cloneMap[orig] = clone;
            }
            mats[materialIndex] = clone;
            renderer.sharedMaterials = mats;
        }

        public static void ReplaceOnRenderers(
            GameObject root,
            Dictionary<Material, Material> cloneMap,
            System.Func<Material, bool> shouldClone)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    if (cloneMap.TryGetValue(mats[i], out var clone))
                    {
                        mats[i] = clone;
                        changed = true;
                    }
                    else if (shouldClone(mats[i]))
                    {
                        clone = new Material(mats[i]);
                        clone.name = mats[i].name;
                        cloneMap[mats[i]] = clone;
                        mats[i] = clone;
                        changed = true;
                    }
                }
                if (changed) r.sharedMaterials = mats;
            }
        }
    }
}
