// PROTOTYPE - NOT FOR PRODUCTION
// Question: ADR-0003 validation criterion 5 (mode-aware composite walkability) plus the
//   concept doc's "can pathfinding stay correct while the player digs mid-route?"
// Date: 2026-07-26

namespace PathfindingSpike;

public sealed class NullSink : ITerrainChangeSink
{
    public void Publish(in TerrainChangeBatch batch) { }
    public void PublishWorldReloaded() { }
}

public sealed class Fixture
{
    public MaterialCatalog Catalog { get; } = new();
    public MutationWindow Window { get; } = new();
    public TerrainWorld World { get; }
    public DoorStore Doors { get; }
    public UnitOccupancyIndex Occupancy { get; } = new();
    public CompositeWalkability Walk { get; }
    public Pathfinder Pathfinder { get; }
    public RegionIndex Regions { get; }
    public PathCache Cache { get; }
    public ushort Dirt, Floor, Stair;

    public Fixture(int sx = 48, int sy = 48, int layers = 4)
    {
        Dirt = Catalog.Register("dirt", 1, 30);
        Floor = Catalog.Register("rock_floor", 1, 0);
        Stair = Catalog.Register("stair", 1, 0);
        World = new TerrainWorld(new WorldBounds(sx, sy, layers), Catalog, new NullSink(), Window, 32);
        // Solid rock everywhere, then carve.
        World.PopulateForLoad(w =>
        {
            for (int z = 0; z < layers; z++)
                for (int y = 0; y < sy; y++)
                    for (int x = 0; x < sx; x++)
                        w.LoadSetCell(new CellCoord(x, y, z),
                            new TerrainCell { FloorTypeId = Floor, WallTypeId = Dirt, WallHp = 30 });
        });
        Doors = new DoorStore(Window);
        Walk = new CompositeWalkability(World, Doors, Occupancy);
        Pathfinder = new Pathfinder(Walk, Stair);
        Regions = new RegionIndex(Walk);
        Regions.SetStairId(Stair);
        Cache = new PathCache(Walk, Pathfinder);
    }

    public void Carve(int x0, int y0, int x1, int y1, int z)
    {
        using (Window.Open())
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    World.ClearWall(new CellCoord(x, y, z));
    }

    public List<CellCoord> Path(CellCoord a, CellCoord b, TimeAuthorityMode mode = TimeAuthorityMode.RealTime,
                                EntityId mover = default)
    {
        var p = new List<CellCoord>();
        Pathfinder.FindPath(a, b, mode, mover, p);
        return p;
    }
}

public static class Tests
{
    private static int _pass, _fail;

    private static void Check(bool cond, string name)
    {
        if (cond) { _pass++; Console.WriteLine($"  PASS  {name}"); }
        else { _fail++; Console.WriteLine($"  FAIL  {name}"); }
    }

    public static int RunAll()
    {
        Console.WriteLine("\n=== PATHFINDING: composite walkability, digs, doors, stairs ===");
        BasicPathing();
        DoorSemantics();
        OccupancySemantics();
        Stairs();
        MidRouteDigs();
        Reachability();
        Determinism();
        Console.WriteLine($"\n  {_pass} passed, {_fail} failed");
        return _fail;
    }

    private static void BasicPathing()
    {
        var f = new Fixture();
        f.Carve(1, 1, 20, 20, 0);
        var a = new CellCoord(2, 2, 0);
        var b = new CellCoord(18, 18, 0);
        List<CellCoord> p = f.Path(a, b);
        Check(p.Count > 0 && p[0] == a && p[^1] == b, "A* finds a path in an open room");
        Check(p.Count == 33, $"path length is Manhattan-optimal (33 cells for a 16+16 step, got {p.Count})");

        var blocked = new CellCoord(30, 30, 0);           // still solid rock
        var empty = new List<CellCoord>();
        PathResult r = f.Pathfinder.FindPath(a, blocked, TimeAuthorityMode.RealTime, EntityId.None, empty);
        Check(r.Status == PathStatus.InvalidGoal, "goal inside solid rock is rejected, not searched");

        f.Carve(30, 30, 32, 32, 0);                        // an isolated pocket
        r = f.Pathfinder.FindPath(a, new CellCoord(31, 31, 0), TimeAuthorityMode.RealTime, EntityId.None, empty);
        Check(r.Status == PathStatus.NoPath, "disconnected region yields NoPath");
    }

    private static void DoorSemantics()
    {
        // Two rooms joined only by a door cell.
        var f = new Fixture();
        f.Carve(1, 1, 8, 8, 0);
        f.Carve(10, 1, 18, 8, 0);
        f.Carve(9, 4, 9, 4, 0);                            // the doorway
        var door = new CellCoord(9, 4, 0);
        f.Doors.Spawn(door);

        var a = new CellCoord(2, 4, 0);
        var b = new CellCoord(17, 4, 0);

        List<CellCoord> rt = f.Path(a, b, TimeAuthorityMode.RealTime);
        Check(rt.Count > 0 && rt.Contains(door),
              "RealTime: colonists path THROUGH a closed door (auto-open in transit)");

        List<CellCoord> tb = f.Path(a, b, TimeAuthorityMode.TurnBased);
        Check(tb.Count == 0, "TurnBased: a closed door BLOCKS the path (opening is an action)");

        using (f.Window.Open()) f.Doors.SetOpen(door, true);
        tb = f.Path(a, b, TimeAuthorityMode.TurnBased);
        Check(tb.Count > 0 && tb.Contains(door), "TurnBased: an open door is passable");

        using (f.Window.Open()) f.Doors.SetOpen(door, false);
        Check(f.Path(a, b, TimeAuthorityMode.TurnBased).Count == 0, "closing the door blocks combat pathing again");

        using (f.Window.Open()) f.Doors.ApplyDamage(door, 999);
        Check(f.Doors.BlocksMovement(door) == false,
              "a BROKEN door unblocks IMMEDIATELY — combat gets its breach the same turn (ADR-0003)");
        Check(f.Path(a, b, TimeAuthorityMode.TurnBased).Count > 0, "TurnBased path opens through the breached door");
        Check(f.Doors.BlocksLos(door) == false, "broken door stops blocking LOS too");
    }

    private static void OccupancySemantics()
    {
        var f = new Fixture();
        f.Carve(1, 4, 18, 4, 0);                           // a 1-wide corridor
        var mover = new EntityId(1);
        var blocker = new EntityId(2);
        var a = new CellCoord(1, 4, 0);
        var b = new CellCoord(18, 4, 0);
        f.Occupancy.Place(blocker, new CellCoord(9, 4, 0));

        List<CellCoord> rt = f.Path(a, b, TimeAuthorityMode.RealTime, mover);
        Check(rt.Count > 0, "RealTime: occupancy does NOT block colony pathing (advisory) — no traffic deadlock");

        List<CellCoord> tb = f.Path(a, b, TimeAuthorityMode.TurnBased, mover);
        Check(tb.Count == 0, "TurnBased: a unit in a 1-wide corridor BLOCKS the path (tactics legality)");

        List<CellCoord> self = f.Path(new CellCoord(9, 4, 0), b, TimeAuthorityMode.TurnBased, blocker);
        Check(self.Count > 0, "a unit is not blocked by ITSELF");
    }

    private static void Stairs()
    {
        var f = new Fixture();
        f.Carve(1, 1, 10, 10, 0);
        f.Carve(1, 1, 10, 10, 1);
        var a = new CellCoord(2, 2, 0);
        var below = new CellCoord(2, 2, 1);

        Check(f.Path(a, below).Count == 0, "no vertical movement without a stair (layers are disconnected)");

        var stairCell = new CellCoord(5, 5, 0);
        using (f.Window.Open()) f.World.SetFloor(stairCell, f.Stair, 0);

        List<CellCoord> p = f.Path(a, below);
        Check(p.Count > 0 && p.Contains(stairCell) && p.Contains(new CellCoord(5, 5, 1)),
              "a stair floor Z-links its cell to the layer BELOW (ADR-0002 rule)");
        Check(p[^1] == below, "descent path reaches the target on the lower layer");

        List<CellCoord> up = f.Path(below, a);
        Check(up.Count > 0 && up.Contains(stairCell), "the same stair carries movement upward");
    }

    private static void MidRouteDigs()
    {
        // The concept doc's open question, directly.
        var f = new Fixture();
        f.Carve(1, 1, 20, 20, 0);
        var mover = new EntityId(1);
        var a = new CellCoord(2, 2, 0);
        var b = new CellCoord(18, 18, 0);

        PathCache.Entry e = f.Cache.Add(mover, a, b, TimeAuthorityMode.RealTime);
        int originalLength = e.Path.Count;
        Check(originalLength > 0, "colonist has a live cached path");

        // Nothing changed: polling must be a no-op.
        f.Cache.PollAndRevalidate(TimeAuthorityMode.RealTime);
        Check(f.Cache.RevalidationCount == 0, "Revision polling costs NOTHING when the world has not changed");

        // A dig that does not touch the route: revision moves, path stays valid, no recompute.
        using (f.Window.Open()) f.World.ClearWall(new CellCoord(25, 25, 0));
        f.Cache.PollAndRevalidate(TimeAuthorityMode.RealTime);
        Check(f.Cache.RevalidationCount == 1 && f.Cache.RecomputeCount == 0,
              "an off-route dig triggers ONE revalidation and ZERO recomputes");
        Check(e.Path.Count == originalLength, "the path is untouched");

        // Now wall off the route mid-journey — the actual hazard.
        e.Cursor = 5;
        CellCoord onRoute = e.Path[10];
        using (f.Window.Open()) f.World.SetWall(onRoute, f.Dirt, 0);
        f.Cache.PollAndRevalidate(TimeAuthorityMode.RealTime);
        Check(f.Cache.RecomputeCount == 1, "a build ON the remaining route forces exactly one recompute");
        Check(e.Path.Count > 0 && !e.Path.Contains(onRoute), "the new path routes AROUND the new wall");
        Check(e.Path[0] == e.Path[0] && e.Path[^1] == b, "the recomputed path still reaches the goal");

        // Digging behind the colonist must NOT invalidate.
        int recomputesBefore = f.Cache.RecomputeCount;
        e.Cursor = e.Path.Count - 2;
        using (f.Window.Open()) f.World.SetWall(new CellCoord(2, 2, 0), f.Dirt, 0);   // where it came from
        f.Cache.PollAndRevalidate(TimeAuthorityMode.RealTime);
        Check(f.Cache.RecomputeCount == recomputesBefore,
              "changes BEHIND the cursor do not invalidate — only the remaining route matters");

        // Sealing the goal makes the recompute fail gracefully rather than corrupting state.
        // NOTE: movement is 4-connected (no diagonals), so the connecting passage must be
        // orthogonal — a corner-to-corner "diagonal" corridor does NOT connect. (This is the
        // routed diagonal-movement question made concrete; see spike note.)
        var g = new Fixture();
        g.Carve(1, 1, 10, 10, 0);
        g.Carve(14, 14, 18, 18, 0);
        var passage = new List<CellCoord>();
        for (int x = 11; x <= 14; x++) passage.Add(new CellCoord(x, 10, 0));
        for (int y = 11; y <= 14; y++) passage.Add(new CellCoord(14, y, 0));
        using (g.Window.Open()) foreach (CellCoord c in passage) g.World.ClearWall(c);

        PathCache.Entry e2 = g.Cache.Add(new EntityId(1), new CellCoord(2, 2, 0), new CellCoord(16, 16, 0),
                                         TimeAuthorityMode.RealTime);
        Check(e2.Path.Count > 0, "path exists through the connecting passage");
        using (g.Window.Open()) foreach (CellCoord c in passage) g.World.SetWall(c, g.Dirt, 0);
        g.Cache.PollAndRevalidate(TimeAuthorityMode.RealTime);
        Check(e2.Path.Count == 0, "sealing the only passage yields an empty path, not a stale one");
    }

    private static void Reachability()
    {
        var f = new Fixture();
        f.Carve(1, 1, 10, 10, 0);
        f.Carve(20, 20, 30, 30, 0);
        f.Regions.Rebuild();
        Check(f.Regions.RegionCount == 2, $"flood fill labels 2 disconnected regions (got {f.Regions.RegionCount})");
        Check(f.Regions.AreConnected(new CellCoord(2, 2, 0), new CellCoord(9, 9, 0)), "same room = connected");
        Check(!f.Regions.AreConnected(new CellCoord(2, 2, 0), new CellCoord(25, 25, 0)), "separate rooms = unreachable");

        Check(!f.Regions.IsStale, "region index is fresh right after rebuild");
        using (f.Window.Open()) f.World.ClearWall(new CellCoord(40, 40, 0));
        Check(f.Regions.IsStale, "a terrain mutation marks the region index STALE (Revision poll)");

        // Joining the rooms must merge the regions.
        f.Carve(11, 10, 19, 20, 0);
        f.Regions.Rebuild();
        Check(f.Regions.AreConnected(new CellCoord(2, 2, 0), new CellCoord(25, 25, 0)),
              "digging a connecting corridor merges the regions — reachability updates");
    }

    private static void Determinism()
    {
        var f = new Fixture();
        f.Carve(1, 1, 30, 30, 0);
        using (f.Window.Open())
            for (int i = 5; i < 25; i++) f.World.SetWall(new CellCoord(i, 15, 0), f.Dirt, 0);   // a baffle

        var a = new CellCoord(2, 2, 0);
        var b = new CellCoord(28, 28, 0);
        List<CellCoord> p1 = f.Path(a, b);
        List<CellCoord> p2 = f.Path(a, b);
        Check(p1.SequenceEqual(p2), "identical queries return IDENTICAL paths (deterministic tie-breaking)");

        var g = new Fixture();
        g.Carve(1, 1, 30, 30, 0);
        using (g.Window.Open())
            for (int i = 5; i < 25; i++) g.World.SetWall(new CellCoord(i, 15, 0), g.Dirt, 0);
        Check(g.Path(a, b).SequenceEqual(p1), "a fresh world with the same inputs produces the same path");
    }
}
