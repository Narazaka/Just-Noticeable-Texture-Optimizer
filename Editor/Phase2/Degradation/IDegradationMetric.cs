using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Degradation
{
    public interface IDegradationMetric
    {
        string Name { get; }
        float Evaluate(Texture2D original, Texture2D candidate);
    }

    public interface IPerPixelMetric : IDegradationMetric
    {
        float[] EvaluatePerPixel(Texture2D original, Texture2D candidate);
    }
}
