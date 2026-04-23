using System.Collections.Generic;
using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Compression
{
    /// <summary>
    /// 「正しい候補 fmt 集合」を元 fmt + shader usage + α 必要性 から導出する。
    /// </summary>
    public static class FormatCandidateSelector
    {
        /// <summary>
        /// 候補 fmt の重複なしリストを返す。常に元 fmt を含む (no-op 候補)。
        /// </summary>
        public static List<TextureFormat> Select(TextureFormat originalFormat, ShaderUsage usage, bool alphaUsed)
        {
            var result = new List<TextureFormat>();
            void Add(TextureFormat f) { if (!result.Contains(f)) result.Add(f); }

            // ケース判定
            switch (originalFormat)
            {
                // A: Normal-encoding 形式
                case TextureFormat.BC5:
                case TextureFormat.RG16:
                    Add(originalFormat);
                    Add(TextureFormat.BC5);
                    Add(TextureFormat.BC7);
                    return result;

                // B: Single-channel 形式
                case TextureFormat.BC4:
                case TextureFormat.R8:
                case TextureFormat.Alpha8:
                    Add(originalFormat);
                    Add(TextureFormat.BC4);
                    Add(TextureFormat.BC7);
                    return result;

                // C: α 物理的無し RGB
                case TextureFormat.RGB24:
                case TextureFormat.BC6H:
                    Add(originalFormat);
                    if (usage == ShaderUsage.Color)
                    {
                        Add(TextureFormat.DXT1);
                        Add(TextureFormat.BC7);
                    }
                    // Normal/SingleChannel: 元 fmt 固定 (data を normal/single-ch に再エンコ不可)
                    return result;

                // D: DXT1 (1bit α)
                case TextureFormat.DXT1:
                case TextureFormat.DXT1Crunched:
                    Add(TextureFormat.DXT1);
                    if (usage == ShaderUsage.Color)
                    {
                        Add(TextureFormat.BC7);
                    }
                    // Normal/SingleChannel: DXT1 のまま (size のみ縮小可)
                    return result;

                // E: α 持つ汎用
                case TextureFormat.BC7:
                case TextureFormat.DXT5:
                case TextureFormat.DXT5Crunched:
                case TextureFormat.RGBA32:
                case TextureFormat.ARGB32:
                case TextureFormat.BGRA32:
                    if (usage == ShaderUsage.Normal)
                    {
                        Add(originalFormat);
                        Add(TextureFormat.BC5);
                        Add(TextureFormat.BC7);
                    }
                    else if (usage == ShaderUsage.SingleChannel)
                    {
                        Add(originalFormat);
                        Add(TextureFormat.BC4);
                        Add(TextureFormat.BC7);
                    }
                    else // Color
                    {
                        if (alphaUsed)
                        {
                            // α 使用: DXT5/BC7 (DXT1 では α 失う)
                            Add(TextureFormat.DXT5);
                            Add(TextureFormat.BC7);
                        }
                        else
                        {
                            // α 不使用: DXT1/BC7 (DXT5 は冗長で除外)
                            Add(TextureFormat.DXT1);
                            Add(TextureFormat.BC7);
                        }
                    }
                    return result;

                // F: その他 (RGBAFloat, RGBAHalf, R16, RGB565 等)
                default:
                    Add(originalFormat);
                    Add(TextureFormat.BC7);
                    return result;
            }
        }
    }
}
