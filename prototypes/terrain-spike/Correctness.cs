// PROTOTYPE - NOT FOR PRODUCTION
// Question: does the ADR-0002 contract actually hold as specified? (validation criteria 1-4)
// Date: 2026-07-25

namespace TerrainSpike;

/// <summary>Recording sink: stands in for the World Change Event Bus adapter.</summary>
public sealed class RecordingSink : ITerrainChangeSink
{
    public List<TerrainChange> Changes { get; } = [];
    public List<int> BatchSizes { get; } = [];
    public int WorldReloadedCount { get; private set; }
    public Action<TerrainChange>? OnChange;

    public void Publish(in TerrainChangeBatch batch)
    {
        BatchSizes.Add(batch.Changes.Length);
        foreach (TerrainChange c in batch.Changes) { Changes.Add(c); OnChange?.Invoke(c); }
    }
    public void PublishWorldReloaded() => WorldReloadedCount++;
    public void Reset() { Changes.Clear(); BatchSizes.Clear(); }
}

public static class Correctness
{
    private static int _pass, _fail;

    private static void Check(bool condition, string name)
    {
        if (condition) { _pass++; Console.WriteLine($"  PASS  {name}"); }
        else { _fail++; Console.WriteLine($"  FAIL  {name}"); }
    }

    private static void Throws<T>(Action a, string name) where T : Exception
    {
        try { a(); Check(false, name + " (no throw)"); }
        catch (T) { Check(true, name); }
        catch (Exception e) { Check(false, $"{name} (wrong exception {e.GetType().Name})"); }
    }

    public record Fixture(TerrainWorld World, RecordingSink Sink, MutationWindow Window,
                          MaterialCatalog Catalog, ushort Dirt, ushort Granite, ushort Reinforced, ushort Floor);

    public static Fixture NewWorld(int sizeX = 64, int sizeY = 64, int layers = 4, int chunkSize = 32)
    {
        var catalog = new MaterialCatalog();
        ushort dirt = catalog.Register("dirt", 1, 30);
        ushort granite = catalog.Register("granite", 2, 120);
        ushort reinforced = catalog.Register("reinforced", 3, 400);
        ushort floor = catalog.Register("rock_floor", 1, 0);
        var sink = new RecordingSink();
        var window = new MutationWindow();
        var world = new TerrainWorld(new WorldBounds(sizeX, sizeY, layers), catalog, sink, window, chunkSize);
        return new Fixture(world, sink, window, catalog, dirt, granite, reinforced, floor);
    }

    public static int RunAll()
    {
        Console.WriteLine("\n=== CORRECTNESS: ADR-0002 contract ===");
        MutationAndEvents();
        DamageAndRepair();
        BulkSemantics();
        WindowAssertions();
        LoadAndSnapshot();
        Determinism();
        Console.WriteLine($"\n  {_pass} passed, {_fail} failed");
        return _fail;
    }

    private static void MutationAndEvents()
    {
        var f = NewWorld();
        var c = new CellCoord(5, 6, 1);
        using (f.Window.Open())
        {
            f.World.SetFloor(c, f.Floor, 0);
            f.World.SetWall(c, f.Dirt, 0);
        }
        Check(f.Sink.Changes.Count == 2, "one change per mutating call");
        Check(f.Sink.BatchSizes.SequenceEqual([1, 1]), "single-cell writes publish one-entry batches");
        Check(f.Sink.Changes[1].Kind == TerrainChangeKind.WallPlaced, "WallPlaced kind");
        Check(f.Sink.Changes[1].Previous.WallTypeId == 0 && f.Sink.Changes[1].Previous.FloorTypeId == f.Floor,
              "Previous captures state BEFORE the change (CD-1)");
        Check(f.World.GetCell(c).WallHp == 30, "SetWall sets HP to catalog max");
        Check(!f.World.IsPassableTerrain(c), "wall blocks passability");

        f.Sink.Reset();
        using (f.Window.Open()) f.World.SetWall(c, f.Dirt, 0);
        Check(f.Sink.Changes.Count == 0, "no-op single write publishes nothing");

        f.Sink.Reset();
        using (f.Window.Open()) f.World.SetTerrainJobClaim(c, true);
        Check(f.Sink.Changes.Count == 0 && f.World.IsTerrainJobClaimed(c),
              "terrain-job-claim flips publish nothing (rule 2)");

        using (f.Window.Open()) f.World.ClearWall(c);
        Check(f.World.IsPassableTerrain(c), "floor present + no wall = passable");

        ulong rev = f.World.Revision;
        using (f.Window.Open()) f.World.SetWall(c, f.Granite, 0);
        Check(f.World.Revision == rev + 1, "Revision increments per mutating call");
    }

    private static void DamageAndRepair()
    {
        var f = NewWorld();
        var c = new CellCoord(2, 2, 0);
        using (f.Window.Open())
        {
            f.World.SetFloor(c, f.Floor, 0);
            f.World.SetWall(c, f.Granite, 0);       // 120 hp
            f.Sink.Reset();

            DamageResult d1 = f.World.ApplyWallDamage(c, 50);
            Check(d1 is { Outcome: DamageOutcome.Damaged, AppliedAmount: 50, RemainingHp: 70 }, "damage applies and reports");
            Check(f.Sink.Changes[^1].Kind == TerrainChangeKind.WallDamaged, "WallDamaged kind");

            RepairResult r1 = f.World.ApplyWallRepair(c, 30);
            Check(r1 is { Outcome: RepairOutcome.Repaired, RemainingHp: 100 }, "repair applies (CD-7)");
            RepairResult r2 = f.World.ApplyWallRepair(c, 999);
            Check(r2.Outcome == RepairOutcome.Repaired && r2.RemainingHp == 120, "repair clamps at catalog max");
            RepairResult r3 = f.World.ApplyWallRepair(c, 10);
            Check(r3.Outcome == RepairOutcome.AlreadyAtMax, "repair at max is a no-op result");

            f.Sink.Reset();
            DamageResult d2 = f.World.ApplyWallDamage(c, 999);
            Check(d2 is { Outcome: DamageOutcome.Destroyed, AppliedAmount: 120, RemainingHp: 0 }, "damage clamps; destroy at zero");
            Check(f.Sink.Changes[^1].Kind == TerrainChangeKind.WallRemoved, "destroyed wall reports WallRemoved");
            Check(f.Sink.Changes[^1].Previous.WallTypeId == f.Granite,
                  "Previous names the material tier that failed (CD-1)");
            Check(f.World.ApplyWallDamage(c, 5).Outcome == DamageOutcome.NoWall, "damage on empty cell = NoWall");
        }
    }

    private static void BulkSemantics()
    {
        var f = NewWorld();
        using (f.Window.Open())
        {
            // stair excavation: SetFloor at Z + ClearWall at Z+1, one atomic Apply
            for (int z = 0; z < 2; z++)
                for (int x = 0; x < 4; x++)
                    f.World.SetWall(new CellCoord(x, 0, z), f.Dirt, 0);
            f.Sink.Reset();

            var stair = new TerrainMutation[]
            {
                new(new CellCoord(1, 0, 0), TerrainMutationKind.SetFloor, f.Floor, 0, 0),
                new(new CellCoord(1, 0, 1), TerrainMutationKind.ClearWall, 0, 0, 0),
            };
            BulkResult ok = f.World.Apply(stair);
            Check(ok.Accepted && f.Sink.BatchSizes.Count == 1 && f.Sink.BatchSizes[0] == 2,
                  "stair excavation publishes exactly ONE batch (criterion 4)");
            Check(f.Sink.Changes[0].Cell.Z == 0 && f.Sink.Changes[1].Cell.Z == 1, "batch preserves caller order");

            f.Sink.Reset();
            var withDup = new TerrainMutation[]
            {
                new(new CellCoord(2, 0, 0), TerrainMutationKind.ClearWall, 0, 0, 0),
                new(new CellCoord(2, 0, 0), TerrainMutationKind.SetWall, f.Granite, 0, 0),
            };
            BulkResult dup = f.World.Apply(withDup);
            Check(!dup.Accepted && dup.FirstInvalidIndex == 1, "duplicate cell in batch rejects the whole batch");
            Check(f.World.GetCell(new CellCoord(2, 0, 0)).WallTypeId == f.Dirt, "rejected batch applied nothing");
            Check(f.Sink.Changes.Count == 0, "rejected batch published nothing");

            f.Sink.Reset();
            var withOob = new TerrainMutation[]
            {
                new(new CellCoord(3, 0, 0), TerrainMutationKind.ClearWall, 0, 0, 0),
                new(new CellCoord(999, 0, 0), TerrainMutationKind.ClearWall, 0, 0, 0),
            };
            BulkResult oob = f.World.Apply(withOob);
            Check(!oob.Accepted && oob.FirstInvalidIndex == 1, "out-of-bounds entry reports its index");
            Check(f.World.GetCell(new CellCoord(3, 0, 0)).WallTypeId == f.Dirt, "validate-all-then-apply: nothing applied");

            f.Sink.Reset();
            var withNoop = new TerrainMutation[]
            {
                new(new CellCoord(10, 10, 0), TerrainMutationKind.ClearWall, 0, 0, 0),   // already open: no-op
                new(new CellCoord(3, 0, 0), TerrainMutationKind.ClearWall, 0, 0, 0),     // real
            };
            f.World.Apply(withNoop);
            Check(f.Sink.BatchSizes.Count == 1 && f.Sink.BatchSizes[0] == 1, "no-op entries are dropped from the published batch");

            // multi-cell combat effect (AoE) = one batch
            f.Sink.Reset();
            var aoe = new TerrainMutation[6];
            for (int i = 0; i < 6; i++) aoe[i] = new TerrainMutation(new CellCoord(i, 5, 0), TerrainMutationKind.SetWall, f.Dirt, 0, 0);
            f.World.Apply(aoe);
            Check(f.Sink.BatchSizes.Count == 1 && f.Sink.BatchSizes[0] == 6, "multi-cell combat effect publishes ONE batch");
        }
    }

    private static void WindowAssertions()
    {
        var f = NewWorld();
        Throws<InvalidOperationException>(
            () => f.World.SetWall(new CellCoord(1, 1, 0), f.Dirt, 0),
            "write outside the mutation window throws (rule 5)");

        // bus handler attempting a write during Publish (the second forbidden path)
        var g = NewWorld();
        bool handlerThrew = false;
        g.Sink.OnChange = _ =>
        {
            try { g.World.SetWall(new CellCoord(9, 9, 0), g.Dirt, 0); }
            catch (InvalidOperationException) { handlerThrew = true; }
        };
        using (g.Window.Open()) g.World.SetWall(new CellCoord(1, 1, 0), g.Dirt, 0);
        Check(handlerThrew, "write from inside a bus handler throws (bookkeeping only)");

        var h = NewWorld();
        using (h.Window.Open())
            Throws<ArgumentOutOfRangeException>(
                () => h.World.SetWall(new CellCoord(500, 0, 0), h.Dirt, 0),
                "out-of-bounds single write throws (programming error)");
    }

    private static void LoadAndSnapshot()
    {
        var f = NewWorld();
        f.World.PopulateForLoad(w =>
        {
            for (int z = 0; z < 4; z++)
                for (int y = 0; y < 64; y++)
                    for (int x = 0; x < 64; x++)
                        w.LoadSetCell(new CellCoord(x, y, z),
                            new TerrainCell { FloorTypeId = f.Floor, WallTypeId = f.Dirt, WallHp = 30 });
        });
        Check(f.Sink.Changes.Count == 0 && f.Sink.WorldReloadedCount == 1,
              "load publishes WorldReloaded and ZERO per-cell events (rule 6)");

        using (f.Window.Open())
        {
            f.World.SetWall(new CellCoord(3, 3, 1), f.Granite, 2);
            f.World.ApplyWallDamage(new CellCoord(3, 3, 1), 40);
        }

        TerrainSnapshot snap = f.World.Snapshot();
        var g = NewWorld();
        g.World.Restore(snap);
        Check(g.Sink.WorldReloadedCount == 1, "Restore ends with WorldReloaded");

        bool identical = true;
        for (int z = 0; z < 4 && identical; z++)
            for (int y = 0; y < 64 && identical; y++)
                for (int x = 0; x < 64; x++)
                {
                    TerrainCell a = f.World.GetCell(new CellCoord(x, y, z)), b = g.World.GetCell(new CellCoord(x, y, z));
                    if (a.FloorTypeId != b.FloorTypeId || a.WallTypeId != b.WallTypeId ||
                        a.WallHp != b.WallHp || a.StyleId != b.StyleId || a.Flags != b.Flags)
                    { identical = false; break; }
                }
        Check(identical, "Snapshot -> Restore round-trip is byte-identical (criterion 2)");

        // catalog gains a material BEFORE the existing ones: ids must be remapped, not re-materialized
        var cat2 = new MaterialCatalog();
        cat2.Register("marble", 2, 200);                    // new id 1 — shifts everything
        ushort dirt2 = cat2.Register("dirt", 1, 30);
        cat2.Register("granite", 2, 120);
        cat2.Register("reinforced", 3, 400);
        cat2.Register("rock_floor", 1, 0);
        var sink2 = new RecordingSink(); var win2 = new MutationWindow();
        var world2 = new TerrainWorld(new WorldBounds(64, 64, 4), cat2, sink2, win2, 32);
        world2.Restore(snap);
        Check(world2.GetCell(new CellCoord(0, 0, 0)).WallTypeId == dirt2,
              "Restore REMAPS ids through the material manifest when the catalog is reordered");

        var cat3 = new MaterialCatalog();
        cat3.Register("dirt", 1, 30);                        // missing the rest
        var world3 = new TerrainWorld(new WorldBounds(64, 64, 4), cat3, new RecordingSink(), new MutationWindow(), 32);
        Throws<InvalidOperationException>(() => world3.Restore(snap), "unknown material key fails restore loudly");
    }

    private static void Determinism()
    {
        static (List<TerrainChange> events, List<TerrainCell> state) Run()
        {
            var f = NewWorld(32, 32, 2);
            var rng = new Random(12345);                     // fixed seed = the "same inputs" half
            using (f.Window.Open())
            {
                for (int i = 0; i < 500; i++)
                {
                    var c = new CellCoord(rng.Next(32), rng.Next(32), rng.Next(2));
                    switch (i % 4)
                    {
                        case 0: f.World.SetWall(c, f.Granite, 0); break;
                        case 1: f.World.ApplyWallDamage(c, rng.Next(1, 200)); break;
                        case 2: f.World.SetFloor(c, f.Floor, 0); break;
                        case 3: f.World.ApplyWallRepair(c, rng.Next(1, 50)); break;
                    }
                }
            }
            var state = new List<TerrainCell>();
            for (int z = 0; z < 2; z++) for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
                state.Add(f.World.GetCell(new CellCoord(x, y, z)));
            return (f.Sink.Changes, state);
        }

        var a = Run(); var b = Run();
        Check(a.events.Count == b.events.Count && a.events.SequenceEqual(b.events),
              "identical mutation sequences produce IDENTICAL EVENT STREAMS (rule 8)");
        bool same = a.state.Count == b.state.Count;
        for (int i = 0; same && i < a.state.Count; i++)
            same = a.state[i].FloorTypeId == b.state[i].FloorTypeId && a.state[i].WallTypeId == b.state[i].WallTypeId
                && a.state[i].WallHp == b.state[i].WallHp && a.state[i].Flags == b.state[i].Flags;
        Check(same, "identical mutation sequences produce byte-identical STATE (rule 8)");
    }
}
