# The engine in the browser: plan

Asked on 25.10.2026 (pending decision 124). The engine compiled to WebAssembly and run in a Web Worker, so a tester opens Lapin from a URL, with the project staying in the browser. This plan starts from what the code does today and is bound by one rule, stated first because everything else bends to it.

## The rule: local first stays as it is

The desktop app (Tauri shell + `Lapin.Host` on loopback) remains the product and the reference. The browser build is a second host over the same engine, never a fork of it, and never a reason to make the desktop slower, bigger or different. Measurable form of the rule, checked on every pull request of this plan:

1. `pnpm build:product <id>` produces the same bundle it does today: same files in `Resources/modules`, same `Lapin.Host` binary, same `product.json`. The browser build is a separate command (`pnpm build:web <id>`) that the desktop pipeline never calls.
2. No module (`engine/src/Modules/*`), no platform behaviour and no test changes because of this plan. Where the browser needs a seam (a stream instead of a path, a static module list instead of a folder), the seam is added beside the existing code, and the desktop host keeps calling the existing code. The existing 437 engine tests and 87 UI tests pass unchanged; new tests cover the seams.
3. The UI keeps talking to the engine through `EngineClient` and the same JSON-RPC/binary-frame protocol. The transport (WebSocket to the local engine, or `postMessage` to a worker) is chosen at build time by the environment; the desktop and `pnpm dev` keep the WebSocket, with no code path of theirs touched.
4. Nothing of the desktop's is taken away for the web's sake: the recovery journals on disk, the MCP bridge through `host.json`, the detached view windows, LegacyImport from paths, all stay. The web either has its own version (journals in OPFS) or does without (MCP, detached windows), stated as a limit.
5. No engine process ever runs on a server for this plan, and no project ever leaves the browser. Hosting is static files only.

If a step cannot be done within these five points, the step is dropped, not the rule.

## What exists today (the facts the plan builds on)

- `Lapin.Host/Program.cs` starts Kestrel on loopback with one `Session` per process; `EngineHub.Handle(text)` takes one JSON-RPC call and returns the reply as a binary frame, broadcasting the diff first. The session is single-threaded by design (a semaphore gates the calls). The only `Task.Run` is the parent-process watch.
- Files are paths: `project.open`/`project.save` through `ProjectFiles` (`PackageFiles.Open/Save`), `project.importIfc` through `IfcImport.Run(session, path)`, `project.exportIfc` through `IfcExport.Write(session, path)` (into a temp file when no path is given), the recovery journal under `<user-dir>/recovery`. Exports already come back as base64 when no path is given (`Deliver`), and `ui/src/engine/files.ts` downloads them in a browser.
- The only native code is manifold (`Lapin.Booleans`, `DllImport("manifoldc")`, probed with `NativeLibrary.TryLoad`); the engine runs without it, with diagnostics. The only NuGet package in the engine is `Apache.Arrow` (managed). PDF, DXF and XLSX are written by `Lapin.Drawing` itself.
- Modules are loaded at start from a folder by `ModuleLoader` (`AssemblyLoadContext.LoadFromAssemblyPath`, `Activator.CreateInstance`); `products/<id>.json` lists them and `scripts/product.mjs` completes the list with what they require.
- The geometry kernel is Lapin's own code (`engine/src/Geometry`, MIT) since c71fe5f: a clean clone builds alone.
- `ui/src/engine/client.ts`: `EngineClient(resolveUrl)` opens a WebSocket, sends JSON text, receives binary frames (`decodeFrame`), reconnects by itself. `engineUrl()` reads `VITE_ENGINE_URL`, else `ws://127.0.0.1:47810/ws`.

## Stage 0: the spike (2–3 days, on a branch, nothing merged but what passes)

Three questions, in this order; each has a pass/fail answer and the next starts only when the previous passed.

1. **Does the engine compile and run for `browser-wasm`?** A throwaway `Lapin.Browser` project referencing `Lapin.Platform`, `Lapin.Package`, `Lapin.Ifc` and the modules of `products/lapin.json` as plain project references (no `ModuleLoader`), interpreted (no AOT), without manifold. One `[JSExport]` that takes a JSON-RPC string and returns the reply frame bytes. Test page: copies `modele/testLapin.lapin` into Emscripten's in-memory file system and calls `project.open` with that virtual path, then `doc.snapshot`. **Pass:** every element has a body and the part volumes match the native engine's (minus the openings, which need manifold), no exception from `Apache.Arrow` or `System.IO.Compression`.
2. **Do AOT and trimming hold?** Same project with `RunAOTCompilation` and `PublishTrimmed`. **Pass:** builds; the test page runs without a `MissingMethodException`/reflection failure (the known suspects: `ModuleLoader`'s `Activator.CreateInstance`, `JsonSerializer.Deserialize<ModuleManifest|ProductManifest|LintelProduct>`, `JsonSerializer.SerializeToNode` of the roof catalogue); rebuilding the test house (`architecture.updateRoof` with a new pitch) takes under 3× the native time; the compressed download is under 15 MB.
3. **Does manifold work from npm?** `manifold-3d` loaded in the worker, an `ISolidBooleans` implementation in `Lapin.Browser` that marshals the mesh arrays through `[JSImport]`. **Pass:** the openings of `testLapin.lapin` are cut and the volumes now match the native engine's exactly.

Deliverable: a short section at the end of this file with the three answers and the measured numbers (download size, start time, rebuild time, memory at peak). If 1 fails, the plan stops here and the file says why.

## Stage 1: the seams (desktop unchanged, proven by tests)

Small, behaviour-preserving refactors that both hosts then share. Each is one commit with its own test, mergeable on its own, useful even if the browser never ships.

- **`Lapin.Protocol`** (new class library under `engine/src/Platform`): `BinaryFrame` and the dispatcher (`EngineHub.Handle`/`Dispatch` without the WebSocket parts) move there as `EngineDispatcher`. `Lapin.Host` keeps `Program.cs`, the Kestrel loop, `Recovery`, `ExitGuard`, `LegacyImport`, `Support`, and calls the dispatcher. Test: the protocol tests run against the dispatcher directly (they already drive `Handle`).
- **Streams beside paths.** `LapinPackage.Read(Stream)`/`Write(Stream, …)`, `IfcExport.Write(session, Stream, …)`, `IfcImport.Run(session, Stream, name, …)`; the path overloads become thin wrappers that open the file and call the stream ones, so every existing caller and test is untouched. `ProjectFiles` gains a bytes-based pair (`OpenBytes(name, bytes)`, `SaveBytes()`), the desktop keeps `Open(path)`/`Save(path)`.
- **`IJournalStore`** behind `Recovery`: the class keeps its logic (start lines, tx lines, grace, exclusive hold) over an interface with `Open(name)`, `Append(line)`, `List()`, `Read(name)`, `Delete(name)`. `FileJournalStore` is today's code under `<user-dir>/recovery`; nothing changes for the desktop.
- **Static module registry.** A generated file `Modules.g.cs` per product (written by `scripts/product.mjs` at build, from the same resolution the desktop uses) that returns `new ArchitectureModule()`, `new StructureModule()`, … in order. `ModuleLoader` stays the desktop's way; the browser host uses the generated list. The product resolution stays one function, so a module added to a product reaches both hosts.
- **Manifold behind the service, not the class.** `ISolidBooleans` is already the contract; `CoreModule.Register` registers `ManifoldBooleans` when `Available`. The registration moves to the host (desktop: `ManifoldBooleans`; browser: the npm-backed one), so `Lapin.Platform` no longer references `Lapin.Booleans` directly. The desktop still gets the native library; the tests still get it through the fixture.

## Stage 2: the browser host and its UI transport

- **`engine/src/Host/Lapin.Browser`** (`<TargetFramework>net10.0</TargetFramework>`, `<RuntimeIdentifier>browser-wasm</RuntimeIdentifier>`): the generated module list, `EngineDispatcher`, `BytesProjectFiles`, `OpfsJournalStore` (through `[JSImport]` to OPFS), the npm manifold `ISolidBooleans`. `[JSExport] static byte[] Handle(string json)` plus `[JSExport] static void Subscribe()` for diffs delivered as frames. Nothing else of the desktop host is in it.
- **The worker** (`ui/src/engine/worker/engine.worker.ts`): loads `dotnet.js`, forwards messages, posts frames back as transferable `ArrayBuffer`s (zero copy; the existing `decodeFrame` reads them).
- **The transport** (`ui/src/engine/transport.ts`): `EngineTransport { send(text), onFrame(cb), onStatus(cb) }` with `WebSocketTransport` (today's `open()` logic moved, byte for byte) and `WorkerTransport`. `EngineClient` takes a transport; `session.ts` picks it from `import.meta.env.VITE_ENGINE` (`ws`, the default and what `pnpm dev` and the desktop use; `worker` for the web build). The client's tests cover both transports.
- **Files in the browser** (`files.ts` already branches on `isDesktop()`): open through `showOpenFilePicker` where it exists, else `<input type=file>`; the bytes go to `project.openBytes`. Save through `showSaveFilePicker` where it exists (the handle kept for the session so "Save" writes in place), else download; `project.saveBytes` returns the package. The recent list keeps names, not paths, on the web.
- **The web build**: `pnpm build:web <id>` publishes `Lapin.Browser` for that product (AOT, trimmed), runs the Vite build with `VITE_ENGINE=worker` and the product's UI modules, and writes one static folder (`build/<id>/web`). The Bubbles plugins tab is excluded as it is from every public build (`lapin-plugins-private`).
- **Deploy**: a folder on the nginx that serves `lapin.ciuntucbimstudio.ro` (static, with the COOP/COEP headers only if the threaded runtime is ever used; the single-threaded one needs none), or GitHub Pages for prereleases. `deploy/lapin/deploy.sh` gains a `web` target; the existing targets are unchanged.

## Stage 3: budgets, CI, limits

- **Budgets** written in `scripts/build-web.mjs` and enforced there: compressed download ≤ 15 MB, start to `session.info` ≤ 4 s on a 2020 laptop, rebuild of the test house ≤ 3× native. A build that breaks a budget fails, so the numbers from the spike stay true.
- **CI**: a `web` job beside `ui` and `engine` in `.github/workflows/ci.yml` (the Emscripten workload, `dotnet publish`, the budgets, a headless Chrome smoke test that opens `testLapin.lapin`). The `engine` job does not change.
- **Stated limits** on the web (in the start screen's about text, and in `docs/mcp.md`): no MCP bridge; no detached view windows; the memory ceiling (2 GB; a large imported IFC may not fit, which is in line with "Lapin is native first"); Safari and Firefox save by download.
- **What the web does without, by choice**: no server engine, no accounts, no sync. A tester's project lives in their browser (OPFS journal) and in the `.lapin` they download.

## Stage 4: parity and the desktop's gains

Only after the web runs with the budgets kept:

- LegacyImport from bytes (old format-2 projects dropped on the page).
- `support.report` from the browser (the report goes to the same Hetzner endpoint as the desktop's, `lapin-feedback-reports`).
- The seams pay back on the desktop: `project.saveBytes` lets the Tauri shell save without the engine touching the path (one less reason for the engine to know the file system), and `IJournalStore` lets the desktop keep journals elsewhere if ever wanted. Neither is required; both are free.

## Effort

Stage 0: 2–3 days. Stage 1: 2–3 days (mostly tests). Stage 2: 5–7 days. Stage 3: 2 days. Total to "a tester opens, draws, saves and exports": about two weeks after the spike. Stage 4: one more week, when wanted.

## Open questions

1. **Which products go to the web?** `lapin` only, or every edition that has no private module? (Default: `lapin` only, until the budgets are known.)
2. **Where is it served?** `lapin.ciuntucbimstudio.ro/web/` beside the site, a subdomain, or GitHub Pages for prereleases? (Default: the nginx folder, same deploy script, a `web` target.)
3. **Safari and Firefox**: is "save by download" acceptable for testers, or do we wait for them to get the File System Access API? (Default: acceptable, stated on the start screen.)
4. **Interpreter fallback**: keep a non-AOT build for debugging only, or none? (Default: debugging only, never deployed.)
5. **The plugins**: the web build excludes the Bubbles tab as every public build does; confirm nothing else is private in the `lapin` product. (Default: nothing else.)

## Spike results (25.10.2026, branch `spike/browser-engine`, `engine/spike/`)

The spike lives outside `Lapin.slnx` (the desktop build and CI never see it): `engine/spike/shared/Probe.cs` is the measurement, run natively by `Lapin.Browser.Check` (the reference, with manifold) and in the browser by `Lapin.Browser` (`Microsoft.NET.Sdk.WebAssembly`, the modules by plain project references). Two loads: `testLapin.lapin` (the old test room, converted once to format 3 by the native engine) and a generated storey (24 × 20 m in 4 m rooms: 13 walls crossing and joining, 25 doors and windows, a slab, a hip roof; 176 entities, 80 bodies), built by commands as the interface would, then the whole storey rebuilt.

### Step 1: the engine compiles and runs for `browser-wasm` — passed

- Builds with the stock SDK, no workload, no change to any engine project. `NativeLibrary.TryLoad("manifoldc")` answers "missing" without failing; `Apache.Arrow` and `System.IO.Compression` work; the package opens from its bytes through Emscripten's in-memory file system.
- Only obstacle: the WebAssembly SDK turns reflection-based `System.Text.Json` off by default; turned back on for this step (`JsonSerializerIsReflectionEnabledByDefault`).
- `testLapin.lapin`: same 30 entities and 5 bodies as natively; the slab (14.1288 m³), the column (0.48) and the 200 mm wall are identical to the native volumes; the two layered walls are larger because, without manifold, the column does not cut them (the diagnostic says so). Runtime start 0.15 s (local), open 0.4 s.
- Interpreted speed on the generated storey: snapshot 1925 ms against 97 ms natively, rebuild of the storey 2028 ms against 99 ms, the whole build by commands 26.4 s against 3.0 s; that is about 20× slower although the browser does no booleans. **The interpreter is not usable: AOT is required**, as the plan assumed.

### Step 3: manifold in the browser — passed, by linking it in (not from npm)

- **manifold-3d from npm does not work for us.** Called synchronously from inside a .NET → JavaScript call (`[JSImport]`), which is what `ISolidBooleans` needs, it aborts (`RuntimeError: Aborted()`), even on two cubes; the same calls work in Node and in a page without the .NET runtime, and an instance made before the runtime starts fails with "memory access out of bounds". Two Emscripten modules sharing a call stack is not a road to build on; its meshes are single precision besides.
- **Linked into the engine's WebAssembly, it works.** `engine/spike/build-manifold-wasm.sh` builds manifold v3.2.1 (the tag and options of `scripts/build-manifold.mjs`: manifold, Clipper2, no TBB) with the Emscripten 3.1.56 that the `wasm-tools` workload brings, with the runtime's exception model (`-fwasm-exceptions`) and SIMD; `Lapin.Browser` takes the three static libraries as `NativeFileReference`s (`WasmBuildNative`). The library must be named `manifoldc.a`, not `libmanifoldc.a`: the generated P/Invoke table names a static library by its file name, and `ManifoldBooleans` imports `manifoldc`. Then **the desktop's own `ManifoldBooleans` runs unchanged, in double precision** (`NativeLibrary.TryLoad` cannot see a linked library, so the browser host registers the service itself).
- Result: two cubes give 7, 15 and 1 m³ as natively; `testLapin.lapin` gives 18.92712353 m³ and the generated storey 362.588057 m³, **identical to the native engine to the last decimal**.
- Cost: the runtime's native module grows to about 3.1 MB (before compression).

### Step 2: AOT and trimming — AOT holds; the speed is just over budget; full trimming needs the JSON contexts

- **AOT** (`RunAOTCompilation`, the `wasm-tools` workload; the system SDK needs root for it, so the spike used a user-level SDK in `~/.dotnet-wasm`): publishes in about 2 minutes and runs everything without an exception; volumes identical to the native ones.
- **Speed**, generated storey, against the native engine on the same machine (Apple M-series, headless Chrome):

  | | Interpreted | AOT | Native |
  |---|---|---|---|
  | All bodies (snapshot) | 2705 ms | 311–341 ms | 92–97 ms |
  | Storey rebuilt | 2795 ms | 327–344 ms | 97–99 ms |
  | Built by 60 commands | 50 s | 6.1–6.3 s | 3.0 s |
  | Small edit (`testLapin`, 5 bodies) | 28 ms | 10–16 ms | 4–5 ms |

  AOT is 8× faster than the interpreter and about **3.4× slower than native** on whole-storey work (2× on command-by-command editing): over the 3× budget, though a third of a second for rebuilding 80 bodies stays interactive. `-O3` for the native part instead of the defaults gains about 5%, no more.
- **Size**, compressed (brotli), everything the page downloads: **6.3 MB** with AOT and the framework trimmed (`TrimMode=partial`, our assemblies whole); 2.7 MB interpreted and fully trimmed. Inside the 15 MB budget either way.
- **Memory**: the WebAssembly heap is 115 MB after the generated storey.
- **Start**: 0.14–0.18 s to a running engine from a local server (the download comes on top, over the network).
- **Full trimming** (`TrimMode=full`) breaks at run time where the engine serializes records by reflection (constructor parameter names are trimmed away): 55 places reported by the trimmer, nearly all `JsonSerializer.SerializeToNode/Deserialize` of records (`JoinerySpec`, `LintelRule`, `StairSpec`, `LintelProduct`, `MaterialLayer`, the standards, the package manifest…), generic `JsonArray.Add<T>`/`JsonValue.Create<T>`, and `ModuleLoader` (which the browser does not use). The cure is mechanical and helps the desktop too: one source-generated `JsonSerializerContext` for those records, and `JsonNode` overloads instead of the generic ones. Until then the browser build keeps reflection on and trims only the framework, which the 6.3 MB already reflects.

### What the spike says

All three questions are answered. The engine runs in the browser unchanged, with its own booleans in double precision and results identical to the desktop's, in 6.3 MB and 115 MB of memory. The one number past its budget is speed: 3.4× native on whole-storey work. Stage 1 can start as planned, with two changes:

1. **manifold is linked in, not taken from npm** (stage 1's "manifold behind the service" stays; the browser host registers the desktop's `ManifoldBooleans`). `scripts/build-manifold.mjs` gains a `browser-wasm` branch that writes `manifoldc.a` (no `lib` prefix).
2. **The JSON contexts move into stage 1** (they were implied by "trimming"): with them the browser build trims fully and shrinks, and the desktop serializes without reflection.

The speed budget is for the user to settle: keep 3× and look for the rest in stage 3 (profiling the rebuild in the browser; the snapshot builds meshes the worker could build lazily), or accept 4× for testers.
