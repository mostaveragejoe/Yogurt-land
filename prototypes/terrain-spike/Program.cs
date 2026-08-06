// PROTOTYPE - NOT FOR PRODUCTION
// Question: Tier 0 terrain spike — validate ADR-0002 and produce the numbers that
//   gate its promotion to Accepted (chunk size, memory, Gen0, sweep, extraction).
// Date: 2026-07-25

using TerrainSpike;

Console.WriteLine("HOLLOWDEEP — TIER 0 TERRAIN SPIKE (data-model half)");
Console.WriteLine($"runtime: .NET {Environment.Version}   GC: {(System.Runtime.GCSettings.IsServerGC ? "server" : "workstation")}   cores: {Environment.ProcessorCount}");
Console.WriteLine("Implements ADR-0002 verbatim: chunked AoS of 8-byte cells behind one TerrainWorld facade.");

int failures = Correctness.RunAll();

Benchmarks.Memory();
Benchmarks.WalkabilitySweep();
Benchmarks.DigHeavyAllocation();
Benchmarks.BulkApply();
Benchmarks.RenderExtraction();
Benchmarks.SnapshotCost();

Console.WriteLine(failures == 0
    ? "\nCONTRACT: all correctness checks passed."
    : $"\nCONTRACT: {failures} FAILURES — ADR-0002 needs revision before Accepted.");
return failures == 0 ? 0 : 1;
