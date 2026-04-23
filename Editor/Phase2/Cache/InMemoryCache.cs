using System;
using System.Collections.Generic;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.GpuPipeline;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Cache
{
    /// <summary>
    /// 1 回の NDMF ビルド実行中に使い回すオブジェクト群。
    /// テクスチャ単位の GpuTextureContext (RT chain) を保持。
    /// Dispose で RT を解放。
    /// R-D4-2 で BlockStats 保持は不要になったため削除。
    /// </summary>
    public class InMemoryCache : IDisposable
    {
        public readonly Dictionary<Texture2D, GpuTextureContext> Contexts = new();

        public void Dispose()
        {
            foreach (var ctx in Contexts.Values)
            {
                if (ctx != null) ctx.Dispose();
            }
            Contexts.Clear();
        }
    }
}
