using System.Collections.Generic;
using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Density
{
    public static class MeshDensityAnalyzer
    {
        public static List<MeshDensityStats> Analyze(
            Renderer renderer,
            Dictionary<Transform, BoneCategory> bonemap,
            BoneWeightMap weights)
        {
            Mesh mesh = null;
            Transform[] bones = null;
            BoneWeight[] bw = null;

            if (renderer is SkinnedMeshRenderer smr)
            {
                mesh = smr.sharedMesh;
                bones = smr.bones;
                bw = mesh != null ? mesh.boneWeights : null;
            }
            else if (renderer is MeshRenderer mr)
            {
                var mf = mr.GetComponent<MeshFilter>();
                mesh = mf != null ? mf.sharedMesh : null;
            }
            if (mesh == null) return new List<MeshDensityStats>();

            var verts = mesh.vertices;
            var uvs = mesh.uv;
            var localToWorld = renderer.localToWorldMatrix;
            var result = new List<MeshDensityStats>();

            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                var tris = mesh.GetTriangles(sub);
                float uvArea = 0f, worldArea = 0f;
                float boneWeightSum = 0f;
                int triCount = tris.Length / 3;

                for (int i = 0; i < tris.Length; i += 3)
                {
                    int i0 = tris[i], i1 = tris[i + 1], i2 = tris[i + 2];
                    if (uvs != null && uvs.Length > i2) uvArea += TriArea2D(uvs[i0], uvs[i1], uvs[i2]);
                    var w0 = localToWorld.MultiplyPoint3x4(verts[i0]);
                    var w1 = localToWorld.MultiplyPoint3x4(verts[i1]);
                    var w2 = localToWorld.MultiplyPoint3x4(verts[i2]);
                    worldArea += TriArea3D(w0, w1, w2);

                    if (bones != null && bw != null)
                    {
                        float wTri = (BoneWeightFor(bw[i0], bones, bonemap, weights) +
                                      BoneWeightFor(bw[i1], bones, bonemap, weights) +
                                      BoneWeightFor(bw[i2], bones, bonemap, weights)) / 3f;
                        boneWeightSum += wTri;
                    }
                    else
                    {
                        boneWeightSum += WeightForStaticMesh(renderer.transform, bonemap, weights);
                    }
                }

                result.Add(new MeshDensityStats
                {
                    Renderer = renderer,
                    SubmeshIndex = sub,
                    UvArea = uvArea,
                    WorldArea = worldArea * 10000f,
                    BoneWeightAverage = triCount > 0 ? boneWeightSum / triCount : 0.5f,
                });
            }

            return result;
        }

        static float TriArea2D(Vector2 a, Vector2 b, Vector2 c) =>
            Mathf.Abs(((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x))) * 0.5f;

        static float TriArea3D(Vector3 a, Vector3 b, Vector3 c) =>
            Vector3.Cross(b - a, c - a).magnitude * 0.5f;

        static float BoneWeightFor(BoneWeight bw, Transform[] bones,
                                   Dictionary<Transform, BoneCategory> bonemap, BoneWeightMap weights)
        {
            float sum = 0f;
            sum += bw.weight0 * LookupBone(bones, bw.boneIndex0, bonemap, weights);
            sum += bw.weight1 * LookupBone(bones, bw.boneIndex1, bonemap, weights);
            sum += bw.weight2 * LookupBone(bones, bw.boneIndex2, bonemap, weights);
            sum += bw.weight3 * LookupBone(bones, bw.boneIndex3, bonemap, weights);
            return sum;
        }

        static float LookupBone(Transform[] bones, int idx,
                                Dictionary<Transform, BoneCategory> bonemap, BoneWeightMap weights)
        {
            if (bones == null || idx < 0 || idx >= bones.Length || bones[idx] == null) return weights.Get(BoneCategory.Other);
            var cat = ClassifyInHierarchy(bones[idx], bonemap);
            return weights.Get(cat);
        }

        static BoneCategory ClassifyInHierarchy(Transform t, Dictionary<Transform, BoneCategory> bonemap)
        {
            for (var cur = t; cur != null; cur = cur.parent)
                if (bonemap.TryGetValue(cur, out var c)) return c;
            return BoneClassifier.ClassifyByName(t.name);
        }

        static float WeightForStaticMesh(Transform t, Dictionary<Transform, BoneCategory> bonemap, BoneWeightMap weights)
        {
            for (var cur = t; cur != null; cur = cur.parent)
                if (bonemap.TryGetValue(cur, out var c)) return weights.Get(c);
            return weights.Get(BoneCategory.Other);
        }
    }
}
