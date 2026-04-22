namespace Narazaka.VRChat.Jnto
{
    public enum EncodePolicy
    {
        /// <summary>最終候補を必ず実 encode で verify。</summary>
        Safe,
        /// <summary>予測確信度が閾値超なら verify をスキップ。</summary>
        Fast,
    }
}
