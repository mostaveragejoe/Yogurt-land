// PROTOTYPE - NOT FOR PRODUCTION
// Question: is pathfinding + invalidation affordable at MVP scale on a 16.6 ms budget?
// Date: 2026-07-26

using System.Diagnostics;

namespace PathfindingSpike;

public static class Benchmarks
{
    private const int MvpX = 128, MvpY = 128, MvpLayers = 16;

    /// <summary>A plausible colony: rooms and corridors carved into solid rock, deterministic.</summary>
    private static Fixture BuildColony(int seed = 7, bool diagonals = true)
    {
        var f = new Fixture(MvpX, MvpY, MvpLayers, diagonals);
        var rng = new Random(seed);
        using (f.Window.Open())
        {
            for (int z = 0; z < MvpLayers; z++)
            {
                // Rooms
                for (int room = 0; room < 10; room++)
                {
                    int rx = rng.Next(MvpX - 14), ry = rng.Next(MvpY - 14), rw = rng.Next(5, 13), rh = rng.Next(5, 13);
                    for (int y = ry; y < Math.Min(ry + rh, MvpY); y++)
                        for (int x = rx; x < Math.Min(rx + rw, MvpX); x++)
                            f.World.ClearWall(new CellCoord(x, y, z));
                }
                // Trunk corridors so the layer is well connected
                for (int x = 1; x < MvpX - 1; x++) f.World.ClearWall(new CellCoord(x, MvpY / 2, z));
                for (int y = 1; y < MvpY - 1; y++) f.World.ClearWall(new CellCoord(MvpX / 2, y, z));
            }
            // Stairs linking every layer at a fixed shaft
            for (int z = 0; z < MvpLayers - 1; z++)
                f.World.SetFloor(new CellCoord(MvpX / 2, MvpY / 2, z), f.Stair, 0);
        }
        f.Regions.Rebuild();
        return f;
    }

    public static void RunAll()
    {
        Console.WriteLine("\n=== COST (MVP scale: 128x128x16 = 262,144 cells) ===");
        DiagonalCostDelta();
        SinglePaths();
        ColonyFrame();
        RegionRebuild();
        InvalidationUnderDigging();
        Allocation();
    }

    /// <summary>Turns the "roughly 2x" estimate into a measurement.</summary>
    private static void DiagonalCostDelta()
    {
        Console.WriteLine("  -- 8-connected (adopted) vs 4-connected, same colony --");
        var path = new List<CellCoord>(512);
        var mover = new EntityId(1);
        var a = new CellCoord(10, 64, 0);
        var b = new CellCoord(120, 64, 0);

        foreach (bool diagonals in new[] { true, false })
        {
            Fixture f = BuildColony(diagonals: diagonals);
            for (int w = 0; w < 50; w++) f.Pathfinder.FindPath(a, b, TimeAuthorityMode.RealTime, mover, path);
            const int reps = 500;
            var sw = Stopwatch.StartNew();
            PathResult r = default;
            for (int i = 0; i < reps; i++) r = f.Pathfinder.FindPath(a, b, TimeAuthorityMode.RealTime, mover, path);
            sw.Stop();

            for (int w = 0; w < 2; w++) f.Regions.Rebuild();
            var sw2 = Stopwatch.StartNew();
            for (int i = 0; i < 10; i++) f.Regions.Rebuild();
            sw2.Stop();

            Console.WriteLine($"  {(diagonals ? "8-connected" : "4-connected"),-14} A* {sw.Elapsed.TotalMilliseconds / reps * 1000,7:F1} us   " +
                              $"len={path.Count,4}  expanded={r.NodesExpanded,6}  cost={r.Cost,5}   " +
                              $"regions {sw2.Elapsed.TotalMilliseconds / 10,5:F2} ms ({f.Regions.RegionCount})");
        }
    }

    private static void SinglePaths()
    {
        Fixture f = BuildColony();
        var path = new List<CellCoord>(512);
        var mover = new EntityId(1);

        (string label, CellCoord a, CellCoord b)[] cases =
        [
            ("short (same room, ~10 cells)", new CellCoord(64, 60, 0), new CellCoord(64, 70, 0)),
            ("medium (cross-layer trunk)",   new CellCoord(10, 64, 0), new CellCoord(120, 64, 0)),
            ("long (corner to corner)",      new CellCoord(1, 64, 0),  new CellCoord(126, 64, 0)),
            ("descent (5 layers via stairs)", new CellCoord(64, 60, 0), new CellCoord(64, 64, 5)),
        ];

        foreach ((string label, CellCoord a, CellCoord b) in cases)
        {
            for (int w = 0; w < 50; w++) f.Pathfinder.FindPath(a, b, TimeAuthorityMode.RealTime, mover, path);
            const int reps = 500;
            var sw = Stopwatch.StartNew();
            PathResult r = default;
            for (int i = 0; i < reps; i++) r = f.Pathfinder.FindPath(a, b, TimeAuthorityMode.RealTime, mover, path);
            sw.Stop();
            double us = sw.Elapsed.TotalMilliseconds / reps * 1000;
            Console.WriteLine($"  {label,-32} {us,8:F1} us  status={r.Status,-8} len={path.Count,4} expanded={r.NodesExpanded,6}" +
                              $"  = {us / 1000 / 16.6 * 100:F2}% frame");
        }
    }

    private static void ColonyFrame()
    {
        Fixture f = BuildColony();
        var path = new List<CellCoord>(512);
        // 10 colonists (MVP cap) each requesting a fresh medium-length path in the same frame:
        // the pessimistic case — in practice a colonist paths once per job, not once per frame.
        const int colonists = 10;
        var starts = new CellCoord[colonists];
        var goals = new CellCoord[colonists];
        for (int i = 0; i < colonists; i++)
        {
            starts[i] = new CellCoord(10 + i, 64, 0);
            goals[i] = new CellCoord(110 - i, 64, 0);
        }
        for (int w = 0; w < 20; w++)
            for (int i = 0; i < colonists; i++)
                f.Pathfinder.FindPath(starts[i], goals[i], TimeAuthorityMode.RealTime, new EntityId(i + 1), path);

        const int frames = 200;
        var sw = Stopwatch.StartNew();
        for (int fr = 0; fr < frames; fr++)
            for (int i = 0; i < colonists; i++)
                f.Pathfinder.FindPath(starts[i], goals[i], TimeAuthorityMode.RealTime, new EntityId(i + 1), path);
        sw.Stop();
        double perFrame = sw.Elapsed.TotalMilliseconds / frames;
        Console.WriteLine($"  10 colonists ALL repathing long routes in one frame: {perFrame:F3} ms = {perFrame / 16.6 * 100:F1}% of budget");
        Console.WriteLine($"    (pessimistic: real colonists path once per job assignment, not per frame)");
    }

    private static void RegionRebuild()
    {
        Fixture f = BuildColony();
        for (int w = 0; w < 3; w++) f.Regions.Rebuild();
        const int reps = 20;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < reps; i++) f.Regions.Rebuild();
        sw.Stop();
        double ms = sw.Elapsed.TotalMilliseconds / reps;
        Console.WriteLine($"  FULL region flood fill (whole world): {ms:F2} ms = {ms / 16.6 * 100:F1}% of a frame, " +
                          $"{f.Regions.RegionCount} regions");
        Console.WriteLine($"    -> {(ms < 16.6 ? "affordable as an occasional full rebuild" : "TOO EXPENSIVE per dig — needs incremental")}" +
                          "; must NOT run per dig (see spike note)");
    }

    private static void InvalidationUnderDigging()
    {
        Fixture f = BuildColony();
        // 10 colonists with live cached paths, then dig-heavy play.
        for (int i = 0; i < 10; i++)
            f.Cache.Add(new EntityId(i + 1), new CellCoord(10 + i, 64, 0), new CellCoord(110 - i, 64, 0),
                        TimeAuthorityMode.RealTime);

        // Every iteration must be a REAL mutation: ClearWall on an already-open cell is a no-op
        // and does not bump Revision, which would silently dilute the per-dig number. So collect
        // cells verified SOLID first, and dig each exactly once.
        var solid = new List<CellCoord>(2000);
        for (int z = 0; z < MvpLayers && solid.Count < 2000; z++)
            for (int y = 0; y < MvpY && solid.Count < 2000; y++)
                for (int x = 0; x < MvpX && solid.Count < 2000; x++)
                {
                    var c = new CellCoord(x, y, z);
                    if (f.World.GetCell(c).WallTypeId != 0) solid.Add(c);
                }

        ulong revBefore = f.World.Revision;
        var sw = Stopwatch.StartNew();
        using (f.Window.Open())
        {
            foreach (CellCoord c in solid)
            {
                f.World.ClearWall(c);                                   // guaranteed real mutation
                f.Cache.PollAndRevalidate(TimeAuthorityMode.RealTime);
            }
        }
        sw.Stop();
        long realMutations = (long)(f.World.Revision - revBefore);
        double perDig = sw.Elapsed.TotalMilliseconds / realMutations;
        Console.WriteLine($"  ({realMutations:N0} of {solid.Count:N0} operations were real mutations)");
        Console.WriteLine($"  dig + poll/revalidate 10 cached paths: {perDig * 1000:F1} us per dig = {perDig / 16.6 * 100:F2}% of a frame");
        Console.WriteLine($"    revalidations={f.Cache.RevalidationCount}, invalidated={f.Cache.InvalidatedCount}, " +
                          $"recomputes={f.Cache.RecomputeCount}");
        Console.WriteLine($"    -> {(perDig < 1.0 ? "Revision polling is AFFORDABLE — no change-list upgrade needed" : "polling too costly — trigger ADR-0003's change-list upgrade")}");
    }

    private static void Allocation()
    {
        Fixture f = BuildColony();
        var path = new List<CellCoord>(512);
        var mover = new EntityId(1);
        var a = new CellCoord(10, 64, 0);
        var b = new CellCoord(120, 64, 0);
        for (int w = 0; w < 100; w++) f.Pathfinder.FindPath(a, b, TimeAuthorityMode.RealTime, mover, path);

        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        int gen0Before = GC.CollectionCount(0);
        long allocBefore = GC.GetAllocatedBytesForCurrentThread();
        const int reps = 5000;
        for (int i = 0; i < reps; i++) f.Pathfinder.FindPath(a, b, TimeAuthorityMode.RealTime, mover, path);
        long alloc = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
        int gen0 = GC.CollectionCount(0) - gen0Before;
        Console.WriteLine($"  A* allocation: {(double)alloc / reps:F2} B/query over {reps:N0} long queries, Gen0: {gen0}");
        Console.WriteLine($"  VERDICT: {(alloc / (double)reps < 1.0 ? "ZERO steady-state allocation (generation-stamped scratch arrays)" : "ALLOCATION PRESENT — investigate")}");
    }
}
