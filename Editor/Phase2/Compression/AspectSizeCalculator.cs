using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Compression
{
    /// <summary>
    /// 縮小後のアスペクト維持サイズ計算。
    /// target となる最大辺と元アスペクトから、最小辺を 4 の倍数に丸めた (w, h) を返す。
    /// ブロック圧縮フォーマットが 4x4 ブロック単位のため min=4 で clamp する。
    /// </summary>
    public static class AspectSizeCalculator
    {
        public static (int width, int height) Compute(int origWidth, int origHeight, int targetMaxDim)
        {
            if (origWidth >= origHeight)
            {
                int tw = targetMaxDim;
                int th = Mathf.Max(4, RoundToMultipleOf4(
                    Mathf.RoundToInt(targetMaxDim * (float)origHeight / origWidth)));
                return (tw, th);
            }
            else
            {
                int th = targetMaxDim;
                int tw = Mathf.Max(4, RoundToMultipleOf4(
                    Mathf.RoundToInt(targetMaxDim * (float)origWidth / origHeight)));
                return (tw, th);
            }
        }

        static int RoundToMultipleOf4(int v) => ((v + 3) / 4) * 4;
    }
}
