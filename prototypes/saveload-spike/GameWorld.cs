// PROTOTYPE - NOT FOR PRODUCTION
// Question: the whole-world wiring the save contract operates on, incl. the derived state
//   that must be REBUILT on load and never serialized.
// Date: 2026-07-26

namespace SaveLoadSpike;

public struct ItemRecord
{
    public EntityId Id;
    public CellCoord Cell;
    public string MaterialKey;      // STABLE string key, not a runtime id (serialization contract)
    public int Quantity;
}

public sealed class ItemStore(EntityIdSource ids, MutationWindow window)
{
    private readonly List<ItemRecord> _records = [];
    public long Revision { get; private set; }
    public IReadOnlyList<ItemRecord> All => _records;

    public EntityId SpawnStack(string materialKey, int qty, CellCoord cell)
    {
        if (!window.IsOpen) throw new InvalidOperationException("Item write outside the mutation window.");
        EntityId id = ids.Next();
        _records.Add(new ItemRecord { Id = id, Cell = cell, MaterialKey = materialKey, Quantity = qty });
        Revision++;
        return id;
    }

    public void RestoreAll(IEnumerable<ItemRecord> records)
    {
        _records.Clear();
        _records.AddRange(records.OrderBy(r => r.Id.Value));
        Revision++;
    }
}

public struct DoorRecord
{
    public EntityId Id;
    public CellCoord Cell;
    public bool IsOpen;
    public int Hp;
    public bool IsBroken;
}

public sealed class DoorStore(EntityIdSource ids, MutationWindow window)
{
    private readonly List<DoorRecord> _records = [];
    public long Revision { get; private set; }
    public IReadOnlyList<DoorRecord> All => _records;

    public EntityId Spawn(CellCoord cell, int hp = 60)
    {
        if (!window.IsOpen) throw new InvalidOperationException("Door write outside the mutation window.");
        EntityId id = ids.Next();
        _records.Add(new DoorRecord { Id = id, Cell = cell, Hp = hp });
        Revision++;
        return id;
    }

    public void SetOpen(CellCoord cell, bool open)
    {
        int i = _records.FindIndex(d => d.Cell == cell);
        if (i < 0) return;
        DoorRecord d = _records[i]; d.IsOpen = open; _records[i] = d;
        Revision++;
    }

    public void RestoreAll(IEnumerable<DoorRecord> records)
    {
        _records.Clear();
        _records.AddRange(records.OrderBy(r => r.Id.Value));
        Revision++;
    }
}

/// <summary>Derived bookkeeping: EntityId -> kind. Rebuilt on load, NEVER serialized (ADR-0003).</summary>
public sealed class EntityDirectory
{
    private readonly Dictionary<long, EntityKind> _kinds = [];
    public int Count => _kinds.Count;
    public EntityKind KindOf(EntityId id) => _kinds.TryGetValue(id.Value, out EntityKind k) ? k : EntityKind.None;
    public void Clear() => _kinds.Clear();
    public void Add(EntityId id, EntityKind kind) => _kinds[id.Value] = kind;
}

/// <summary>Everything the save contract touches, plus the derived state it must NOT touch.</summary>
public sealed class GameWorld
{
    public TimeAuthorityManager Manager { get; }
    public MaterialCatalog Catalog { get; } = new();
    public TerrainWorld World { get; }
    public EntityIdSource Ids { get; } = new();
    public UnitOccupancyIndex Occupancy { get; }
    public ColonistStore Colonists { get; }
    public RaiderStore Raiders { get; }
    public DoorStore Doors { get; }
    public ItemStore Items { get; }
    public StackReservationTable Reservations { get; } = new();
    public EntityDirectory Directory { get; } = new();
    public EncounterOutcomeInbox Inbox { get; } = new();
    public ushort Dirt, Floor;

    /// <summary>Instrumentation: proves the derived state was actually rebuilt, not serialized.</summary>
    public int OccupancyRebuildCount { get; private set; }
    public int DirectoryRebuildCount { get; private set; }

    public GameWorld(int sx = 64, int sy = 64, int layers = 8)
    {
        Manager = new TimeAuthorityManager(new InstantPresentationGate());
        Dirt = Catalog.Register("dirt", 1, 30);
        Floor = Catalog.Register("rock_floor", 1, 0);
        World = new TerrainWorld(new WorldBounds(sx, sy, layers), Catalog, new NullSink(), Manager.Window, 32);
        World.PopulateForLoad(w =>
        {
            for (int z = 0; z < layers; z++)
                for (int y = 0; y < sy; y++)
                    for (int x = 0; x < sx; x++)
                        w.LoadSetCell(new CellCoord(x, y, z), new TerrainCell { FloorTypeId = Floor });
        });
        Occupancy = new UnitOccupancyIndex(() => Manager.CurrentMode);
        Colonists = new ColonistStore(Ids, Occupancy, Manager.Window, () => Manager.CurrentMode);
        Raiders = new RaiderStore(Ids, Occupancy, Manager.Window);
        Doors = new DoorStore(Ids, Manager.Window);
        Items = new ItemStore(Ids, Manager.Window);
    }

    /// <summary>The load window: replace authoritative state, then REBUILD all derived state.</summary>
    public void RestoreEntities(List<ColonistRecord> colonists, List<DoorRecord> doors,
                                List<ItemRecord> items, List<(EntityId, EntityId, int)> reservations)
    {
        Colonists.RestoreAll(colonists);
        Doors.RestoreAll(doors);
        Items.RestoreAll(items);
        Reservations.RestoreAll(reservations.Select(r => (r.Item1, r.Item2, r.Item3)));
        RebuildDerived();
    }

    public void RebuildDerived()
    {
        Occupancy.Clear();
        foreach (ColonistRecord c in Colonists.All)
            if (!c.IsDead) Occupancy.PlaceForRebuild(c.Id, c.Cell);
        OccupancyRebuildCount++;

        Directory.Clear();
        foreach (ColonistRecord c in Colonists.All) Directory.Add(c.Id, EntityKind.Colonist);
        foreach (DoorRecord d in Doors.All) Directory.Add(d.Id, EntityKind.Door);
        foreach (ItemRecord i in Items.All) Directory.Add(i.Id, EntityKind.Item);
        DirectoryRebuildCount++;
    }

    public sealed class NullSink : ITerrainChangeSink
    {
        public void Publish(in TerrainChangeBatch batch) { }
        public void PublishWorldReloaded() { }
    }
}
