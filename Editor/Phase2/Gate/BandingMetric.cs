using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.GpuPipeline;
using Narazaka.VRChat.Jnto.Editor.Phase2.Tiling;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Gate
{
    /// <summary>
    /// 圧縮 (BC1 / DXT5) 後の平坦グラデーション領域に出る階段化アーチファクトを per-tile で検出する。
    /// Compute Shader で平坦ピクセルを抽出し、2 次差分ヒストグラムのゼロ付近ピーク比を JND 単位で返す。
    /// </summary>
    public class BandingMetric : IMetric
    {
        public string Name => "Banding";
        public MetricContext Context => MetricContext.Compression;

        public void Evaluate(
            RenderTexture orig, RenderTexture candidate,
            UvTileGrid grid, float[] rPerTile,
            DegradationCalibration calib, float[] scoresOut)
        {
            var cs = ComputeResources.Load("Banding");
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
                cs.SetFloat("_BandingScale", calib.BandingScale);

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
