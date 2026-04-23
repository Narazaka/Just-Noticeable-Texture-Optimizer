using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.GpuPipeline;
using Narazaka.VRChat.Jnto.Editor.Phase2.Tiling;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Gate
{
    /// <summary>
    /// ノーマルマップの per-tile 角度差メトリクス (GPU 実装)。
    /// 各ピクセルで RG → tangent space normal をデコードし、dot product から角度を算出。
    /// タイル内 N*N サンプルの max angle を採用 (99%ile の保守版)。
    /// </summary>
    public class NormalAngleMetric : IMetric
    {
        public string Name => "NormalAngle";
        public MetricContext Context => MetricContext.Both;

        /// <summary>0 = standard RG, 1 = DXT5nm AG (X in Alpha, Y in Green)</summary>
        public int OrigChannelMapping;
        /// <summary>0 = standard RG, 1 = DXT5nm AG (X in Alpha, Y in Green)</summary>
        public int CandChannelMapping;

        public void Evaluate(
            RenderTexture orig, RenderTexture candidate,
            UvTileGrid grid, float[] rPerTile,
            DegradationCalibration calib, float[] scoresOut)
        {
            var cs = ComputeResources.Load("NormalAngle");
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
                cs.SetFloat("_NormalAngleScale", calib.NormalAngleScale);
                cs.SetInt("_OrigChannelMapping", OrigChannelMapping);
                cs.SetInt("_CandChannelMapping", CandChannelMapping);

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
