// PROTOTYPE - NOT FOR PRODUCTION
// Question: Tier 0 pathfinding spike — ADR-0003 criterion 5 and the concept doc's
//   "can pathfinding stay correct and performant while the player digs mid-route?"
// Date: 2026-07-26

using PathfindingSpike;

Console.WriteLine("HOLLOWDEEP — TIER 0 PATHFINDING SPIKE");
Console.WriteLine($"runtime: .NET {Environment.Version}   (headless: zero Godot types)");
Console.WriteLine("Composite walkability = terrain (ADR-0002) AND doors AND occupancy (ADR-0003), mode-aware.");

int failures = Tests.RunAll();
Benchmarks.RunAll();

Console.WriteLine(failures == 0
    ? "\nCONTRACT: all checks passed."
    : $"\nCONTRACT: {failures} FAILURES.");
return failures == 0 ? 0 : 1;
