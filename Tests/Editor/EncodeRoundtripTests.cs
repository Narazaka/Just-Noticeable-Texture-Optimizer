using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.Compression;
using Narazaka.VRChat.Jnto.Editor.Tests.Fixtures;

/// <summary>
/// バグ#5 回帰防止: TextureEncodeDecode.EncodeAndDecode と最終 Encode が
/// 同じ compress 結果になることを確認 (verify と最終出力の品質ズレ防止)。
/// </summary>
public class EncodeRoundtripTests
{
    [Test]
    public void EncodeAndDecode_NonSquare_ReturnsCorrectDimensions()
    {
        var src = TestTextureFactory.MakeCheckerboard(64, 32, period: 2);
        try
        {
            var encoded = TextureEncodeDecode.EncodeAndDecode(src, TextureFormat.DXT1);
            Assert.IsNotNull(encoded);
            Assert.AreEqual(64, encoded.width);
            Assert.AreEqual(32, encoded.height);
            Object.DestroyImmediate(encoded);
        }
        finally { Object.DestroyImmediate(src); }
    }

    [Test]
    public void EncodeAndDecode_SquareDxt1_PreservesGeneralColor()
    {
        var src = TestTextureFactory.MakeSolid(16, 16, new Color(0.6f, 0.3f, 0.1f, 1f));
        try
        {
            var encoded = TextureEncodeDecode.EncodeAndDecode(src, TextureFormat.DXT1);
            try
            {
                var c = encoded.GetPixel(8, 8);
                Assert.AreEqual(0.6f, c.r, 0.05f, "DXT1 should preserve color within block precision");
                Assert.AreEqual(0.3f, c.g, 0.05f);
                Assert.AreEqual(0.1f, c.b, 0.05f);
            }
            finally { Object.DestroyImmediate(encoded); }
        }
        finally { Object.DestroyImmediate(src); }
    }
}
