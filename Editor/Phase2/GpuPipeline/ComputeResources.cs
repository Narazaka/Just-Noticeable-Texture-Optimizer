using UnityEditor;
using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.GpuPipeline
{
    /// <summary>
    /// Compute Shader を AssetDatabase からロードするヘルパー。
    /// パッケージ内の Shaders/ ディレクトリから .compute をロードする。
    /// </summary>
    public static class ComputeResources
    {
        const string Root = "Packages/net.narazaka.vrchat.jnto/Editor/Phase2/GpuPipeline/Shaders/";

        public static ComputeShader Load(string name)
        {
            var path = Root + name + ".compute";
            var cs = AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
            if (cs == null)
                throw new System.IO.FileNotFoundException("Compute shader not found: " + path);
            return cs;
        }
    }
}
