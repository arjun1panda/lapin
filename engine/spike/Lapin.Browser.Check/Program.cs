// The probe natively: the reference the browser's numbers are compared with.
var file = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "../../../../Lapin.Browser/wwwroot/testLapin.lapin");
Console.WriteLine(Lapin.Spike.Probe.Open(File.ReadAllBytes(file), Path.Combine(Path.GetTempPath(), "lapin-spike")));
Console.WriteLine(Lapin.Spike.Probe.Rebuild());
Console.WriteLine(Lapin.Spike.Probe.Heavy());
Console.WriteLine(Lapin.Spike.Probe.Booleans());
