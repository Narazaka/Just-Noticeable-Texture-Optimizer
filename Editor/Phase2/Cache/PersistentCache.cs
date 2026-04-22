using System;
using System.IO;
using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Cache
{
    [Serializable]
    public class CachedTextureResult
    {
        public int FinalSize;          // backward compat: 旧フォーマット (= max(W, H))
        public int FinalWidth;         // バグ#11 回帰防止: 非正方形テクスチャの W/H を別々に保持
        public int FinalHeight;
        public string FinalFormatName; // TextureFormat enum string
        public byte[] CompressedRawBytes;
    }

    public static class PersistentCache
    {
        public static string RootPath => Path.Combine("Library", "JntoCache");
        public static string BlobsPath => Path.Combine(RootPath, "blobs");

        public static CachedTextureResult TryLoad(CacheKey key, CacheMode mode)
        {
            if (mode == CacheMode.Disabled) return null;
            var metaPath = Path.Combine(RootPath, key.ToString() + ".json");
            if (!File.Exists(metaPath)) return null;
            try
            {
                var json = File.ReadAllText(metaPath);
                var r = JsonUtility.FromJson<CachedTextureResult>(json);
                if (r == null) return null;

                // バグ#11 回帰防止: レガシー cache (FinalWidth/Height 未書込) は square と仮定して補完
                if (r.FinalWidth == 0 && r.FinalHeight == 0 && r.FinalSize > 0)
                {
                    r.FinalWidth = r.FinalSize;
                    r.FinalHeight = r.FinalSize;
                }

                if (mode == CacheMode.Full
                    && !string.IsNullOrEmpty(r.FinalFormatName)
                    && (r.CompressedRawBytes == null || r.CompressedRawBytes.Length == 0))
                {
                    var blobPath = Path.Combine(BlobsPath, key.ToString() + ".bin");
                    if (File.Exists(blobPath))
                        r.CompressedRawBytes = File.ReadAllBytes(blobPath);
                }
                return r;
            }
            catch
            {
                return null;
            }
        }

        public static void Store(CacheKey key, CachedTextureResult value, CacheMode mode)
        {
            if (mode == CacheMode.Disabled || value == null) return;
            Directory.CreateDirectory(RootPath);

            var metaPath = Path.Combine(RootPath, key.ToString() + ".json");
            byte[] rawBytes = value.CompressedRawBytes;

            if (mode == CacheMode.Compact)
            {
                value.CompressedRawBytes = null;
            }
            else if (rawBytes != null && rawBytes.Length > 0)
            {
                Directory.CreateDirectory(BlobsPath);
                var blobPath = Path.Combine(BlobsPath, key.ToString() + ".bin");
                File.WriteAllBytes(blobPath, rawBytes);
                // store null in JSON to keep JSON small
                value.CompressedRawBytes = null;
            }

            try
            {
                var json = JsonUtility.ToJson(value);
                File.WriteAllText(metaPath, json);
            }
            finally
            {
                value.CompressedRawBytes = rawBytes;
            }
        }

        public static void ClearAll()
        {
            if (Directory.Exists(RootPath))
                Directory.Delete(RootPath, true);
        }
    }
}
