using System.Runtime.Versioning;
using Lapin.Geometry;
using Lapin.Platform;

namespace Lapin.Spike;

/// <summary>
/// What the browser host adds to the product: the booleans. manifold's C API
/// is linked into the engine's WebAssembly (build-manifold-wasm.sh), so the
/// desktop's own ManifoldBooleans runs unchanged, in double precision. (Its
/// probe, NativeLibrary.TryLoad, cannot see a statically linked library, so
/// the host registers it.)
/// </summary>
[SupportedOSPlatform("browser")]
sealed class BrowserModule : ILapinModule
{
    public string Id => "browser";
    public string Name => "Browser";
    public void Register(IModuleRegistry r) => r.Service<ISolidBooleans>(new Lapin.Booleans.ManifoldBooleans());
}
