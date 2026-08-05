using Hollowdeep.Core.Entities;
using Hollowdeep.Core.Primitives;

namespace Hollowdeep.Core.Colony;

/// <summary>
/// Stockpile zones. Zone cells are insertion-ordered (deterministic free-cell search).
/// One stack per cell; hauling merges into same-type stacks with space first.
/// </summary>
public sealed class StockpileSystem
{
    private readonly List<CellCoord> _zoneCells = new();
    private readonly HashSet<CellCoord> _zoneSet = new();
    private readonly ItemStore _items;

    public Revision Revision;

    public StockpileSystem(ItemStore items) => _items = items;

    public IReadOnlyList<CellCoord> ZoneCells => _zoneCells;
    public bool IsStockpile(CellCoord c) => _zoneSet.Contains(c);

    public void AddZoneCell(CellCoord c)
    {
        if (_zoneSet.Add(c)) { _zoneCells.Add(c); Revision.Bump(); }
    }

    public void RemoveZoneCell(CellCoord c)
    {
        if (_zoneSet.Remove(c)) { _zoneCells.RemoveAll(z => z == c); Revision.Bump(); }
    }

    /// <summary>A loose stack already sitting on a zone cell is considered stored.</summary>
    public bool IsStored(in ItemData item) => item.IsLoose && _zoneSet.Contains(item.Cell);

    /// <summary>
    /// Picks the drop target for a hauled stack: first a same-type stack with space
    /// (merge), then the first empty zone cell; insertion order breaks ties.
    /// </summary>
    public bool TryFindDropCell(in ItemData hauled, out CellCoord cell, out EntityId mergeTarget)
    {
        // Pass 1: merge targets.
        for (int i = 0; i < _items.Count; i++)
        {
            var it = _items[i];
            if (!it.IsLoose || it.Id == hauled.Id) continue;
            if (!_zoneSet.Contains(it.Cell)) continue;
            if (it.Kind != hauled.Kind || it.Material != hauled.Material) continue;
            if (it.Count + hauled.Count > GameConfig.MaxStackCount) continue;
            cell = it.Cell;
            mergeTarget = it.Id;
            return true;
        }
        // Pass 2: empty cells.
        foreach (var zc in _zoneCells)
        {
            bool occupied = false;
            for (int i = 0; i < _items.Count; i++)
            {
                var it = _items[i];
                if (it.IsLoose && it.Cell == zc) { occupied = true; break; }
            }
            if (!occupied)
            {
                cell = zc;
                mergeTarget = EntityId.None;
                return true;
            }
        }
        cell = default;
        mergeTarget = EntityId.None;
        return false;
    }

    public int StoredItemCount()
    {
        int n = 0;
        for (int i = 0; i < _items.Count; i++)
            if (IsStored(_items[i])) n += _items[i].Count;
        return n;
    }

    public void WriteTo(BinaryWriter w)
    {
        w.Write(_zoneCells.Count);
        foreach (var c in _zoneCells) { w.Write(c.X); w.Write(c.Y); w.Write(c.Z); }
    }

    public void ReadFrom(BinaryReader r)
    {
        _zoneCells.Clear();
        _zoneSet.Clear();
        int n = r.ReadInt32();
        for (int i = 0; i < n; i++)
        {
            var c = new CellCoord(r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
            _zoneCells.Add(c);
            _zoneSet.Add(c);
        }
        Revision.Bump();
    }
}
