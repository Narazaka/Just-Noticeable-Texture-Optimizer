namespace Narazaka.VRChat.Jnto.Editor.Phase2.Compression
{
    /// <summary>
    /// shader がテクスチャをどう使うかの分類。
    /// 候補 fmt 決定の入力。
    /// </summary>
    public enum ShaderUsage
    {
        /// <summary>通常の color テクスチャ (RGB / RGBA)。</summary>
        Color,
        /// <summary>normal map として sample される (UnpackNormal 等)。</summary>
        Normal,
        /// <summary>single channel mask として sample される。</summary>
        SingleChannel,
    }
}
