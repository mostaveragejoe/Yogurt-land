using Hollowdeep.Core;
using Hollowdeep.Core.Entities;
using Hollowdeep.Core.Path;
using Hollowdeep.Core.Primitives;
using Hollowdeep.Core.Sim;
using Hollowdeep.Core.Terrain;
using Xunit;

namespace Hollowdeep.Core.Tests;

/// <summary>Minimal world rig for path tests: one open floor layer at z=0, walls added per test.</summary>
public sealed class PathRig
{
    public MutationGate Gate = new();
    public TerrainWorld Terrain;
    public UnitOccupancyIndex Occupancy = new();
    public DoorStore Doors;
    public ColonistStore Colonists;
    public RaiderStore Raiders;
    public EntityIdAllocator Ids = new();
    public Walkability Walk;
    public Pathfinder Path;
    public List<CellCoord> Out = new();

    public PathRig(int floors = 2)
    {
        Terrain = new TerrainWorld(64, 64, 8, Gate);
        Doors = new DoorStore(Gate);
        Colonists = new ColonistStore(Gate, Occupancy);
        Raiders = new RaiderStore(Gate, Occupancy);
        Walk = new Walkability(Terrain, Doors, Occupancy);
        Path = new Pathfinder(Terrain, Doors, Walk);
        using var _ = Gate.Open();
        for (int z = 0; z < floors; z++)
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                    Terrain.SetFloor(new CellCoord(x, y, z), FloorKind.Natural, MaterialId.Dirt);
    }

    public void Wall(int x, int y, int z = 0, MaterialId mat = MaterialId.Granite)
    {
        using var _ = Gate.Open();
        Terrain.BuildWall(new CellCoord(x, y, z), mat);
    }

    public bool Find(CellCoord a, CellCoord b, MoveContext ctx, EntityId mover = default,
        GoalMode mode = GoalMode.Exact, bool breach = false, int maxCost = int.MaxValue)
        => Path.FindPath(a, b, ctx, mover, mode, Out, breach, maxCost);
}

public class PathfindingTests
{
    [Fact]
    public void StraightLinePathHasOctileCosts()
    {
        var rig = new PathRig();
        Assert.True(rig.Find(new(0, 0, 0), new(3, 0, 0), MoveContext.ColonistRealTime));
        Assert.Equal(3, rig.Out.Count);
        Assert.Equal(new CellCoord(3, 0, 0), rig.Out[^1]);
    }

    [Fact]
    public void DiagonalSealedByOneCellThickDiagonalWall()
    {
        var rig = new PathRig();
        // Walls at (1,0) and (0,1): the diagonal (0,0)->(1,1) must be sealed (corner-cut ban).
        rig.Wall(1, 0);
        rig.Wall(0, 1);
        // Wall off the rest of the 2x2 pocket so the ONLY exit would be the sealed diagonal.
        rig.Wall(2, 0); rig.Wall(2, 1); rig.Wall(0, 2); rig.Wall(1, 2);
        Assert.False(rig.Find(new(0, 0, 0), new(5, 5, 0), MoveContext.ColonistRealTime));
        Assert.Empty(rig.Out); // sealed goal returns empty, not stale
    }

    [Fact]
    public void ClearingOneOrthogonalNeighborLegalizesTheDiagonal()
    {
        var rig = new PathRig();
        rig.Wall(1, 0);
        rig.Wall(0, 1);
        // maxCost 14 admits exactly one diagonal step — no routing around.
        Assert.False(rig.Find(new(0, 0, 0), new(1, 1, 0), MoveContext.ColonistRealTime, maxCost: 14));
        using (rig.Gate.Open())
            rig.Terrain.DigWall(new CellCoord(1, 0, 0)); // clear ONE orthogonal neighbor
        Assert.True(rig.Find(new(0, 0, 0), new(1, 1, 0), MoveContext.ColonistRealTime, maxCost: 14));
        Assert.Equal(new[] { new CellCoord(1, 1, 0) }, rig.Out); // the diagonal step itself
    }

    [Fact]
    public void SealedGoalReturnsEmptyNotStale()
    {
        var rig = new PathRig();
        // Box in the goal completely.
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
                if (dx != 0 || dy != 0)
                    rig.Wall(10 + dx, 10 + dy);
        rig.Out.Add(new CellCoord(9, 9, 9)); // stale garbage that must be cleared
        Assert.False(rig.Find(new(0, 0, 0), new(10, 10, 0), MoveContext.ColonistRealTime));
        Assert.Empty(rig.Out);
    }

    [Fact]
    public void PathIsDeterministicAcrossRepeatedRuns()
    {
        var rig = new PathRig();
        rig.Wall(5, 5); rig.Wall(5, 6); rig.Wall(6, 5);
        Assert.True(rig.Find(new(0, 0, 0), new(20, 17, 0), MoveContext.ColonistRealTime));
        var first = rig.Out.ToList();
        for (int i = 0; i < 5; i++)
        {
            Assert.True(rig.Find(new(0, 0, 0), new(20, 17, 0), MoveContext.ColonistRealTime));
            Assert.Equal(first, rig.Out);
        }
    }

    [Fact]
    public void StairsLinkLayersBothDirections()
    {
        var rig = new PathRig(floors: 2);
        using (rig.Gate.Open())
            rig.Terrain.SetFloor(new CellCoord(3, 3, 0), FloorKind.Stair, MaterialId.Granite);
        // Up: z0 -> z1.
        Assert.True(rig.Find(new(0, 0, 0), new(6, 6, 1), MoveContext.ColonistRealTime));
        Assert.Contains(new CellCoord(3, 3, 1), rig.Out);
        // Down: z1 -> z0.
        Assert.True(rig.Find(new(6, 6, 1), new(0, 0, 0), MoveContext.ColonistRealTime));
        Assert.Contains(new CellCoord(3, 3, 0), rig.Out);
    }

    [Fact]
    public void ClosedDoorRulesPerMode()
    {
        var rig = new PathRig();
        // Wall line at x=5 with a door gap at (5,3).
        for (int y = 0; y < 64; y++)
            if (y != 3) rig.Wall(5, y);
        using (rig.Gate.Open())
            rig.Doors.Add(rig.Ids, new CellCoord(5, 3, 0), MaterialId.Dirt);

        // RT colonist: door auto-opens (+10 surcharge on traversal).
        Assert.True(rig.Find(new(0, 3, 0), new(10, 3, 0), MoveContext.ColonistRealTime));
        Assert.Contains(new CellCoord(5, 3, 0), rig.Out);

        // RT raider: closed door is a wall to it.
        Assert.False(rig.Find(new(0, 3, 0), new(10, 3, 0), MoveContext.RaiderRealTime));

        // TB: closed door blocks anyone.
        Assert.False(rig.Find(new(0, 3, 0), new(10, 3, 0), MoveContext.TurnBased));
    }

    [Fact]
    public void BrokenDoorUnblocksMovementImmediately()
    {
        var rig = new PathRig();
        for (int y = 0; y < 64; y++)
            if (y != 3) rig.Wall(5, y);
        EntityId doorId;
        using (rig.Gate.Open())
            doorId = rig.Doors.Add(rig.Ids, new CellCoord(5, 3, 0), MaterialId.Dirt);
        Assert.False(rig.Find(new(0, 3, 0), new(10, 3, 0), MoveContext.TurnBased));

        using (rig.Gate.Open())
        {
            ref var d = ref rig.Doors.Ref(doorId);
            d.Hp = 0;
            d.IsBroken = true;
        }
        Assert.True(rig.Find(new(0, 3, 0), new(10, 3, 0), MoveContext.TurnBased));
    }

    [Fact]
    public void TurnBasedOccupancyBlocksButUnitNeverBlocksItself()
    {
        var rig = new PathRig();
        EntityId mover, blocker;
        using (rig.Gate.Open())
        {
            mover = rig.Colonists.Add(rig.Ids, "Mover", new CellCoord(0, 0, 0), false);
            blocker = rig.Colonists.Add(rig.Ids, "Blocker", new CellCoord(1, 0, 0), false);
        }
        // Corridor y=0 walled from y=1 so the blocker cannot be stepped around.
        for (int x = 0; x < 64; x++) rig.Wall(x, 1);

        // TB: blocked by the occupant.
        Assert.False(rig.Find(new(0, 0, 0), new(3, 0, 0), MoveContext.TurnBased, mover));
        // RT: occupancy is advisory — path goes through.
        Assert.True(rig.Find(new(0, 0, 0), new(3, 0, 0), MoveContext.ColonistRealTime, mover));
        // TB: the mover's own cell never blocks it (pathing away from itself).
        Assert.True(rig.Find(new(1, 0, 0), new(1, 0, 0), MoveContext.TurnBased, blocker));
    }

    [Fact]
    public void BreachPathPrefersWeakWallsAndFindsDigTargets()
    {
        var rig = new PathRig();
        // Full wall line at x=10: granite everywhere, one dirt cell at y=7.
        for (int y = 0; y < 64; y++)
            rig.Wall(10, y, 0, y == 7 ? MaterialId.Dirt : MaterialId.Granite);
        Assert.False(rig.Find(new(0, 7, 0), new(20, 7, 0), MoveContext.RaiderRealTime));
        Assert.True(rig.Find(new(0, 7, 0), new(20, 7, 0), MoveContext.RaiderRealTime, breach: true));
        Assert.Contains(new CellCoord(10, 7, 0), rig.Out); // crosses at the weak dirt cell
    }

    [Fact]
    public void AdjacentGoalModeStopsBesideTheTarget()
    {
        var rig = new PathRig();
        rig.Wall(10, 10); // dig target
        Assert.True(rig.Find(new(0, 10, 0), new(10, 10, 0), MoveContext.ColonistRealTime, mode: GoalMode.Adjacent8));
        var last = rig.Out[^1];
        Assert.True(CellCoord.Chebyshev(last, new CellCoord(10, 10, 0)) == 1);
    }

    [Fact]
    public void MaxCostBoundsThePath()
    {
        var rig = new PathRig();
        Assert.False(rig.Find(new(0, 0, 0), new(20, 0, 0), MoveContext.ColonistRealTime, maxCost: 50));
        Assert.True(rig.Find(new(0, 0, 0), new(5, 0, 0), MoveContext.ColonistRealTime, maxCost: 50));
    }
}

public class RegionIndexTests
{
    [Fact]
    public void RegionsSplitAndMergeAcrossWallsAndStairs()
    {
        var rig = new PathRig(floors: 2);
        var regions = new RegionIndex(rig.Terrain, rig.Walk);
        var a = new CellCoord(2, 2, 0);
        var b = new CellCoord(20, 2, 0);
        Assert.True(regions.Reachable(a, b));

        // Wall the layer in half at x=10 (full height so diagonals cannot slip through).
        using (rig.Gate.Open())
            for (int y = 0; y < 64; y++)
                rig.Terrain.BuildWall(new CellCoord(10, y, 0), MaterialId.Granite);
        Assert.False(regions.Reachable(a, b));

        // Upper layer is one open region: reach b's side via stairs on both halves.
        using (rig.Gate.Open())
        {
            rig.Terrain.SetFloor(new CellCoord(2, 5, 0), FloorKind.Stair, MaterialId.Dirt);
            rig.Terrain.SetFloor(new CellCoord(20, 5, 0), FloorKind.Stair, MaterialId.Dirt);
        }
        Assert.True(regions.Reachable(a, b));

        // Digging the wall reconnects directly (dirty-layer rebuild, not per-dig flood).
        using (rig.Gate.Open())
            rig.Terrain.DigWall(new CellCoord(10, 30, 0));
        Assert.True(regions.Reachable(a, b));
    }

    [Fact]
    public void RegionConnectivityMatchesAStarDiagonalRule()
    {
        var rig = new PathRig();
        // Diagonal wall seam: (i, i) and the cell above-right, creating sealed diagonals.
        for (int i = 0; i < 63; i++)
        {
            rig.Wall(i, i);
            rig.Wall(i + 1, i);
        }
        rig.Wall(63, 63);
        var regions = new RegionIndex(rig.Terrain, rig.Walk);
        var below = new CellCoord(30, 10, 0);
        var above = new CellCoord(10, 30, 0);
        bool aStar = rig.Find(below, above, MoveContext.ColonistRealTime);
        Assert.Equal(aStar, regions.Reachable(below, above));
        Assert.False(aStar); // the seam seals: same answer from both systems
    }
}
