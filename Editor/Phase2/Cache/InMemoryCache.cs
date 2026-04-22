using System;
using System.Collections.Generic;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.Compression;
using Narazaka.VRChat.Jnto.Editor.Phase2.GpuPipeline;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Cache
{
    /// <summary>
    /// 1 回の NDMF ビルド実行中に使い回すオブジェクト群。
    /// テクスチャ単位の GpuTextureContext (RT chain) と BlockStats[] を保持。
    /// Dispose で RT/buffer を解放。
    /// </summary>
    public class InMemoryCache : IDisposable
    {
        public readonly Dictionary<Texture2D, GpuTextureContext> Contexts = new();
        public readonly Dictionary<Texture2D, BlockStats[]> BlockStats = new();

        public void Dispose()
        {
            foreach (var ctx in Contexts.Values)
            {
                if (ctx != null) ctx.Dispose();
            }
            Contexts.Clear();
            BlockStats.Clear();
        }
    }
}
