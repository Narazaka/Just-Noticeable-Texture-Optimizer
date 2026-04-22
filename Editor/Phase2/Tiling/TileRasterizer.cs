using System.Collections.Generic;
using UnityEngine;
using Narazaka.VRChat.Jnto;
using Narazaka.VRChat.Jnto.Editor.Phase2;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Tiling
{
    public static class TileRasterizer
    {
        /// <summary>
        /// メッシュの三角形を UvTileGrid にラスタライズし、
        /// 各タイルの Density / BoneWeight を保守 max で集計する。
        /// 1 三角形が複数タイルにまたがる場合、交差するすべてのタイルに対して書き込む。
        /// </summary>
        public static void Accumulate(
            UvTileGrid grid,
            Renderer renderer,
            Mesh mesh,
            Dictionary<Transform, BoneCategory> bonemap,
            BoneWeightMap weights)
        {
            if (mesh == null) return;
            var uvs = mesh.uv;
            var verts = mesh.vertices;
            if (uvs == null || uvs.Length == 0 || verts == null) return;

            var l2w = renderer.localToWorldMatrix;
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

                    var w0 = l2w.MultiplyPoint3x4(verts[i0]);
                    var w1 = l2w.MultiplyPoint3x4(verts[i1]);
                    var w2 = l2w.MultiplyPoint3x4(verts[i2]);
                    float worldAreaM2 = TriArea3D(w0, w1, w2);
                    float worldAreaCm2 = worldAreaM2 * 10000f;
                    float density = worldAreaCm2 / uvArea;

                    float bwAvg = 1f;
                    if (bones != null && bw != null && bonemap != null && weights != null)
                    {
                        bwAvg = (BoneWeightFor(bw, i0, bones, bonemap, weights)
                               + BoneWeightFor(bw, i1, bones, bonemap, weights)
                               + BoneWeightFor(bw, i2, bones, bonemap, weights)) / 3f;
                    }
                    else if (bonemap != null && weights != null)
                    {
                        bwAvg = StaticBoneWeight(renderer.transform, bonemap, weights);
                    }

                    Rasterize(grid, uvs[i0], uvs[i1], uvs[i2], density, bwAvg);
                }
            }
        }

        static void Rasterize(UvTileGrid g, Vector2 uv0, Vector2 uv1, Vector2 uv2, float density, float bw)
        {
            float tw = g.TilesX, th = g.TilesY;
            float x0 = uv0.x * tw, y0 = uv0.y * th;
            float x1 = uv1.x * tw, y1 = uv1.y * th;
            float x2 = uv2.x * tw, y2 = uv2.y * th;

            int minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(x0, Mathf.Min(x1, x2))));
            int maxX = Mathf.Min(g.TilesX - 1, Mathf.FloorToInt(Mathf.Max(x0, Mathf.Max(x1, x2))));
            int minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(y0, Mathf.Min(y1, y2))));
            int maxY = Mathf.Min(g.TilesY - 1, Mathf.FloorToInt(Mathf.Max(y0, Mathf.Max(y1, y2))));

            for (int ty = minY; ty <= maxY; ty++)
                for (int tx = minX; tx <= maxX; tx++)
                {
                    if (!TriangleIntersectsTile(x0, y0, x1, y1, x2, y2, tx, ty)) continue;
                    ref var tile = ref g.GetTile(tx, ty);
                    tile.HasCoverage = true;
                    if (density > tile.Density) tile.Density = density;
                    if (bw > tile.BoneWeight) tile.BoneWeight = bw;
                }
        }

        static bool TriangleIntersectsTile(float x0, float y0, float x1, float y1, float x2, float y2, int tx, int ty)
        {
            float tl = tx, tr = tx + 1, tt = ty, tb = ty + 1;
            if (PointInTri(tl, tt, x0, y0, x1, y1, x2, y2)) return true;
            if (PointInTri(tr, tt, x0, y0, x1, y1, x2, y2)) return true;
            if (PointInTri(tl, tb, x0, y0, x1, y1, x2, y2)) return true;
            if (PointInTri(tr, tb, x0, y0, x1, y1, x2, y2)) return true;
            if (x0 >= tl && x0 <= tr && y0 >= tt && y0 <= tb) return true;
            if (x1 >= tl && x1 <= tr && y1 >= tt && y1 <= tb) return true;
            if (x2 >= tl && x2 <= tr && y2 >= tt && y2 <= tb) return true;
            if (SegIntersectRect(x0, y0, x1, y1, tl, tt, tr, tb)) return true;
            if (SegIntersectRect(x1, y1, x2, y2, tl, tt, tr, tb)) return true;
            if (SegIntersectRect(x2, y2, x0, y0, tl, tt, tr, tb)) return true;
            return false;
        }

        static bool PointInTri(float px, float py, float x0, float y0, float x1, float y1, float x2, float y2)
        {
            float d1 = (px - x1) * (y0 - y1) - (x0 - x1) * (py - y1);
            float d2 = (px - x2) * (y1 - y2) - (x1 - x2) * (py - y2);
            float d3 = (px - x0) * (y2 - y0) - (x2 - x0) * (py - y0);
            bool hasNeg = (d1 < 0) || (d2 < 0) || (d3 < 0);
            bool hasPos = (d1 > 0) || (d2 > 0) || (d3 > 0);
            return !(hasNeg && hasPos);
        }

        static bool SegIntersectRect(float ax, float ay, float bx, float by, float rl, float rt, float rr, float rb)
        {
            return SegSeg(ax, ay, bx, by, rl, rt, rr, rt)
                || SegSeg(ax, ay, bx, by, rr, rt, rr, rb)
                || SegSeg(ax, ay, bx, by, rr, rb, rl, rb)
                || SegSeg(ax, ay, bx, by, rl, rb, rl, rt);
        }

        static bool SegSeg(float ax, float ay, float bx, float by, float cx, float cy, float dx, float dy)
        {
            float d = (bx - ax) * (dy - cy) - (by - ay) * (dx - cx);
            if (Mathf.Abs(d) < 1e-10f) return false;
            float t = ((cx - ax) * (dy - cy) - (cy - ay) * (dx - cx)) / d;
            float u = ((cx - ax) * (by - ay) - (cy - ay) * (bx - ax)) / d;
            return t >= 0 && t <= 1 && u >= 0 && u <= 1;
        }

        static float TriArea2D(Vector2 a, Vector2 b, Vector2 c) =>
            Mathf.Abs((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x)) * 0.5f;

        static float TriArea3D(Vector3 a, Vector3 b, Vector3 c) =>
            Vector3.Cross(b - a, c - a).magnitude * 0.5f;

        static float BoneWeightFor(BoneWeight[] bw, int idx, Transform[] bones,
            Dictionary<Transform, BoneCategory> bonemap, BoneWeightMap weights)
        {
            if (idx >= bw.Length) return weights.Get(BoneCategory.Other);
            var w = bw[idx];
            float s = 0f;
            s += w.weight0 * LookupBone(bones, w.boneIndex0, bonemap, weights);
            s += w.weight1 * LookupBone(bones, w.boneIndex1, bonemap, weights);
            s += w.weight2 * LookupBone(bones, w.boneIndex2, bonemap, weights);
            s += w.weight3 * LookupBone(bones, w.boneIndex3, bonemap, weights);
            return s;
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

        static float StaticBoneWeight(Transform t,
            Dictionary<Transform, BoneCategory> bonemap, BoneWeightMap weights)
        {
            for (var cur = t; cur != null; cur = cur.parent)
                if (bonemap.TryGetValue(cur, out var c)) return weights.Get(c);
            return weights.Get(BoneCategory.Other);
        }
    }
}
