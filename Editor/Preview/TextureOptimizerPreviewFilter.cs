using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using nadena.dev.ndmf.preview;
using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Preview
{
    public class TextureOptimizerPreviewFilter : IRenderFilter
    {
        public static TogglablePreviewNode Toggle = TogglablePreviewNode.Create(
            () => "Just-Noticeable Texture Optimizer: Preview Optimization",
            "net.narazaka.vrchat.jnto/preview",
            initialState: false);

        public IEnumerable<TogglablePreviewNode> GetPreviewControlNodes() { yield return Toggle; }
        public bool IsEnabled(ComputeContext ctx) => ctx.Observe(Toggle.IsEnabled);

        public ImmutableList<RenderGroup> GetTargetGroups(ComputeContext ctx)
        {
            return ImmutableList<RenderGroup>.Empty;
        }

        public Task<IRenderFilterNode> Instantiate(RenderGroup group, IEnumerable<(Renderer, Renderer)> proxyPairs, ComputeContext ctx)
        {
            return Task.FromResult<IRenderFilterNode>(new PreviewNode());
        }

        class PreviewNode : IRenderFilterNode
        {
            public RenderAspects WhatChanged => RenderAspects.Material;
            public void Dispose() { }
        }
    }
}
