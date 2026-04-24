using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase1;
using Narazaka.VRChat.Jnto.Editor.Phase2.Compression;
using Narazaka.VRChat.Jnto.Editor.Shared;
using Narazaka.VRChat.Jnto.Editor.Phase2.Gate;
using Narazaka.VRChat.Jnto.Editor.Resolution;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Cache
{
    public struct CacheKey
    {
        public ulong Value;
        public override string ToString() => Value.ToString("x16");
    }

    public static class CacheKeyBuilder
    {
        public static CacheKey Build(
            Texture2D tex,
            TextureRole role,
            IEnumerable<TextureReference> references,
            ResolvedSettings settings)
        {
            var h = new XxHash64();
            HashTexture(h, tex);
            HashInt(h, (int)role);
            HashSettings(h, settings);
            if (references != null)
            {
                foreach (var r in references)
                {
                    if (r == null) continue;
                    HashReference(h, r);
                }
            }
            return new CacheKey { Value = h.GetCurrentHashAsUInt64() };
        }

        static void HashTexture(XxHash64 h, Texture2D tex)
        {
            if (tex == null)
            {
                HashString(h, "null-texture");
                return;
            }
            var path = AssetDatabase.GetAssetPath(tex);
            if (string.IsNullOrEmpty(path))
            {
                HashString(h, "runtime://" + tex.GetInstanceID());
                return;
            }
            HashString(h, AssetDatabase.AssetPathToGUID(path));
            try
            {
                if (File.Exists(path))
                {
                    h.Append(File.ReadAllBytes(path));
                }
                var meta = path + ".meta";
                if (File.Exists(meta))
                {
                    h.Append(File.ReadAllBytes(meta));
                }
            }
            catch
            {
                // file read failure → guid alone
            }
        }

        static void HashReference(XxHash64 h, TextureReference r)
        {
            HashString(h, r.PropertyName ?? "");
            if (r.Material != null && r.Material.shader != null)
            {
                var shader = r.Material.shader;
                HashString(h, shader.name);
                var path = AssetDatabase.GetAssetPath(shader);
                if (!string.IsNullOrEmpty(path))
                    HashString(h, AssetDatabase.AssetPathToGUID(path));
                HashString(h, LilTexAlphaUsageAnalyzer.IsAlphaUsed(r.Material, r.PropertyName).ToString());

                int propCount = ShaderUtil.GetPropertyCount(shader);
                for (int i = 0; i < propCount; i++)
                {
                    var ptype = ShaderUtil.GetPropertyType(shader, i);
                    if (ptype != ShaderUtil.ShaderPropertyType.Float
                     && ptype != ShaderUtil.ShaderPropertyType.Range
                     && ptype != ShaderUtil.ShaderPropertyType.Color)
                        continue;
                    var name = ShaderUtil.GetPropertyName(shader, i);
                    if (!(name.Contains("Alpha") || name.Contains("Mode") || name.Contains("Blend"))) continue;
                    HashString(h, name);
                    if (ptype == ShaderUtil.ShaderPropertyType.Color)
                    {
                        var c = r.Material.GetColor(name);
                        HashFloat(h, c.r); HashFloat(h, c.g); HashFloat(h, c.b); HashFloat(h, c.a);
                    }
                    else
                    {
                        HashFloat(h, r.Material.GetFloat(name));
                    }
                }
            }
            if (r.RendererContext != null)
            {
                var m = r.RendererContext.localToWorldMatrix;
                for (int i = 0; i < 16; i++) HashFloat(h, m[i]);

                Mesh mesh = null;
                if (r.RendererContext is SkinnedMeshRenderer smr) mesh = smr.sharedMesh;
                else if (r.RendererContext is MeshRenderer mr)
                {
                    var mf = mr.GetComponent<MeshFilter>();
                    mesh = mf != null ? mf.sharedMesh : null;
                }
                if (mesh != null)
                {
                    HashVectorArray(h, mesh.vertices);
                    HashVector2Array(h, mesh.uv);
                    HashIntArray(h, mesh.triangles);
                }
            }
        }

        static void HashSettings(XxHash64 h, ResolvedSettings s)
        {
            if (s == null) { HashString(h, "null-settings"); return; }
            HashInt(h, (int)s.Preset);
            HashFloat(h, s.ViewDistanceCm);
            HashFloat(h, s.HMDPixelsPerDegree);
            HashInt(h, (int)s.EncodePolicy);
            HashInt(h, (int)s.CacheMode);
            if (s.BoneWeights != null)
            {
                foreach (var e in s.BoneWeights.Entries)
                {
                    HashInt(h, (int)e.Category);
                    HashFloat(h, e.Weight);
                }
            }
            if (s.Calibration is DegradationCalibration cal)
            {
                HashFloat(h, cal.MsslBandEnergyScale);
                HashFloat(h, cal.MsslStructureScale);
                HashFloat(h, cal.RidgeScale);
                HashFloat(h, cal.BandingScale);
                HashFloat(h, cal.BlockBoundaryScale);
                HashFloat(h, cal.AlphaQuantScale);
                HashFloat(h, cal.NormalAngleScale);
                HashFloat(h, cal.ThresholdLow);
                HashFloat(h, cal.ThresholdMedium);
                HashFloat(h, cal.ThresholdHigh);
                HashFloat(h, cal.ThresholdUltra);
            }
        }

        static void HashInt(XxHash64 h, int v) => h.Append(BitConverter.GetBytes(v));
        static void HashFloat(XxHash64 h, float v) => h.Append(BitConverter.GetBytes(v));
        static void HashString(XxHash64 h, string s) => h.Append(System.Text.Encoding.UTF8.GetBytes(s ?? ""));

        static void HashVectorArray(XxHash64 h, Vector3[] arr)
        {
            if (arr == null) return;
            var buf = new byte[arr.Length * 12];
            for (int i = 0; i < arr.Length; i++)
            {
                Buffer.BlockCopy(BitConverter.GetBytes(arr[i].x), 0, buf, i * 12, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(arr[i].y), 0, buf, i * 12 + 4, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(arr[i].z), 0, buf, i * 12 + 8, 4);
            }
            h.Append(buf);
        }

        static void HashVector2Array(XxHash64 h, Vector2[] arr)
        {
            if (arr == null) return;
            var buf = new byte[arr.Length * 8];
            for (int i = 0; i < arr.Length; i++)
            {
                Buffer.BlockCopy(BitConverter.GetBytes(arr[i].x), 0, buf, i * 8, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(arr[i].y), 0, buf, i * 8 + 4, 4);
            }
            h.Append(buf);
        }

        static void HashIntArray(XxHash64 h, int[] arr)
        {
            if (arr == null) return;
            var buf = new byte[arr.Length * 4];
            Buffer.BlockCopy(arr, 0, buf, 0, buf.Length);
            h.Append(buf);
        }
    }
}
