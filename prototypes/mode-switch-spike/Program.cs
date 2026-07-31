// PROTOTYPE - NOT FOR PRODUCTION
// Question: Tier 0 mode-switch spike — validate ADR-0001 (and the ADR-0003 contracts that
//   meet it at the seam) before either is promoted to Accepted.
// Date: 2026-07-26

using ModeSwitchSpike;

Console.WriteLine("HOLLOWDEEP — TIER 0 MODE-SWITCH SPIKE");
Console.WriteLine($"runtime: .NET {Environment.Version}   (headless: zero Godot types in this assembly)");
Console.WriteLine("Implements ADR-0001 verbatim: strategy pattern over one shared world, plain C#.");

int failures = Tests.RunAll();
Benchmarks.RunAll();

Console.WriteLine(failures == 0
    ? "\nCONTRACT: all checks passed."
    : $"\nCONTRACT: {failures} FAILURES — ADR-0001 needs revision before Accepted.");
return failures == 0 ? 0 : 1;
