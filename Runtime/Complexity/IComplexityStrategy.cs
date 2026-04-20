using UnityEngine;

namespace Narazaka.VRChat.Jnto.Complexity
{
    public interface IComplexityStrategy
    {
        float Measure(Color[] region, int width, int height);
    }
}
