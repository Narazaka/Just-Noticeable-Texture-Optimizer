using NUnit.Framework;
using Narazaka.VRChat.Jnto.Editor.Shared;

public class LilToonShaderIdentifierTests
{
    [TestCase("Packages/jp.lilxyzw.liltoon/Shader/lts.shader",                      "lts")]
    [TestCase("Packages/jp.lilxyzw.liltoon/Shader/lts_fur.shader",                  "lts_fur")]
    [TestCase("Packages/jp.lilxyzw.liltoon/Shader/ltsmulti_ref.shader",             "ltsmulti_ref")]
    [TestCase("Packages/jp.lilxyzw.liltoon/Shader/ltspass_opaque.shader",           "ltspass_opaque")]
    [TestCase("Packages/jp.lilxyzw.liltoon/Shader/ltsother_bakeramp.shader",        "ltsother_bakeramp")]
    [TestCase(@"Packages\jp.lilxyzw.liltoon\Shader\lts.shader",                     "lts")]
    [TestCase("Packages/jp.sigmal00.uzumore-shader/Runtime/Shaders/lts.lilcontainer","lts")]
    [TestCase("Packages/jp.sigmal00.uzumore-shader/Runtime/Shaders/lts_fur.lilcontainer","lts_fur")]
    [TestCase("Packages/com.example.other/Something/lts.lilcontainer",              "lts")]
    public void TryGetVariantIdFromPath_Recognized(string path, string expected)
    {
        Assert.AreEqual(expected, LilToonShaderIdentifier.TryGetVariantIdFromPath(path));
    }

    [TestCase("Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.shader")]
    [TestCase("Assets/MyShader.shader")]
    [TestCase("Packages/jp.lilxyzw.liltoon/Editor/lilToonSetting.cs")]
    [TestCase("")]
    [TestCase(null)]
    public void TryGetVariantIdFromPath_NotLilToonFamily_ReturnsNull(string path)
    {
        Assert.IsNull(LilToonShaderIdentifier.TryGetVariantIdFromPath(path));
    }

    [Test]
    public void TryGetVariantId_NullShader_ReturnsNull()
    {
        Assert.IsNull(LilToonShaderIdentifier.TryGetVariantId(null));
    }
}
