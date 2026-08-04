using Hollowdeep.Core.Primitives;

namespace Hollowdeep.Core.Entities;

/// <summary>
/// Cell→unit occupancy. Advisory in RealTime (colonists may overlap; prevents traffic
/// deadlock), exclusive in TurnBased (occupied cells block). The ONLY writers are the
/// stores' position/death handling, synchronously with the position write (ADR-0003).
/// Invariant: in TurnBased the count at any cell is 0 or 1 (pre-switch normalization
/// establishes it; TB movement preserves it), so the Solo occupant is always known there.
/// </summary>
public sealed class UnitOccupancyIndex
{
    private struct Entry
    {
        public int Count;
        public EntityId Solo; // valid iff Count == 1
    }

    private readonly Dictionary<CellCoord, Entry> _cells = new();

    public int CountAt(CellCoord c) => _cells.TryGetValue(c, out var e) ? e.Count : 0;

    /// <summary>True when some unit other than <paramref name="mover"/> occupies the cell.</summary>
    public bool IsBlockedFor(CellCoord c, EntityId mover)
    {
        if (!_cells.TryGetValue(c, out var e) || e.Count == 0) return false;
        return !(e.Count == 1 && e.Solo == mover);
    }

    public EntityId SoloAt(CellCoord c) =>
        _cells.TryGetValue(c, out var e) && e.Count == 1 ? e.Solo : EntityId.None;

    internal void Add(EntityId id, CellCoord c)
    {
        _cells.TryGetValue(c, out var e);
        e.Count++;
        e.Solo = e.Count == 1 ? id : EntityId.None;
        _cells[c] = e;
    }

    internal void Remove(EntityId id, CellCoord c)
    {
        if (!_cells.TryGetValue(c, out var e) || e.Count == 0)
            throw new InvalidOperationException($"Occupancy underflow at {c} removing {id}.");
        e.Count--;
        // 2→1 loses the identity; that transition can only happen in RealTime where
        // occupancy is advisory and identity is never consulted.
        e.Solo = EntityId.None;
        if (e.Count == 0) _cells.Remove(c); else _cells[c] = e;
    }

    internal void Move(EntityId id, CellCoord from, CellCoord to)
    {
        if (from == to) return;
        Remove(id, from);
        Add(id, to);
    }

    /// <summary>Derived state: rebuilt wholesale on load (dead/downed units filtered by callers).</summary>
    public void Clear() => _cells.Clear();
}
