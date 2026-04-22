using System;
using System.Collections.Generic;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Compression
{
    /// <summary>
    /// サイズ降順二分探索。
    /// probe(size) が「その size で pass か」を返す delegate。
    /// origSize から minSize までの power-of-2 候補のうち、最小 pass サイズを返す。
    /// 全 fail なら origSize、全 pass なら minSize。
    /// </summary>
    public static class BinarySearchStrategy
    {
        public static int FindMinPassSize(int origSize, int minSize, Func<int, bool> probe)
        {
            var candidates = new List<int>();
            int s = origSize;
            while (s >= minSize)
            {
                candidates.Add(s);
                s /= 2;
            }
            // ascending order: minSize, ..., origSize
            candidates.Sort();

            int lo = 0, hi = candidates.Count - 1;
            int bestPass = origSize;
            bool foundPass = false;
            bool probedMin = false;

            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                int size = candidates[mid];
                if (mid == 0) probedMin = true;
                if (probe(size))
                {
                    bestPass = size;
                    foundPass = true;
                    hi = mid - 1;
                }
                else
                {
                    lo = mid + 1;
                }
            }

            // Fallback: non-monotone probe safety check at minSize.
            // If the binary search found no passing size and minSize was never probed,
            // try it directly so we can still report the minimum passing size.
            if (!foundPass && !probedMin && candidates.Count > 0)
            {
                if (probe(candidates[0]))
                {
                    bestPass = candidates[0];
                }
            }

            return bestPass;
        }
    }
}
