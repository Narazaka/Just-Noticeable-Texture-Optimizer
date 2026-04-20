using System.Collections.Generic;
using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Phase2
{
    public static class BoneClassifier
    {
        public static Dictionary<Transform, BoneCategory> ClassifyHumanoid(Animator animator)
        {
            var dict = new Dictionary<Transform, BoneCategory>();
            if (animator == null || !animator.isHuman) return dict;

            AddIf(dict, animator, HumanBodyBones.Head, BoneCategory.Head);
            AddIf(dict, animator, HumanBodyBones.Neck, BoneCategory.Neck);
            foreach (var b in new[] { HumanBodyBones.LeftHand, HumanBodyBones.RightHand })
                AddIf(dict, animator, b, BoneCategory.Hand);
            AddIf(dict, animator, HumanBodyBones.Chest, BoneCategory.Chest);
            AddIf(dict, animator, HumanBodyBones.UpperChest, BoneCategory.Chest);
            foreach (var b in new[] { HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm,
                                       HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm })
                AddIf(dict, animator, b, BoneCategory.UpperArm);
            AddIf(dict, animator, HumanBodyBones.Spine, BoneCategory.Spine);
            AddIf(dict, animator, HumanBodyBones.Hips, BoneCategory.Hips);
            foreach (var b in new[] { HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg })
                AddIf(dict, animator, b, BoneCategory.UpperLeg);
            foreach (var b in new[] { HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg })
                AddIf(dict, animator, b, BoneCategory.LowerLeg);
            foreach (var b in new[] { HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot })
                AddIf(dict, animator, b, BoneCategory.Foot);
            foreach (var b in new[] { HumanBodyBones.LeftToes, HumanBodyBones.RightToes })
                AddIf(dict, animator, b, BoneCategory.Toe);
            return dict;
        }

        static void AddIf(Dictionary<Transform, BoneCategory> d, Animator a, HumanBodyBones h, BoneCategory c)
        {
            var t = a.GetBoneTransform(h);
            if (t != null && !d.ContainsKey(t)) d.Add(t, c);
        }

        public static BoneCategory ClassifyByName(string name)
        {
            var n = (name ?? "").ToLowerInvariant();
            if (n.Contains("toe")) return BoneCategory.Toe;
            if (n.Contains("foot")) return BoneCategory.Foot;
            if (n.Contains("lowerleg") || n.Contains("shin") || n.Contains("calf")) return BoneCategory.LowerLeg;
            if (n.Contains("upperleg") || n.Contains("thigh")) return BoneCategory.UpperLeg;
            if (n.Contains("hip")) return BoneCategory.Hips;
            if (n.Contains("chest") || n.Contains("torso")) return BoneCategory.Chest;
            if (n.Contains("upperarm") || n.Contains("lowerarm") || n.Contains("forearm") || n.Contains("shoulder")) return BoneCategory.UpperArm;
            if (n.Contains("spine")) return BoneCategory.Spine;
            if (n.Contains("hand") || n.Contains("finger") || n.Contains("thumb") || n.Contains("index") || n.Contains("middle") || n.Contains("ring") || n.Contains("pinky")) return BoneCategory.Hand;
            if (n.Contains("neck")) return BoneCategory.Neck;
            if (n.Contains("head") || n.Contains("face") || n.Contains("eye") || n.Contains("mouth")) return BoneCategory.Head;
            return BoneCategory.Other;
        }
    }
}
