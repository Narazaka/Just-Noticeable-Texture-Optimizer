using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.Tiling;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Gate
{
    public enum MetricContext
    {
        /// <summary>ダウンサンプル前後の比較で使うメトリクス。</summary>
        Downscale,
        /// <summary>圧縮前後の比較で使うメトリクス。</summary>
        Compression,
        /// <summary>両方で使う。</summary>
        Both,
    }

    /// <summary>
    /// per-tile スコアを返す JND 正規化メトリクス。
    /// 各実装は orig/candidate RT を比較し、scoresOut[i] にタイル i の JND スコアを書き込む。
    /// </summary>
    public interface IMetric
    {
        string Name { get; }
        MetricContext Context { get; }

        /// <summary>
        /// orig/candidate RT を比較し、タイルごとの JND スコアを scoresOut に書き込む。
        /// scoresOut の長さは grid.Tiles.Length と一致すること。
        /// rPerTile[i] が 0 のタイル (coverage 無し) は scoresOut[i] = 0 とすること。
        /// </summary>
        void Evaluate(
            RenderTexture orig,
            RenderTexture candidate,
            UvTileGrid grid,
            float[] rPerTile,
            DegradationCalibration calib,
            float[] scoresOut);
    }
}
