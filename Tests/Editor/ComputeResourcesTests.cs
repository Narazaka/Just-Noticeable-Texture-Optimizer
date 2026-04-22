using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.GpuPipeline;

public class ComputeResourcesTests
{
    [Test]
    public void Load_Placeholder_ReturnsComputeShader()
    {
        var cs = ComputeResources.Load("Placeholder");
        Assert.IsNotNull(cs);
    }

    [Test]
    public void Load_NonexistentName_Throws()
    {
        Assert.Throws<System.IO.FileNotFoundException>(() =>
        {
            ComputeResources.Load("NonexistentShader_xyz");
        });
    }

    [Test]
    public void Placeholder_Dispatch_WritesExpectedValue()
    {
        var cs = ComputeResources.Load("Placeholder");
        var buffer = new ComputeBuffer(1, sizeof(float));
        try
        {
            int k = cs.FindKernel("CSMain");
            cs.SetBuffer(k, "_Out", buffer);
            cs.Dispatch(k, 1, 1, 1);

            var result = new float[1];
            buffer.GetData(result);
            Assert.AreEqual(42f, result[0], 0.001f);
        }
        finally
        {
            buffer.Release();
        }
    }
}
