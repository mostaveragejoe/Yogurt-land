// PROTOTYPE - NOT FOR PRODUCTION
// Question: Tier 0 save/load spike — the last gate on ADR-0003 and cross-cutting contract #2.
// Date: 2026-07-26

using SaveLoadSpike;

Console.WriteLine("HOLLOWDEEP — TIER 0 SAVE/LOAD SPIKE");
Console.WriteLine($"runtime: .NET {Environment.Version}   (headless: zero Godot types)");
Console.WriteLine("Serialization contract: plain data, stable IDs only, derived state rebuilt, schema versioned.");

int failures = Tests.RunAll();
Benchmarks.RunAll();

Console.WriteLine(failures == 0
    ? "\nCONTRACT: all checks passed."
    : $"\nCONTRACT: {failures} FAILURES.");
return failures == 0 ? 0 : 1;
