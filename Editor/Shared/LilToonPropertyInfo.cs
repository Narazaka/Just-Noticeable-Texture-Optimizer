using System;
using Narazaka.VRChat.Jnto.Editor.Phase2.Compression;

namespace Narazaka.VRChat.Jnto.Editor.Shared
{
    /// <summary>
    /// シェーダーがテクスチャのどの channel を参照するか。
    /// 事実値 (.hlsl 読みで判定) として <see cref="LilToonPropertyInfo.ReadChannels"/> に格納される。
    /// </summary>
    [Flags]
    public enum ChannelMask
    {
        None = 0,
        R = 1 << 0,
        G = 1 << 1,
        B = 1 << 2,
        A = 1 << 3,
        RGB  = R | G | B,
        RGBA = R | G | B | A,
    }

    /// <summary>
    /// lilToon プロパティ 1 エントリ分の分類情報。
    /// </summary>
    public readonly struct LilToonPropertyInfo
    {
        public readonly ShaderUsage Usage;
        public readonly ChannelMask ReadChannels;
        public readonly string EvidenceRef;

        public LilToonPropertyInfo(ShaderUsage usage, ChannelMask readChannels, string evidenceRef)
        {
            Usage = usage;
            ReadChannels = readChannels;
            EvidenceRef = evidenceRef;
        }

        public bool AlphaUsed => (ReadChannels & ChannelMask.A) != 0;
    }
}
