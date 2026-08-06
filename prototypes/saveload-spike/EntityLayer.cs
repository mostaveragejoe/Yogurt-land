// PROTOTYPE - NOT FOR PRODUCTION
// Question: do ADR-0003's mode-switch-facing contracts hold at the seam —
//   occupancy exclusivity, pre-switch normalization, reservation release, outcome report?
// Date: 2026-07-26

namespace SaveLoadSpike;

public enum EntityKind { None, Colonist, Raider, Item, Door }

/// <summary>Monotonic, never reused; the counter is serialized (ADR-0003).</summary>
public sealed class EntityIdSource
{
    private long _next = 1;
    public EntityId Next() => new(_next++);
    public long Counter { get => _next; set => _next = value; }
}

public struct ColonistRecord
{
    public EntityId Id;
    public CellCoord Cell;
    public int Hp;
    public bool IsDead;
    public int BattlesSurvived;
    public string Name;
}

public struct RaiderRecord
{
    public EntityId Id;
    public CellCoord Cell;
    public int Hp;
    public bool IsDead;
    public bool Withdrew;
}

/// <summary>
/// Occupancy: exclusive under TurnBased (hard invariant, asserted), advisory under RealTime.
/// Single write path — only the stores' position/death handling touches it (ADR-0003).
/// </summary>
public sealed class UnitOccupancyIndex(Func<TimeAuthorityMode> mode)
{
    private readonly Dictionary<CellCoord, List<EntityId>> _byCell = [];
    public int ExclusivityViolations { get; private set; }

    internal void Move(EntityId id, CellCoord? from, CellCoord to)
    {
        if (from is { } f) Remove(id, f);
        if (!_byCell.TryGetValue(to, out List<EntityId>? l)) _byCell[to] = l = [];
        l.Add(id);
        l.Sort(static (a, b) => a.Value.CompareTo(b.Value));   // ascending EntityId (determinism)

        if (mode() == TimeAuthorityMode.TurnBased && l.Count > 1)
        {
            ExclusivityViolations++;
            throw new InvalidOperationException(
                $"Occupancy exclusivity violated under TurnBased at {to}: {string.Join(",", l.Select(x => x.Value))}");
        }
    }

    internal void Remove(EntityId id, CellCoord cell)
    {
        if (!_byCell.TryGetValue(cell, out List<EntityId>? l)) return;
        l.Remove(id);
        if (l.Count == 0) _byCell.Remove(cell);
    }

    public IReadOnlyList<EntityId> At(CellCoord c) =>
        _byCell.TryGetValue(c, out List<EntityId>? l) ? l : Array.Empty<EntityId>();

    /// <summary>Load-path rebuild only — bypasses the TurnBased exclusivity assert (mode is RealTime on load).</summary>
    public void PlaceForRebuild(EntityId id, CellCoord cell)
    {
        if (!_byCell.TryGetValue(cell, out List<EntityId>? l)) _byCell[cell] = l = [];
        l.Add(id);
        l.Sort(static (a, b) => a.Value.CompareTo(b.Value));
    }

    public bool IsOccupied(CellCoord c) => _byCell.TryGetValue(c, out List<EntityId>? l) && l.Count > 0;
    public int OccupiedCellCount => _byCell.Count;
    public void Clear() => _byCell.Clear();

    /// <summary>Debug-console sweep: index ≡ living store positions (ADR-0003).</summary>
    public bool Verify(ColonistStore colonists, RaiderStore raiders, out string detail)
    {
        var expected = new Dictionary<CellCoord, List<long>>();
        foreach (ColonistRecord c in colonists.All) if (!c.IsDead) Add(c.Cell, c.Id.Value);
        foreach (RaiderRecord r in raiders.All) if (!r.IsDead && !r.Withdrew) Add(r.Cell, r.Id.Value);
        void Add(CellCoord cell, long id)
        {
            if (!expected.TryGetValue(cell, out List<long>? l)) expected[cell] = l = [];
            l.Add(id);
        }
        foreach ((CellCoord cell, List<long> ids) in expected) ids.Sort();

        if (expected.Count != _byCell.Count)
        { detail = $"cell count {_byCell.Count} != expected {expected.Count}"; return false; }
        foreach ((CellCoord cell, List<long> ids) in expected)
        {
            if (!_byCell.TryGetValue(cell, out List<EntityId>? actual) ||
                !actual.Select(a => a.Value).SequenceEqual(ids))
            { detail = $"mismatch at {cell}"; return false; }
        }
        detail = $"{_byCell.Count} occupied cells consistent";
        return true;
    }
}

public sealed class ColonistStore(EntityIdSource ids, UnitOccupancyIndex occupancy,
                                  MutationWindow window, Func<TimeAuthorityMode> mode)
{
    private readonly List<ColonistRecord> _records = [];
    public long Revision { get; private set; }
    public IReadOnlyList<ColonistRecord> All => _records;

    private int IndexOf(EntityId id)
    {
        int lo = 0, hi = _records.Count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            long cmp = _records[mid].Id.Value - id.Value;
            if (cmp == 0) return mid;
            if (cmp < 0) lo = mid + 1; else hi = mid - 1;
        }
        return -1;
    }

    public ColonistRecord Get(EntityId id) => _records[IndexOf(id)];
    public bool Exists(EntityId id) => IndexOf(id) >= 0;

    public EntityId Spawn(string name, CellCoord cell, int hp)
    {
        EntityId id = ids.Next();
        _records.Add(new ColonistRecord { Id = id, Cell = cell, Hp = hp, Name = name });
        occupancy.Move(id, null, cell);
        Revision++;
        return id;
    }

    /// <summary>Position writer — RealTime: Colonist Movement; TurnBased: Combat Movement.
    /// BOTH go through this single setter, which updates occupancy atomically.</summary>
    public void SetCell(EntityId id, CellCoord cell)
    {
        AssertWindow();
        int i = IndexOf(id);
        CellCoord from = _records[i].Cell;
        ColonistRecord r = _records[i];
        r.Cell = cell;
        _records[i] = r;
        occupancy.Move(id, from, cell);
        Revision++;
    }

    /// <summary>Health writer-per-authority: Needs under RealTime, Combat under TurnBased.</summary>
    public void ApplyDamage(EntityId id, int amount, TimeAuthorityMode writerAuthority)
    {
        AssertWindow();
        if (mode() != writerAuthority)
            throw new InvalidOperationException(
                $"Health write by the {writerAuthority} writer while mode is {mode()} (ADR-0003 writer-per-authority).");
        int i = IndexOf(id);
        ColonistRecord r = _records[i];
        r.Hp -= amount;
        if (r.Hp <= 0 && !r.IsDead)
        {
            r.Hp = 0;
            r.IsDead = true;                       // lethal write sets IsDead; no separate death writer
            occupancy.Remove(id, r.Cell);          // dead units do not block cells
        }
        _records[i] = r;
        Revision++;
    }

    /// <summary>Identity Bookkeeping's field (ADR-0003) — only it may write this.</summary>
    public void IncrementBattlesSurvived(EntityId id)
    {
        AssertWindow();
        int i = IndexOf(id);
        ColonistRecord r = _records[i];
        r.BattlesSurvived++;
        _records[i] = r;
        Revision++;
    }

    /// <summary>Reaped by PostEncounterReconcile only — never inside an encounter.</summary>
    public void Despawn(EntityId id)
    {
        AssertWindow();
        if (mode() != TimeAuthorityMode.RealTime)
            throw new InvalidOperationException("No despawn inside an encounter (ADR-0003 universal rule).");
        int i = IndexOf(id);
        if (i < 0) return;
        occupancy.Remove(id, _records[i].Cell);
        _records.RemoveAt(i);                       // stable ordered removal
        Revision++;
    }

    private void AssertWindow()
    {
        if (!window.IsOpen) throw new InvalidOperationException("Entity write outside the mutation window (ADR-0003).");
    }

    /// <summary>Load path: replace contents wholesale, then derived state is rebuilt by the caller.</summary>
    public void RestoreAll(IEnumerable<ColonistRecord> records)
    {
        _records.Clear();
        _records.AddRange(records.OrderBy(r => r.Id.Value));
        Revision++;
    }
}

public sealed class RaiderStore(EntityIdSource ids, UnitOccupancyIndex occupancy, MutationWindow window)
{
    private readonly List<RaiderRecord> _records = [];
    public long Revision { get; private set; }
    public IReadOnlyList<RaiderRecord> All => _records;

    private int IndexOf(EntityId id) => _records.FindIndex(r => r.Id.Value == id.Value);

    public EntityId Spawn(CellCoord cell, int hp)
    {
        EntityId id = ids.Next();
        _records.Add(new RaiderRecord { Id = id, Cell = cell, Hp = hp });
        occupancy.Move(id, null, cell);
        Revision++;
        return id;
    }

    public RaiderRecord Get(EntityId id) => _records[IndexOf(id)];

    public void Kill(EntityId id)
    {
        if (!window.IsOpen) throw new InvalidOperationException("Entity write outside the mutation window.");
        int i = IndexOf(id);
        RaiderRecord r = _records[i];
        r.Hp = 0; r.IsDead = true;
        occupancy.Remove(id, r.Cell);
        _records[i] = r;
        Revision++;
    }

    public void Despawn(EntityId id)
    {
        int i = IndexOf(id);
        if (i < 0) return;
        occupancy.Remove(id, _records[i].Cell);
        _records.RemoveAt(i);
        Revision++;
    }
    public int Count => _records.Count;
}

/// <summary>Owned by Stockpile &amp; Hauling; gates ConsumeFromStack; released at reconcile.</summary>
public sealed class StackReservationTable
{
    private readonly Dictionary<EntityId, (EntityId holder, int qty)> _reservations = [];
    public int Count => _reservations.Count;

    public void Reserve(EntityId stack, EntityId holder, int qty) => _reservations[stack] = (holder, qty);
    public bool IsReservedBy(EntityId stack, EntityId holder) =>
        _reservations.TryGetValue(stack, out (EntityId holder, int qty) v) && v.holder.Value == holder.Value;

    /// <summary>Deterministic ordering for byte-stable saves.</summary>
    public IEnumerable<(EntityId stack, EntityId holder, int qty)> AllOrdered() =>
        _reservations.OrderBy(kv => kv.Key.Value).Select(kv => (kv.Key, kv.Value.holder, kv.Value.qty));

    public void RestoreAll(IEnumerable<(EntityId stack, EntityId holder, int qty)> rows)
    {
        _reservations.Clear();
        foreach ((EntityId s, EntityId h, int q) in rows) _reservations[s] = (h, q);
    }

    public int ReleaseAllHeldBy(EntityId holder)
    {
        var doomed = _reservations.Where(kv => kv.Value.holder.Value == holder.Value).Select(kv => kv.Key).ToList();
        foreach (EntityId s in doomed) _reservations.Remove(s);
        return doomed.Count;
    }
}

public readonly record struct ParticipantOutcome(EntityId Id, bool Survived, CellCoord DeathCell);

public sealed record EncounterOutcomeReport(
    int EncounterId,
    IReadOnlyList<EntityId> ParticipantIds,
    IReadOnlyList<ParticipantOutcome> Outcomes,
    bool RaidRepelled);

/// <summary>One slot. Written by Combat: Turn Order at battle end; drained by reconcile. Never serialized.</summary>
public sealed class EncounterOutcomeInbox
{
    private EncounterOutcomeReport? _slot;
    public bool HasReport => _slot is not null;
    public int WriteCount { get; private set; }

    public void Deposit(EncounterOutcomeReport report)
    {
        if (_slot is not null) throw new InvalidOperationException("Inbox already holds a report (one slot, ADR-0003).");
        _slot = report;
        WriteCount++;
    }

    public EncounterOutcomeReport? Drain()
    {
        EncounterOutcomeReport? r = _slot;
        _slot = null;
        return r;
    }
}
