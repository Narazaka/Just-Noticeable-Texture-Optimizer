using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Reporting
{
    public class DecisionRecord
    {
        public Texture2D OriginalTexture;
        public int OrigSize;
        public int FinalSize;
        public TextureFormat OrigFormat;
        public TextureFormat FinalFormat;
        public long SavedBytes;
        public float TextureScore;
        public string DominantMetric;
        public int DominantMipLevel;
        public int WorstTileIndex;
        public bool CacheHit;
        public float ProcessingMs;
        public string Reason;
    }
}
