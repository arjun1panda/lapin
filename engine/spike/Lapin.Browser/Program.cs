using System.Runtime.InteropServices.JavaScript;

// The engine in the browser: at start, its booleans (when the page brought manifold); then the page calls the exports.
if (OperatingSystem.IsBrowser() && args.Contains("--manifold")) Lapin.Spike.Probe.HostModules = [new Lapin.Spike.BrowserModule()];
return 0;

[System.Runtime.Versioning.SupportedOSPlatform("browser")]
static partial class Exports
{
    [JSExport] internal static string Open(byte[] bytes) => Lapin.Spike.Probe.Open(bytes, "/tmp/lapin");
    [JSExport] internal static string Rebuild() => Lapin.Spike.Probe.Rebuild();
    [JSExport] internal static string Heavy() => Lapin.Spike.Probe.Heavy();
    [JSExport] internal static string Booleans() => Lapin.Spike.Probe.Booleans();
}
