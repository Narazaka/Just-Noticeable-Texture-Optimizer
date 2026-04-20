using NUnit.Framework;
using Narazaka.VRChat.Jnto;
using Narazaka.VRChat.Jnto.Editor.Phase2;

public class BoneClassifierTests
{
    [Test]
    public void NameBased_Fallback_MapsHeadKeyword()
    {
        Assert.AreEqual(BoneCategory.Head, BoneClassifier.ClassifyByName("Head"));
        Assert.AreEqual(BoneCategory.Head, BoneClassifier.ClassifyByName("head_01"));
        Assert.AreEqual(BoneCategory.UpperLeg, BoneClassifier.ClassifyByName("LeftUpperLeg"));
        Assert.AreEqual(BoneCategory.Foot, BoneClassifier.ClassifyByName("RightFoot"));
        Assert.AreEqual(BoneCategory.Other, BoneClassifier.ClassifyByName("Tail_03"));
    }
}
