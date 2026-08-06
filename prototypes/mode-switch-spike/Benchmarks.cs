// PROTOTYPE - NOT FOR PRODUCTION
// Question: what does the mode switch and the dispatch loop actually COST?
// Date: 2026-07-26

using System.Diagnostics;

namespace ModeSwitchSpike;

public static class Benchmarks
{
    public static void RunAll()
    {
        Console.WriteLine("\n=== COST ===");
        DispatchCost();
        SwitchCost();
        ReconcileCost();
        AllocationInSteadyState();
    }

    private static void DispatchCost()
    {
        var f = new Fixture();
        for (int i = 0; i < 10; i++) f.Colonists.Spawn($"C{i}", new CellCoord(i, 0, 0), 10);
        f.RaidTrigger.Armed = false;

        f.PumpRealTime(100);                                    // warm up
        const int frames = 20_000;
        var sw = Stopwatch.StartNew();
        f.PumpRealTime(frames);
        sw.Stop();
        double perDispatch = sw.Elapsed.TotalMilliseconds / frames;
        Console.WriteLine($"  RealTime dispatch (7 systems, 10 colonists): {perDispatch * 1000:F3} us/sub-step " +
                          $"= {perDispatch / 16.6 * 100:F3}% of a 16.6 ms frame at 1x");
        Console.WriteLine($"  at 3x speed that is {perDispatch * 3 / 16.6 * 100:F3}% of the frame budget");
    }

    private static void SwitchCost()
    {
        const int reps = 2000;
        var sw = new Stopwatch();
        double total = 0;
        for (int r = 0; r < reps; r++)
        {
            var f = new Fixture();
            EntityId a = f.Colonists.Spawn("A", new CellCoord(8, 8, 0), 10);
            var data = new SwitchTransitionData(1, SwitchTriggerReason.Breach, [], [a]);
            sw.Restart();
            f.Manager.RequestSwitch(TimeAuthorityMode.TurnBased, data);
            sw.Stop();
            total += sw.Elapsed.TotalMilliseconds;
        }
        Console.WriteLine($"  RealTime -> TurnBased swap: {total / reps * 1000:F2} us " +
                          "(authority swap only — no state conversion, by construction)");
    }

    private static void ReconcileCost()
    {
        const int reps = 500;
        double total = 0;
        var sw = new Stopwatch();
        for (int r = 0; r < reps; r++)
        {
            var f = new Fixture();
            EntityId keeper = f.Colonists.Spawn("K", new CellCoord(10, 10, 0), 10);
            EntityId doomed = f.Colonists.Spawn("D", new CellCoord(20, 20, 0), 10);
            for (int i = 0; i < 50; i++) f.Jobs.Jobs.Add(new Job(i, new CellCoord(i % 30, 1, 0), keeper));
            for (int i = 0; i < 50; i++) f.Pathfinding.CachedPaths.Add([new CellCoord(i % 30, 2, 0)]);
            for (int i = 0; i < 20; i++) f.Reservations.Reserve(new EntityId(10_000 + i), doomed, 1);

            f.RaidTrigger.ThreatPerSecond = 10_000;
            f.PumpRealTime(1);
            f.TurnOrder.Participants.AddRange([keeper, doomed]);
            using (f.Manager.Window.Open()) f.Targeting.DamageColonist(doomed, 999);
            f.TurnOrder.EndBattle(true);
            f.Manager.RequestSwitch(TimeAuthorityMode.RealTime,
                new SwitchTransitionData(1, SwitchTriggerReason.Breach, [], []));
            f.RaidTrigger.Armed = false;

            sw.Restart();
            f.PumpRealTime(1);            // the reconcile pass runs inside this dispatch
            sw.Stop();
            total += sw.Elapsed.TotalMilliseconds;
        }
        Console.WriteLine($"  PostEncounterReconcile (50 jobs, 50 paths, 20 reservations, 1 death, 3 raiders): " +
                          $"{total / reps * 1000:F1} us — a one-off at battle end");
    }

    private static void AllocationInSteadyState()
    {
        var f = new Fixture();
        for (int i = 0; i < 10; i++) f.Colonists.Spawn($"C{i}", new CellCoord(i, 0, 0), 10);
        f.RaidTrigger.Armed = false;
        f.PumpRealTime(500);

        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        int gen0Before = GC.CollectionCount(0);
        long allocBefore = GC.GetAllocatedBytesForCurrentThread();
        const int frames = 20_000;
        f.PumpRealTime(frames);
        long alloc = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
        int gen0 = GC.CollectionCount(0) - gen0Before;
        Console.WriteLine($"  dispatch-path allocation: {(double)alloc / frames:F2} B/sub-step over {frames:N0} sub-steps, " +
                          $"Gen0 collections: {gen0}");
        Console.WriteLine($"  VERDICT: {(alloc / (double)frames < 1.0 ? "ZERO steady-state allocation in the dispatch path" : "ALLOCATION PRESENT — investigate")}");
    }
}
