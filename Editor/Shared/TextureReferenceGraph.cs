using System.Collections.Generic;
using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Shared
{
    public class TextureReference
    {
        public Material Material;
        public string PropertyName;
        public Renderer RendererContext;
    }

    public class TextureReferenceGraph
    {
        public Dictionary<Texture, List<TextureReference>> Map = new Dictionary<Texture, List<TextureReference>>();

        public void Add(Texture tex, TextureReference r)
        {
            if (tex == null) return;
            if (!Map.TryGetValue(tex, out var list)) Map[tex] = list = new List<TextureReference>();
            list.Add(r);
        }
    }
}
