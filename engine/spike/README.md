# The browser-engine spike (stage 0 of `docs/plan-browser-engine.md`)

Outside `Lapin.slnx`: the desktop build, its tests and CI never see this folder.

- `shared/Probe.cs` — the measurement: open a project from its bytes, build every body, time it; rebuild a storey; build a generated storey (`Heavy`); booleans on two cubes.
- `Lapin.Browser.Check` — the probe natively (with the desktop's manifold): the reference. `dotnet run -c Release`.
- `Lapin.Browser` — the probe in the browser (`Microsoft.NET.Sdk.WebAssembly`), the modules of `products/lapin.json` by project references, manifold linked into the WebAssembly.
- `build-manifold-wasm.sh` — manifold v3.2.1 as static libraries for `browser-wasm` (writes `native/browser-wasm/`, ignored by git).

## Running it

Needs the `wasm-tools` workload. The system SDK cannot take it without root, so the spike used a user-level SDK: `dotnet-install.sh --version 10.0.300 --install-dir ~/.dotnet-wasm`, then `~/.dotnet-wasm/dotnet workload install wasm-tools`.

```sh
export DOTNET_ROOT=~/.dotnet-wasm
./build-manifold-wasm.sh                      # once
cd Lapin.Browser
$DOTNET_ROOT/dotnet publish -c Release -p:RunAOTCompilation=true -p:PublishTrimmed=true -p:TrimMode=partial -p:TreatWarningsAsErrors=false -o /tmp/lapin-spike-aot
cp -R wwwroot/* /tmp/lapin-spike-aot/wwwroot/  # the page and testLapin.lapin
# serve /tmp/lapin-spike-aot/wwwroot statically (any server that sends .wasm as application/wasm) and open it: the numbers are in the page and in window.result
```
