namespace Narazaka.VRChat.Jnto
{
    public enum CacheMode
    {
        /// <summary>結果 + encode 済み raw bytes を保存。</summary>
        Full,
        /// <summary>結果メタデータのみ保存、encode は都度実行。</summary>
        Compact,
        /// <summary>永続キャッシュ無効。</summary>
        Disabled,
    }
}
