using nadena.dev.ndmf;
using Narazaka.VRChat.Jnto.Editor;

[assembly: ExportsPlugin(typeof(JntoPlugin))]

namespace Narazaka.VRChat.Jnto.Editor
{
    public class JntoPlugin : Plugin<JntoPlugin>
    {
        public override string QualifiedName => "net.narazaka.vrchat.jnto";
        public override string DisplayName => "Just-Noticeable Texture Optimizer";

        protected override void Configure()
        {
            InPhase(BuildPhase.Optimizing)
                .Run("Collect & Apply Texture Optimization", _ => { });
        }
    }
}
