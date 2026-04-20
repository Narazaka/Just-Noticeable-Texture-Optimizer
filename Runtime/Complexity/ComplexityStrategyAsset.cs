using UnityEngine;

namespace Narazaka.VRChat.Jnto.Complexity
{
    public abstract class ComplexityStrategyAsset : ScriptableObject, IComplexityStrategy
    {
        public abstract float Measure(Color[] region, int width, int height);
    }
}
