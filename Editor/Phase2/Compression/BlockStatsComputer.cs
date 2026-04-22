using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.GpuPipeline;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Compression
{
    public struct BlockStats
    {
        public float Planarity;
        public float Nonlinearity;
        public float AlphaNonlinearity;
        public float MeanR;
        public float MeanG;
        public float MeanB;
    }

    public static class BlockStatsComputer
    {
        public static BlockStats[] Compute(RenderTexture source, int width, int height)
        {
            int bx = Mathf.Max(1, (width + 3) / 4);
            int by = Mathf.Max(1, (height + 3) / 4);
            int total = bx * by;
            var buf = new ComputeBuffer(total, sizeof(float) * 6);
            buf.SetData(new float[total * 6]);

            try
            {
                var cs = ComputeResources.Load("BlockStats");
                int k = cs.FindKernel("CSMain");
                cs.SetTexture(k, "_Source", source);
                cs.SetBuffer(k, "_Stats", buf);
                cs.SetInt("_Width", width);
                cs.SetInt("_Height", height);
                cs.SetInt("_BlocksX", bx);
                cs.SetInt("_BlocksY", by);
                cs.Dispatch(k, Mathf.CeilToInt(bx / 8f), Mathf.CeilToInt(by / 8f), 1);

                var raw = new float[total * 6];
                buf.GetData(raw);

                var result = new BlockStats[total];
                for (int i = 0; i < total; i++)
                {
                    int o = i * 6;
                    result[i] = new BlockStats
                    {
                        Planarity = raw[o + 0],
                        Nonlinearity = raw[o + 1],
                        AlphaNonlinearity = raw[o + 2],
                        MeanR = raw[o + 3],
                        MeanG = raw[o + 4],
                        MeanB = raw[o + 5],
                    };
                }
                return result;
            }
            finally
            {
                buf.Release();
            }
        }
    }
}
