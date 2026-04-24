namespace Narazaka.VRChat.Jnto.Editor.Phase2.Compression
{
    /// <summary>
    /// 粗粒度な texture role。Phase2Pipeline は <see cref="ShaderUsage"/> + alphaUsed で
    /// fmt 候補を決定するため、本 enum は CacheKeyBuilder の互換のためだけに残してある。
    /// </summary>
    public enum TextureRole { ColorOpaque, ColorAlpha, NormalMap, SingleChannel, MatCapOrLut }
}
