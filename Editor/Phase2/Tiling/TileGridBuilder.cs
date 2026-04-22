using System.Collections.Generic;
using UnityEngine;
using Narazaka.VRChat.Jnto;
using Narazaka.VRChat.Jnto.Editor.Phase2;
using Narazaka.VRChat.Jnto.Editor.Resolution;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Tiling
{
    public static class TileGridBuilder
    {
        /// <summary>
        /// テクスチャ 1 枚に対し、そのテクスチャを参照する全 renderer の mesh 三角形を
        /// 1 つの UvTileGrid に統合集計する。renderer 毎に ResolvedSettings の BoneWeights を
        /// 使ってボーン重要度を算出。
        /// </summary>
        public static UvTileGrid Build(
            int textureWidth, int textureHeight,
            IEnumerable<(Renderer renderer, Mesh mesh)> sources,
            Dictionary<Transform, BoneCategory> bonemap,
            IReadOnlyDictionary<Renderer, ResolvedSettings> settingsByRenderer)
        {
            var grid = UvTileGrid.Create(textureWidth, textureHeight);
            if (sources == null) return grid;

            foreach (var src in sources)
            {
                if (src.renderer == null || src.mesh == null) continue;
                if (settingsByRenderer == null) continue;
                if (!settingsByRenderer.TryGetValue(src.renderer, out var s)) continue;
                TileRasterizer.Accumulate(grid, src.renderer, src.mesh, bonemap, s.BoneWeights);
            }
            return grid;
        }
    }
}
