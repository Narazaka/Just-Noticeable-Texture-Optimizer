using System.IO;
using UnityEditor;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.Degradation;

namespace Narazaka.VRChat.Jnto.Editor.Tools
{
    public static class SsimCompareTool
    {
        const string PathsFile = "Temp/jnto_ssim_paths.txt";

        [MenuItem("Tools/JNTO/SSIM Compare (Temp Paths)")]
        public static void Run()
        {
            if (!File.Exists(PathsFile))
            {
                Debug.LogError($"[JNTO_SSIM] missing {PathsFile}");
                return;
            }
            var lines = File.ReadAllLines(PathsFile);
            if (lines.Length < 2)
            {
                Debug.LogError("[JNTO_SSIM] expected 2 lines (PNG path A and B)");
                return;
            }
            var a = Load(lines[0].Trim());
            var b = Load(lines[1].Trim());
            if (a == null || b == null) return;

            var (a2, b2) = MatchSize(a, b);
            var score = new SsimMetric().Evaluate(a2, b2);
            Debug.LogError($"[JNTO_SSIM] {Path.GetFileName(lines[0])} vs {Path.GetFileName(lines[1])} = {score:F6}");

            if (a2 != a) Object.DestroyImmediate(a2);
            if (b2 != b) Object.DestroyImmediate(b2);
            Object.DestroyImmediate(a);
            Object.DestroyImmediate(b);
        }

        static Texture2D Load(string path)
        {
            if (!File.Exists(path))
            {
                Debug.LogError($"[JNTO_SSIM] file not found: {path}");
                return null;
            }
            var bytes = File.ReadAllBytes(path);
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!t.LoadImage(bytes))
            {
                Debug.LogError($"[JNTO_SSIM] failed to decode PNG: {path}");
                Object.DestroyImmediate(t);
                return null;
            }
            return t;
        }

        static (Texture2D, Texture2D) MatchSize(Texture2D a, Texture2D b)
        {
            if (a.width == b.width && a.height == b.height) return (a, b);
            int w = Mathf.Min(a.width, b.width);
            int h = Mathf.Min(a.height, b.height);
            return (Resize(a, w, h), Resize(b, w, h));
        }

        static Texture2D Resize(Texture2D src, int w, int h)
        {
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(src, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var dst = new Texture2D(w, h, TextureFormat.RGBA32, false);
            dst.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            dst.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return dst;
        }
    }
}
