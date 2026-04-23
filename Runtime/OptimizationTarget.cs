namespace Narazaka.VRChat.Jnto
{
    public enum OptimizationTarget
    {
        /// <summary>GPU メモリ (VRAM) 使用量の最小化を優先。raw bytes 基準でソート。</summary>
        VRAM,
        /// <summary>アセットバンドル圧縮後のダウンロード容量の最小化を優先。推定事後圧縮サイズ基準でソート。</summary>
        Download,
    }
}
