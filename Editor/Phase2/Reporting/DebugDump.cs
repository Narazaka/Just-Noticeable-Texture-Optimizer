using System.IO;
using System.Text;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.Tiling;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Reporting
{
    public static class DebugDump
    {
        public static void DumpTileScores(
            string outputDir, string texName,
            UvTileGrid grid, float[][] metricScores, string[] metricNames)
        {
            if (string.IsNullOrEmpty(outputDir)) return;
            Directory.CreateDirectory(outputDir);

            var sb = new StringBuilder();
            sb.Append("tx,ty,hasCoverage,density,boneW");
            foreach (var m in metricNames) sb.Append(",").Append(m);
            sb.AppendLine();

            for (int ty = 0; ty < grid.TilesY; ty++)
            for (int tx = 0; tx < grid.TilesX; tx++)
            {
                int idx = ty * grid.TilesX + tx;
                var t = grid.Tiles[idx];
                sb.Append(tx).Append(",").Append(ty).Append(",").Append(t.HasCoverage)
                  .Append(",").Append(t.Density.ToString("F3"))
                  .Append(",").Append(t.BoneWeight.ToString("F3"));
                for (int m = 0; m < metricScores.Length; m++)
                    sb.Append(",").Append(metricScores[m][idx].ToString("F4"));
                sb.AppendLine();
            }
            File.WriteAllText(Path.Combine(outputDir, SanitizeName(texName) + "_tiles.csv"), sb.ToString());
        }

        public static void DumpHeatmapPng(
            string outputDir, string texName, string metricName,
            UvTileGrid grid, float[] scores)
        {
            if (string.IsNullOrEmpty(outputDir)) return;
            Directory.CreateDirectory(outputDir);

            var tex = new Texture2D(grid.TilesX, grid.TilesY, TextureFormat.RGBA32, false);
            try
            {
                var px = new Color[grid.Tiles.Length];
                for (int i = 0; i < scores.Length; i++)
                {
                    float s = Mathf.Clamp01(scores[i]);
                    px[i] = new Color(s, 1f - s, 0f, grid.Tiles[i].HasCoverage ? 1f : 0.3f);
                }
                tex.SetPixels(px);
                tex.Apply();
                var png = tex.EncodeToPNG();
                File.WriteAllBytes(
                    Path.Combine(outputDir, SanitizeName(texName) + "_" + SanitizeName(metricName) + ".png"),
                    png);
            }
            finally
            {
                Object.DestroyImmediate(tex);
            }
        }

        static string SanitizeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "_";
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(name.Length);
            foreach (var c in name)
                sb.Append(System.Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            return sb.ToString();
        }
    }
}
