namespace Narazaka.VRChat.Jnto.Editor.Phase2.Compression
{
    /// <summary>
    /// 旧パイプライン (Phase2Pipeline / CompressionChain / DegradationGate) が参照する
    /// 粗粒度な texture role。
    /// 新パイプライン (NewPhase2Pipeline) は <see cref="ShaderUsage"/> + alphaUsed で
    /// fmt 候補を決定するため、この enum は互換のためだけに残してある。
    /// </summary>
    public enum TextureRole { ColorOpaque, ColorAlpha, NormalMap, SingleChannel, MatCapOrLut }
}
