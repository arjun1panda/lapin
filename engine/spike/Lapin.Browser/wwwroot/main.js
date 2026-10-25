// Starts the .NET runtime, opens testLapin.lapin from its bytes, rebuilds a storey, builds a heavier storey; the numbers go to window.result.
// ?nomanifold leaves the booleans out (the engine says so in its diagnostics).
import { dotnet } from './_framework/dotnet.js';
const out = document.getElementById('out');
const logs = [];
try {
  const t0 = performance.now();
  const { getAssemblyExports, getConfig, runMain, Module } = await dotnet.withConfig({ printErr: (m) => logs.push(m), print: (m) => logs.push(m) }).create();
  const useManifold = !new URLSearchParams(location.search).has('nomanifold');
  await runMain(getConfig().mainAssemblyName, useManifold ? ['--manifold'] : []);
  const exports = await getAssemblyExports(getConfig().mainAssemblyName);
  const started = performance.now() - t0;
  const booleans = JSON.parse(exports.Exports.Booleans());
  window.booleans = booleans;
  const bytes = new Uint8Array(await (await fetch('testLapin.lapin')).arrayBuffer());
  const t1 = performance.now();
  const open = JSON.parse(exports.Exports.Open(bytes));
  const openMs = performance.now() - t1;
  const rebuild = JSON.parse(exports.Exports.Rebuild());
  const heavy = JSON.parse(exports.Exports.Heavy());
  window.result = { startMs: Math.round(started), wasmHeapMB: Math.round(Module.HEAPU8.length / 1048576), booleans, openMs: Math.round(openMs), open, rebuild, heavy, logs };
} catch (e) {
  window.result = { error: String(e?.stack ?? e), logs };
}
out.textContent = JSON.stringify(window.result, null, 2);

