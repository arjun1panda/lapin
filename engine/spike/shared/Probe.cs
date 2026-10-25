using System.Diagnostics;
using System.Text.Json.Nodes;
using Lapin.Geometry;
using Lapin.Package;
using Lapin.Platform;

namespace Lapin.Spike;

/// <summary>
/// The spike's measurement, the same code natively and in the browser: a
/// project opened from its bytes, every element's body built, the volumes
/// and the time each step took; then a change that rebuilds the storey.
/// </summary>
public static class Probe
{
    static Session? _session;

    public static readonly string[] Modules = ["architecture", "structure", "drafting", "studio", "phases", "civil"];

    /// <summary>What the host adds (the browser's booleans); none natively.</summary>
    public static ILapinModule[] HostModules = [];

    static Session Create() => new(ModuleCatalog.Create(
    [
        new Lapin.Architecture.ArchitectureModule(), new Lapin.Structure.StructureModule(), new Lapin.Drafting.DraftingModule(),
        new Lapin.Studio.StudioModule(), new Lapin.Phases.PhasesModule(), new Lapin.Civil.CivilModule(), .. HostModules,
    ]), new ProductManifest("lapin", "Lapin", ["core", .. Modules, .. HostModules.Select(m => m.Id)]));

    public static string Open(byte[] bytes, string scratchDir)
    {
        var total = Stopwatch.StartNew();
        var t = Stopwatch.StartNew();
        var session = _session = Create();
        var startMs = t.Elapsed.TotalMilliseconds;
        // Through a file, as the engine opens one today (the browser's is Emscripten's, in memory).
        Directory.CreateDirectory(scratchDir);
        var path = Path.Combine(scratchDir, "probe.lapin");
        File.WriteAllBytes(path, bytes);
        t.Restart();
        var content = LapinPackage.Read(path);
        session.Load(content.Entities, content.Relations, path, content.Classes);
        var readMs = t.Elapsed.TotalMilliseconds;
        t.Restart();
        var snapshot = session.Snapshot();
        var snapshotMs = t.Elapsed.TotalMilliseconds;
        var elements = new JsonObject();
        double sum = 0;
        var problems = new JsonArray();
        foreach (var e in session.Model.Entities.OrderBy(e => e.Id, StringComparer.Ordinal))
        {
            if (session.Catalog.Geometry(e.Class) == null) continue;
            var g = session.Geometry(e.Id);
            var v = g?.Parts.Sum(p => Measure.Volume(p.Solid)) ?? 0;
            sum += v;
            elements[$"{e.Class} {e.Name} {e.Id[..6]}"] = Math.Round(v, 9);
            foreach (var d in g?.Diagnostics ?? []) problems.Add((JsonNode?)JsonValue.Create($"{e.Name}: {d}"));
        }
        return new JsonObject
        {
            ["runtime"] = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier,
            ["booleans"] = session.Catalog.Get<ISolidBooleans>() != null,
            ["entities"] = session.Model.Entities.Count(),
            ["renderItems"] = snapshot.Render.Count,
            ["volume"] = Math.Round(sum, 9),
            ["ms"] = new JsonObject { ["session"] = Math.Round(startMs, 1), ["read"] = Math.Round(readMs, 1), ["snapshot"] = Math.Round(snapshotMs, 1), ["total"] = Math.Round(total.Elapsed.TotalMilliseconds, 1) },
            ["problems"] = problems,
            ["elements"] = elements,
        }.ToJsonString();
    }

    /// <summary>The storey raised 10 cm and back: everything on it rebuilt twice.</summary>
    public static string Rebuild()
    {
        var session = _session ?? throw new InvalidOperationException("Open a project first.");
        var storey = Spatial.Storeys(session.Model).First();
        var elevation = storey.Num("elevation");
        var t = Stopwatch.StartNew();
        var up = session.Execute("storey.update", new JsonObject { ["id"] = storey.Id, ["elevation"] = elevation + 0.1 }, out _);
        var upMs = t.Elapsed.TotalMilliseconds;
        t.Restart();
        session.Execute("storey.update", new JsonObject { ["id"] = storey.Id, ["elevation"] = elevation }, out _);
        return new JsonObject { ["rebuilt"] = up.Render.Count, ["ms"] = Math.Round(upMs, 1), ["msBack"] = Math.Round(t.Elapsed.TotalMilliseconds, 1) }.ToJsonString();
    }

    /// <summary>
    /// A heavier model for timing: a 24 × 20 m storey divided into 4 m rooms
    /// (13 walls crossing and joining), a door in every cross wall, a window in
    /// every long one, the floor and a hip roof over it; built by commands, as
    /// the interface would, then the whole storey rebuilt.
    /// </summary>
    public static string Heavy()
    {
        var session = _session = Create();
        var t = Stopwatch.StartNew();
        JsonNode? Run(string c, JsonObject a) { session.Execute(c, a, out var r); return r; }
        Run("storey.create", new JsonObject { ["name"] = "Ground floor" });
        var walls = new List<string>();
        for (var y = 0; y <= 20; y += 4) walls.Add((string)Run("architecture.addWall", new JsonObject { ["x1"] = 0, ["y1"] = y, ["x2"] = 24, ["y2"] = y })!["id"]!);
        for (var x = 0; x <= 24; x += 4) walls.Add((string)Run("architecture.addWall", new JsonObject { ["x1"] = x, ["y1"] = 0, ["x2"] = x, ["y2"] = 20 })!["id"]!);
        foreach (var (w, i) in walls.Select((w, i) => (w, i)))
            for (var k = 0; k < 5; k++)
                try { Run(i < 6 ? "architecture.addWindow" : "architecture.addDoor", new JsonObject { ["wallId"] = w, ["along"] = 2 + k * 4 }); } catch (CommandException) { }
        Run("architecture.addSlab", new JsonObject { ["x1"] = 0, ["y1"] = 0, ["x2"] = 24, ["y2"] = 20 });
        Run("architecture.addRoof", new JsonObject { ["points"] = new JsonArray(new JsonArray(0, 0), new JsonArray(24, 0), new JsonArray(24, 20), new JsonArray(0, 20)), ["style"] = "traditional_campie" });
        var buildMs = t.Elapsed.TotalMilliseconds;
        t.Restart();
        var snapshot = session.Snapshot();
        var snapshotMs = t.Elapsed.TotalMilliseconds;
        var rebuild = Rebuild();
        double volume = 0;
        foreach (var e in session.Model.Entities) if (session.Catalog.Geometry(e.Class) != null) volume += session.Geometry(e.Id)?.Parts.Sum(p => Measure.Volume(p.Solid)) ?? 0;
        return new JsonObject
        {
            ["entities"] = session.Model.Entities.Count(), ["renderItems"] = snapshot.Render.Count, ["volume"] = Math.Round(volume, 6),
            ["ms"] = new JsonObject { ["build"] = Math.Round(buildMs, 1), ["snapshot"] = Math.Round(snapshotMs, 1) }, ["rebuild"] = JsonNode.Parse(rebuild),
        }.ToJsonString();
    }

    /// <summary>Two cubes of 2 m, the second moved 1 m along each axis: their difference is 7 m³, their union 15, their intersection 1.</summary>
    public static string Booleans()
    {
        var b = _session?.Catalog.Get<ISolidBooleans>() ?? Create().Catalog.Get<ISolidBooleans>();
        if (b == null) return "{\"booleans\":false}";
        var a = Prism.Box(new Vec3(1, 1, 1), 2, 2, 2, "a")!;
        var c = Prism.Box(new Vec3(2, 2, 2), 2, 2, 2, "c")!;
        string V(Func<Solid?> f) { try { return f() is { } s ? Math.Round(Measure.Volume(s), 9).ToString(System.Globalization.CultureInfo.InvariantCulture) : "null"; } catch (Exception ex) { return "ERR " + ex.Message; } }
        return new JsonObject { ["a"] = Measure.Volume(a), ["difference"] = V(() => b.Difference(a, [c])), ["union"] = V(() => b.Union([a, c])), ["intersection"] = V(() => b.Intersection(a, c)) }.ToJsonString();
    }
}
