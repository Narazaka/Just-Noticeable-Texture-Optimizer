using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Complexity;

public class SobelComplexityStrategyTests
{
    [Test]
    public void Uniform_ReturnsNearZero()
    {
        var s = ScriptableObject.CreateInstance<SobelComplexityStrategy>();
        var px = new Color[64];
        for (int i = 0; i < px.Length; i++) px[i] = Color.gray;
        Assert.Less(s.Measure(px, 8, 8), 0.05f);
    }

    [Test]
    public void HighContrastStripes_ReturnsHigh()
    {
        var s = ScriptableObject.CreateInstance<SobelComplexityStrategy>();
        var px = new Color[64];
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
                px[y*8+x] = ((x / 2) % 2 == 0) ? Color.black : Color.white;
        Assert.Greater(s.Measure(px, 8, 8), 0.3f);
    }
}
