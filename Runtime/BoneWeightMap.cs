using System;
using System.Collections.Generic;
using UnityEngine;

namespace Narazaka.VRChat.Jnto
{
    public enum BoneCategory { Head, Neck, Hand, Chest, UpperArm, Spine, Hips, UpperLeg, LowerLeg, Foot, Toe, Other }

    [Serializable]
    public struct BoneWeightEntry
    {
        public BoneCategory Category;
        [Range(0f, 2f)] public float Weight;
    }

    [Serializable]
    public class BoneWeightMap
    {
        public List<BoneWeightEntry> Entries = new List<BoneWeightEntry>();

        public static BoneWeightMap Default() => new BoneWeightMap
        {
            Entries = new List<BoneWeightEntry>
            {
                new BoneWeightEntry { Category = BoneCategory.Head,      Weight = 1.0f },
                new BoneWeightEntry { Category = BoneCategory.Neck,      Weight = 1.0f },
                new BoneWeightEntry { Category = BoneCategory.Hand,      Weight = 1.0f },
                new BoneWeightEntry { Category = BoneCategory.Chest,     Weight = 0.7f },
                new BoneWeightEntry { Category = BoneCategory.UpperArm,  Weight = 0.7f },
                new BoneWeightEntry { Category = BoneCategory.Spine,     Weight = 0.7f },
                new BoneWeightEntry { Category = BoneCategory.Hips,      Weight = 0.5f },
                new BoneWeightEntry { Category = BoneCategory.UpperLeg,  Weight = 0.5f },
                new BoneWeightEntry { Category = BoneCategory.LowerLeg,  Weight = 0.3f },
                new BoneWeightEntry { Category = BoneCategory.Foot,      Weight = 0.3f },
                new BoneWeightEntry { Category = BoneCategory.Toe,       Weight = 0.3f },
                new BoneWeightEntry { Category = BoneCategory.Other,     Weight = 0.5f },
            },
        };

        public float Get(BoneCategory c)
        {
            foreach (var e in Entries) if (e.Category == c) return e.Weight;
            return 0.5f;
        }
    }
}
