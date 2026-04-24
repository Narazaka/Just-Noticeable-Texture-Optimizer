using System.Collections.Generic;
using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Density
{
    public class TexelDensityMap
    {
        public float[] Density;
        public int Width;
        public int Height;

        public static TexelDensityMap Build(
            Renderer renderer, Mesh mesh, int texWidth, int texHeight,
            Dictionary<Transform, BoneCategory> bonemap, BoneWeightMap weights)
        {
            var map = new TexelDensityMap
            {
                Width = texWidth,
                Height = texHeight,
                Density = new float[texWidth * texHeight],
            };

            if (mesh == null) return map;

            var uvs = mesh.uv;
            var verts = mesh.vertices;
            if (uvs == null || uvs.Length == 0 || verts == null) return map;

            var localToWorld = renderer.localToWorldMatrix;
            Transform[] bones = null;
            BoneWeight[] bw = null;
            if (renderer is SkinnedMeshRenderer smr)
            {
                bones = smr.bones;
                bw = mesh.boneWeights;
            }

            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                var tris = mesh.GetTriangles(sub);
                for (int i = 0; i < tris.Length; i += 3)
                {
                    int i0 = tris[i], i1 = tris[i + 1], i2 = tris[i + 2];
                    if (i0 >= uvs.Length || i1 >= uvs.Length || i2 >= uvs.Length) continue;

                    float uvArea = TriArea2D(uvs[i0], uvs[i1], uvs[i2]);
                    if (uvArea < 1e-10f) continue;

                    var w0 = localToWorld.MultiplyPoint3x4(verts[i0]);
                    var w1 = localToWorld.MultiplyPoint3x4(verts[i1]);
                    var w2 = localToWorld.MultiplyPoint3x4(verts[i2]);
                    float worldArea = TriArea3D(w0, w1, w2);

                    float boneWeight = 1f;
                    if (bones != null && bw != null && bonemap != null && weights != null)
                    {
                        boneWeight = (BoneWeightFor(bw, i0, bones, bonemap, weights)
                                    + BoneWeightFor(bw, i1, bones, bonemap, weights)
                                    + BoneWeightFor(bw, i2, bones, bonemap, weights)) / 3f;
                    }

                    float density = worldArea / uvArea * boneWeight;

                    RasterizeTriangle(map, uvs[i0], uvs[i1], uvs[i2], density);
                }
            }

            Normalize(map);
            return map;
        }

        public static TexelDensityMap ResizeTo(TexelDensityMap src, int newW, int newH)
        {
            if (src == null) return null;
            var result = new TexelDensityMap { Width = newW, Height = newH, Density = new float[newW * newH] };
            for (int y = 0; y < newH; y++)
                for (int x = 0; x < newW; x++)
                {
                    int sx = Mathf.Clamp(x * src.Width / newW, 0, src.Width - 1);
                    int sy = Mathf.Clamp(y * src.Height / newH, 0, src.Height - 1);
                    result.Density[y * newW + x] = src.Density[sy * src.Width + sx];
                }
            return result;
        }

        public static TexelDensityMap Merge(TexelDensityMap a, TexelDensityMap b)
        {
            if (a == null) return b;
            if (b == null) return a;
            if (a.Width != b.Width || a.Height != b.Height) return a;

            var result = new TexelDensityMap
            {
                Width = a.Width,
                Height = a.Height,
                Density = new float[a.Density.Length],
            };
            for (int i = 0; i < result.Density.Length; i++)
                result.Density[i] = Mathf.Max(a.Density[i], b.Density[i]);
            return result;
        }

        static void RasterizeTriangle(TexelDensityMap map, Vector2 uv0, Vector2 uv1, Vector2 uv2, float density)
        {
            int w = map.Width, h = map.Height;
            float px0 = uv0.x * w, py0 = uv0.y * h;
            float px1 = uv1.x * w, py1 = uv1.y * h;
            float px2 = uv2.x * w, py2 = uv2.y * h;

            int minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(py0, Mathf.Min(py1, py2))));
            int maxY = Mathf.Min(h - 1, Mathf.CeilToInt(Mathf.Max(py0, Mathf.Max(py1, py2))));

            for (int y = minY; y <= maxY; y++)
            {
                float fy = y + 0.5f;
                float minX = w, maxX = 0;
                EdgeIntersect(px0, py0, px1, py1, fy, ref minX, ref maxX);
                EdgeIntersect(px1, py1, px2, py2, fy, ref minX, ref maxX);
                EdgeIntersect(px2, py2, px0, py0, fy, ref minX, ref maxX);

                int x0 = Mathf.Max(0, Mathf.FloorToInt(minX));
                int x1 = Mathf.Min(w - 1, Mathf.CeilToInt(maxX));
                for (int x = x0; x <= x1; x++)
                {
                    int idx = y * w + x;
                    if (map.Density[idx] < density)
                        map.Density[idx] = density;
                }
            }
        }

        static void EdgeIntersect(float x0, float y0, float x1, float y1, float scanY, ref float minX, ref float maxX)
        {
            if ((y0 <= scanY && y1 > scanY) || (y1 <= scanY && y0 > scanY))
            {
                float t = (scanY - y0) / (y1 - y0);
                float ix = x0 + t * (x1 - x0);
                if (ix < minX) minX = ix;
                if (ix > maxX) maxX = ix;
            }
        }

        static void Normalize(TexelDensityMap map)
        {
            float max = 0;
            for (int i = 0; i < map.Density.Length; i++)
                if (map.Density[i] > max) max = map.Density[i];
            if (max < 1e-8f) return;
            float inv = 1f / max;
            for (int i = 0; i < map.Density.Length; i++)
                map.Density[i] *= inv;
        }

        static float TriArea2D(Vector2 a, Vector2 b, Vector2 c) =>
            Mathf.Abs(((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x))) * 0.5f;

        static float TriArea3D(Vector3 a, Vector3 b, Vector3 c) =>
            Vector3.Cross(b - a, c - a).magnitude * 0.5f;

        static float BoneWeightFor(BoneWeight[] bw, int vertIdx, Transform[] bones,
            Dictionary<Transform, BoneCategory> bonemap, BoneWeightMap weights)
        {
            if (bw == null || vertIdx >= bw.Length) return weights.Get(BoneCategory.Other);
            var w = bw[vertIdx];
            float sum = 0;
            sum += w.weight0 * LookupBone(bones, w.boneIndex0, bonemap, weights);
            sum += w.weight1 * LookupBone(bones, w.boneIndex1, bonemap, weights);
            sum += w.weight2 * LookupBone(bones, w.boneIndex2, bonemap, weights);
            sum += w.weight3 * LookupBone(bones, w.boneIndex3, bonemap, weights);
            return sum;
        }

        static float LookupBone(Transform[] bones, int idx,
            Dictionary<Transform, BoneCategory> bonemap, BoneWeightMap weights)
        {
            if (bones == null || idx < 0 || idx >= bones.Length || bones[idx] == null)
                return weights.Get(BoneCategory.Other);
            for (var cur = bones[idx]; cur != null; cur = cur.parent)
                if (bonemap.TryGetValue(cur, out var c)) return weights.Get(c);
            return weights.Get(BoneClassifier.ClassifyByName(bones[idx].name));
        }
    }
}
