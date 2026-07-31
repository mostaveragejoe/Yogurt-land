// PROTOTYPE - NOT FOR PRODUCTION
// Question: does ADR-0003's mode-aware composite walkability (terrain ∧ doors ∧ occupancy)
//   hold, and does Revision-polling invalidation survive mid-route digs inside budget?
// Date: 2026-07-26

namespace PathfindingSpike;

public readonly record struct EntityId(long Value)
{
    public static readonly EntityId None = new(0);
}

public enum TimeAuthorityMode { RealTime, TurnBased }

public struct DoorRecord
{
    public EntityId Id;
    public CellCoord Cell;
    public bool IsOpen;
    public int Hp;
    public bool IsBroken;
}

/// <summary>
/// ADR-0003: a door blocks like a wall but is an entity. Terrain stays ignorant of doors;
/// doors stay ignorant of pathfinding. Exposes BlocksMovement; Pathfinding composes.
/// </summary>
public sealed class DoorStore(MutationWindow window)
{
    private readonly List<DoorRecord> _records = [];
    private readonly Dictionary<CellCoord, int> _byCell = [];
    private long _nextId = 1;

    public long Revision { get; private set; }
    public int Count => _records.Count;
    public IReadOnlyList<DoorRecord> All => _records;

    public EntityId Spawn(CellCoord cell, int hp = 60)
    {
        var id = new EntityId(_nextId++);
        _byCell[cell] = _records.Count;
        _records.Add(new DoorRecord { Id = id, Cell = cell, Hp = hp });
        Revision++;
        return id;
    }

    public bool TryGet(CellCoord cell, out DoorRecord door)
    {
        if (_byCell.TryGetValue(cell, out int i)) { door = _records[i]; return true; }
        door = default; return false;
    }

    /// <summary>Closed, unbroken door on the cell (ADR-0003).</summary>
    public bool BlocksMovement(CellCoord cell) =>
        _byCell.TryGetValue(cell, out int i) && !_records[i].IsOpen && !_records[i].IsBroken;

    /// <summary>Closed, unbroken doors block LOS like walls (ADR-0003).</summary>
    public bool BlocksLos(CellCoord cell) => BlocksMovement(cell);

    public void SetOpen(CellCoord cell, bool open)
    {
        Assert();
        if (!_byCell.TryGetValue(cell, out int i)) return;
        DoorRecord d = _records[i];
        if (d.IsOpen == open) return;
        d.IsOpen = open;
        _records[i] = d;
        Revision++;
    }

    /// <summary>Hp -> 0 sets IsBroken, which unblocks IMMEDIATELY (same turn, ADR-0003).</summary>
    public void ApplyDamage(CellCoord cell, int amount)
    {
        Assert();
        if (!_byCell.TryGetValue(cell, out int i)) return;
        DoorRecord d = _records[i];
        d.Hp -= amount;
        if (d.Hp <= 0) { d.Hp = 0; d.IsBroken = true; }
        _records[i] = d;
        Revision++;
    }

    private void Assert()
    {
        if (!window.IsOpen) throw new InvalidOperationException("Door write outside the mutation window (ADR-0003).");
    }
}

/// <summary>Units only; exclusive under TurnBased, advisory under RealTime (ADR-0003).</summary>
public sealed class UnitOccupancyIndex
{
    private readonly Dictionary<CellCoord, List<EntityId>> _byCell = [];
    public long Revision { get; private set; }

    public void Place(EntityId id, CellCoord cell)
    {
        if (!_byCell.TryGetValue(cell, out List<EntityId>? l)) _byCell[cell] = l = [];
        l.Add(id);
        l.Sort(static (a, b) => a.Value.CompareTo(b.Value));
        Revision++;
    }

    public void Remove(EntityId id, CellCoord cell)
    {
        if (!_byCell.TryGetValue(cell, out List<EntityId>? l)) return;
        l.Remove(id);
        if (l.Count == 0) _byCell.Remove(cell);
        Revision++;
    }

    public bool IsOccupied(CellCoord c) => _byCell.TryGetValue(c, out List<EntityId>? l) && l.Count > 0;
    public bool IsOccupiedByOther(CellCoord c, EntityId self) =>
        _byCell.TryGetValue(c, out List<EntityId>? l) && l.Any(id => id.Value != self.Value);
    public IReadOnlyList<EntityId> At(CellCoord c) =>
        _byCell.TryGetValue(c, out List<EntityId>? l) ? l : Array.Empty<EntityId>();
    public void Clear() => _byCell.Clear();
}

/// <summary>
/// The composition ADR-0002 delegated and ADR-0003 specified. Pathfinding owns this —
/// terrain contributes IsPassableTerrain, doors contribute BlocksMovement, occupancy
/// blocks only under TurnBased.
/// </summary>
public sealed class CompositeWalkability(TerrainWorld world, DoorStore doors, UnitOccupancyIndex occupancy)
{
    public TerrainWorld World => world;
    public DoorStore Doors => doors;

    public bool IsWalkable(CellCoord c, TimeAuthorityMode mode, EntityId mover)
    {
        if (!world.InBounds(c)) return false;
        if (!world.IsPassableTerrain(c)) return false;

        if (mode == TimeAuthorityMode.TurnBased)
        {
            // Combat: closed doors block (opening is an action, Combat-GDD-era);
            // occupancy hard-blocks — tactics legality.
            if (doors.BlocksMovement(c)) return false;
            if (occupancy.IsOccupiedByOther(c, mover)) return false;
            return true;
        }

        // RealTime: colonists auto-open doors in transit; occupancy does NOT block
        // colony pathing in MVP (advisory) — the decision made concrete.
        DoorRecord d;
        if (doors.TryGet(c, out d) && !d.IsBroken && !d.IsOpen)
            return true;                       // auto-open: passable, at a cost (see StepCost)
        return true;
    }

    /// <summary>Auto-opening a door costs time in RealTime; free once open/broken.</summary>
    public int StepCost(CellCoord c, TimeAuthorityMode mode)
    {
        if (mode == TimeAuthorityMode.RealTime && doors.TryGet(c, out DoorRecord d) && !d.IsOpen && !d.IsBroken)
            return 2;                          // door-opening surcharge
        return 1;
    }
}
