using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.GpuPipeline
{
    /// <summary>
    /// 任意サイズの RenderTexture (ARGB32 + mipmap chain) を作成して
    /// 入力 Texture から Blit + GenerateMips するラッパー。
    /// 呼び出し側は Release/DestroyImmediate の責任を負う。
    /// </summary>
    public static class PyramidBuilder
    {
        public static RenderTexture CreatePyramid(Texture source, int width, int height, string debugName, bool isLinear = false)
        {
            var desc = new RenderTextureDescriptor(width, height, RenderTextureFormat.ARGB32, 0)
            {
                useMipMap = true,
                autoGenerateMips = false,
                // バグ#2 回帰防止: NormalMap/SingleChannel role (isLinear=true) では
                // sRGB=false にして linear 値を保持する。
                sRGB = !isLinear,
            };
            var rt = new RenderTexture(desc) { name = debugName, filterMode = FilterMode.Trilinear };
            rt.Create();
            Graphics.Blit(source, rt);
            rt.GenerateMips();
            return rt;
        }

        public static int MipLevelCount(int width, int height)
        {
            int maxDim = Mathf.Max(width, height);
            int levels = 0;
            while (maxDim > 0)
            {
                levels++;
                maxDim >>= 1;
            }
            return levels;
        }
    }
}
