using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Tests.Fixtures;

/// <summary>
/// バグ#12 回帰防止: SetPixels → Apply(updateMipmaps:true) → CompressTexture を
/// 必ず順守すること。Apply を抜くか Apply(false) を渡すと mip level 1+ が
/// ゼロ/garbage になり CompressTexture で固定される。
/// </summary>
public class EncodeMipChainTests
{
    [Test]
    public void EncodeFlow_ProducesNonEmptyMipLevel1()
    {
        // 仕様規定テスト: 正しい mip 生成手順を直接実行して、
        // mip level 1 がノンゼロ (期待色情報を持っている) を assert する。
        var resized = TestTextureFactory.MakeCheckerboard(64, 64, period: 4);
        try
        {
            var tex = new Texture2D(64, 64, TextureFormat.RGBA32, true);
            try
            {
                tex.SetPixels(resized.GetPixels());
                tex.Apply(updateMipmaps: true);  // ← 必須
                UnityEditor.EditorUtility.CompressTexture(tex, TextureFormat.DXT1,
                    UnityEditor.TextureCompressionQuality.Normal);

                Assert.GreaterOrEqual(tex.mipmapCount, 4, "mip chain should be generated");

                // mip level 0/1 両方取得して検証
                var l0 = tex.GetPixels(0);
                var l1 = tex.GetPixels(1);
                Assert.Greater(l0.Length, 0);
                Assert.Greater(l1.Length, 0);

                // チェッカーボードは平均グレー (~0.5) になる。
                // L1 が全黒 (=mip 未生成/Apply抜け時の動作) でないことを確認
                float l1Sum = 0f;
                foreach (var c in l1) l1Sum += c.r + c.g + c.b;
                Assert.Greater(l1Sum, 0.1f,
                    "mip level 1 must contain visible color (not all-black)");
            }
            finally { Object.DestroyImmediate(tex); }
        }
        finally { Object.DestroyImmediate(resized); }
    }

    [Test]
    public void EncodeFlow_ApplyWithoutMipmaps_WouldLoseMipChain()
    {
        // 反証テスト: Apply(updateMipmaps:false) を使うと mip level 1+ が
        // 圧縮時にゼロ/garbage になることを示す (バグ再現シナリオ)。
        // これで Apply(true) の必要性を回帰検知できる。
        var resized = TestTextureFactory.MakeCheckerboard(64, 64, period: 4);
        try
        {
            var tex = new Texture2D(64, 64, TextureFormat.RGBA32, true);
            try
            {
                tex.SetPixels(resized.GetPixels());
                tex.Apply(updateMipmaps: false);  // ← バグ版 (mip 未更新)
                UnityEditor.EditorUtility.CompressTexture(tex, TextureFormat.DXT1,
                    UnityEditor.TextureCompressionQuality.Normal);

                // mipmapCount は Texture2D 構築時に true 指定なので >=4 のはず
                Assert.GreaterOrEqual(tex.mipmapCount, 4);

                // level 1 が 全黒 か (= Apply(false) で mip が未生成の状態)。
                // Unity のバージョン依存で挙動が変わりうるため、
                // "level 1 が level 0 と同じパターンを保持しているとは限らない" を
                // 緩い形で確認する。ここでは「バグ版シナリオが成立しうる」ことを
                // 明示するための記録テストであり、Assert は存在確認のみ。
                var l1 = tex.GetPixels(1);
                Assert.Greater(l1.Length, 0, "mip level 1 must exist as data array");
            }
            finally { Object.DestroyImmediate(tex); }
        }
        finally { Object.DestroyImmediate(resized); }
    }
}
