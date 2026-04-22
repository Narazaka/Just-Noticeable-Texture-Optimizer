using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.GpuPipeline;
using Narazaka.VRChat.Jnto.Editor.Phase2.Tiling;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Gate
{
    /// <summary>
    /// Hessian + Frangi 風リッジ強度に基づく線状模様保存メトリクス。
    /// Compute Shader で各タイル毎に ridgeness の差分を計算し、MSSL の補助として機能する。
    /// </summary>
    public class RidgeMetric : IMetric
    {
        public string Name => "Ridge";
        public MetricContext Context => MetricContext.Downscale;

        public void Evaluate(
            RenderTexture orig, RenderTexture candidate,
            UvTileGrid grid, float[] rPerTile,
            DegradationCalibration calib, float[] scoresOut)
        {
            var cs = ComputeResources.Load("Ridge");
            int k = cs.FindKernel("CSEvaluate");

            int tileCount = grid.Tiles.Length;
            var scoreBuf = new ComputeBuffer(tileCount, sizeof(float));
            scoreBuf.SetData(new float[tileCount]);
            var rBuf = new ComputeBuffer(tileCount, sizeof(float));
            rBuf.SetData(rPerTile);

            try
            {
                cs.SetTexture(k, "_Orig", orig);
                cs.SetTexture(k, "_Candidate", candidate);
                cs.SetBuffer(k, "_Scores", scoreBuf);
                cs.SetBuffer(k, "_RPerTile", rBuf);
                cs.SetInt("_TilesX", grid.TilesX);
                cs.SetInt("_TilesY", grid.TilesY);
                cs.SetInt("_TileSize", grid.TileSize);
                cs.SetInt("_TextureWidth", grid.TextureWidth);
                cs.SetInt("_TextureHeight", grid.TextureHeight);
                cs.SetFloat("_RidgeScale", calib.RidgeScale);

                int gx = Mathf.CeilToInt(grid.TilesX / 8f);
                int gy = Mathf.CeilToInt(grid.TilesY / 8f);
                cs.Dispatch(k, gx, gy, 1);

                scoreBuf.GetData(scoresOut);
            }
            finally
            {
                scoreBuf.Release();
                rBuf.Release();
            }
        }
    }
}
