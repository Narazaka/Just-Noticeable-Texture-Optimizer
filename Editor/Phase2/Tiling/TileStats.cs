namespace Narazaka.VRChat.Jnto.Editor.Phase2.Tiling
{
    public struct TileStats
    {
        /// <summary>タイル内三角形の worldArea / uvArea の最大値 (cm² / uv²)。</summary>
        public float Density;
        /// <summary>タイル内三角形のボーン重要度の最大値。</summary>
        public float BoneWeight;
        /// <summary>このタイルに属する三角形が存在するか。</summary>
        public bool HasCoverage;
    }
}
