using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.GpuPipeline;
using Narazaka.VRChat.Jnto.Editor.Phase2.Tiling;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Gate
{
    /// <summary>
    /// DXT5 等で α チャンネルが低 bit 量子化される影響を per-tile で検出する。
    /// タイル内の α 範囲が十分にあれば、RMSE と candidate α の level 数から JND スコアを返す。
    /// </summary>
    public class AlphaQuantizationMetric : IMetric
    {
        public string Name => "AlphaQuantization";
        public MetricContext Context => MetricContext.Compression;

        public void Evaluate(
            RenderTexture orig, RenderTexture candidate,
            UvTileGrid grid, float[] rPerTile,
            DegradationCalibration calib, float[] scoresOut)
        {
            var cs = ComputeResources.Load("AlphaQuantization");
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
                cs.SetFloat("_AlphaQuantScale", calib.AlphaQuantScale);

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
