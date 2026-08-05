using Hollowdeep.Core.Entities;
using Hollowdeep.Core.Primitives;
using Hollowdeep.Core.Terrain;

namespace Hollowdeep.Core.Path;

/// <summary>
/// Who is moving and under which authority. Passed explicitly (never read off the
/// clock) so previews, tests, and pre-switch normalization all share one truth.
/// </summary>
public enum MoveContext : byte
{
    /// <summary>RT colonist: doors auto-open (+10 cost), occupancy advisory (ignored).</summary>
    ColonistRealTime = 0,
    /// <summary>RT raider: unbroken doors are walls to break; occupancy ignored.</summary>
    RaiderRealTime = 1,
    /// <summary>TB unit: closed doors block, occupied cells block (mover never blocks itself).</summary>
    TurnBased = 2,
}

/// <summary>Mode-aware composite walkability (§4d). One implementation for A*, regions, and normalization.</summary>
public sealed class Walkability
{
    private readonly TerrainWorld _terrain;
    private readonly DoorStore _doors;
    private readonly UnitOccupancyIndex _occupancy;

    public Walkability(TerrainWorld terrain, DoorStore doors, UnitOccupancyIndex occupancy)
    {
        _terrain = terrain;
        _doors = doors;
        _occupancy = occupancy;
    }

    /// <summary>Architecture-only walkability: in bounds, no wall, has a floor.</summary>
    public bool TerrainWalkable(CellCoord c)
    {
        if (!_terrain.InBounds(c)) return false;
        var cell = _terrain.Get(c);
        return !cell.HasWall && cell.HasFloor;
    }

    /// <summary>
    /// Physical openness for a mover: terrain plus door rules. Excludes occupancy —
    /// corner geometry and region connectivity never consult who is standing where.
    /// </summary>
    public bool PhysicallyOpen(CellCoord c, MoveContext ctx)
    {
        if (!TerrainWalkable(c)) return false;
        if (_doors.TryGetAt(c, out var door) && door.Blocks)
            return ctx == MoveContext.ColonistRealTime; // colonists auto-open; raiders/TB blocked
        return true;
    }

    /// <summary>Full enter check for a mover, occupancy included where the mode is exclusive.</summary>
    public bool CanEnter(CellCoord c, MoveContext ctx, EntityId mover)
    {
        if (!PhysicallyOpen(c, ctx)) return false;
        if (ctx == MoveContext.TurnBased && _occupancy.IsBlockedFor(c, mover)) return false;
        return true;
    }

    /// <summary>
    /// §4d diagonal rule: a diagonal step is legal iff at least one of its two orthogonal
    /// neighbors is physically open — a 1-cell-thick diagonal wall seals; clearing one
    /// orthogonal neighbor legalizes the step. Corner-cutting through two touching wall
    /// corners is banned.
    /// </summary>
    public bool DiagonalLegal(CellCoord from, int dx, int dy, MoveContext ctx)
    {
        return PhysicallyOpen(from.Offset(dx, 0), ctx) || PhysicallyOpen(from.Offset(0, dy), ctx);
    }

    /// <summary>Extra cost to enter a cell beyond the base step cost. Only working doors surcharge.</summary>
    public int EnterSurcharge(CellCoord c, MoveContext ctx)
    {
        if (ctx == MoveContext.ColonistRealTime && _doors.TryGetAt(c, out var door) && door.Blocks)
            return GameConfig.DoorTraversalSurcharge;
        return 0;
    }

    /// <summary>Stairs link Z↔Z+1 both directions: the stair cell is the LOWER endpoint.</summary>
    public bool HasStairLink(CellCoord lower)
        => _terrain.InBounds(lower) && _terrain.Get(lower).Floor == FloorKind.Stair;
}
