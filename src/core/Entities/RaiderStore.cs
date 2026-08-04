using Hollowdeep.Core.Primitives;
using Hollowdeep.Core.Sim;

namespace Hollowdeep.Core.Entities;

public enum RaiderKind : byte
{
    Melee = 0,
    Ranged = 1,
}

public enum RaiderState : byte
{
    Approaching = 0,   // RT: pathing toward the colony
    AttackingWall = 1, // RT: breaking a wall/door on its breach path
    Fighting = 2,      // TB
    Routing = 3,       // TB: pathing to the map edge
    Exited = 4,        // left the map (reaped at reconcile)
    Dead = 5,
}

public struct RaiderData
{
    public EntityId Id;
    public RaiderKind Kind;
    public CellCoord Cell;
    public float MoveProgress;
    public CellCoord NextCell;
    public int Hp;
    public int MaxHp;
    public RaiderState State;
    public CellCoord BreachTarget;   // wall/door cell it is hitting (AttackingWall)
    public float ActionTimer;        // RT: seconds until next wall hit / step
    public int RaidIndex;            // which raid wave spawned it

    public readonly bool IsAlive => State != RaiderState.Dead && State != RaiderState.Exited;
    public readonly int MeleeDamage => Kind == RaiderKind.Ranged ? GameConfig.RaiderRangedDamage : GameConfig.RaiderMeleeDamage;
}

public sealed class RaiderStore
{
    private readonly List<RaiderData> _data = new();
    private readonly Dictionary<EntityId, int> _index = new();
    private readonly MutationGate _gate;
    private readonly UnitOccupancyIndex _occupancy;

    public Revision Revision;

    public RaiderStore(MutationGate gate, UnitOccupancyIndex occupancy)
    {
        _gate = gate;
        _occupancy = occupancy;
    }

    public int Count => _data.Count;
    public RaiderData this[int i] => _data[i];
    public bool Contains(EntityId id) => _index.ContainsKey(id);
    public bool TryGet(EntityId id, out RaiderData data)
    {
        if (_index.TryGetValue(id, out int i)) { data = _data[i]; return true; }
        data = default; return false;
    }
    public RaiderData Get(EntityId id) => _data[_index[id]];

    public int AliveCount()
    {
        int n = 0;
        for (int i = 0; i < _data.Count; i++) if (_data[i].IsAlive) n++;
        return n;
    }

    internal ref RaiderData Ref(EntityId id)
    {
        _gate.AssertOpen();
        id.AssertKind(EntityKind.Raider);
        var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_data);
        return ref span[_index[id]];
    }

    internal EntityId Add(EntityIdAllocator ids, RaiderKind kind, CellCoord cell, int raidIndex)
    {
        _gate.AssertOpen();
        var id = ids.Next(EntityKind.Raider);
        _index[id] = _data.Count;
        _data.Add(new RaiderData
        {
            Id = id, Kind = kind, Cell = cell, NextCell = cell,
            Hp = GameConfig.RaiderMaxHp, MaxHp = GameConfig.RaiderMaxHp,
            State = RaiderState.Approaching, RaidIndex = raidIndex,
        });
        _occupancy.Add(id, cell);
        Revision.Bump();
        return id;
    }

    internal void SetCell(EntityId id, CellCoord cell)
    {
        ref var d = ref Ref(id);
        if (d.Cell == cell) return;
        if (d.IsAlive) _occupancy.Move(id, d.Cell, cell);
        d.Cell = cell;
        Revision.Bump();
    }

    internal void SetState(EntityId id, RaiderState state)
    {
        ref var d = ref Ref(id);
        bool wasAlive = d.IsAlive;
        d.State = state;
        if (wasAlive && !d.IsAlive) _occupancy.Remove(id, d.Cell);
        Revision.Bump();
    }

    /// <summary>
    /// PostEncounterReconcile reap: removes ALL raiders — live ones would become ghosts
    /// (ADR-0001). Ids are never reused; the list closes ranks preserving order.
    /// </summary>
    internal void ReapAll()
    {
        _gate.AssertOpen();
        for (int i = 0; i < _data.Count; i++)
            if (_data[i].IsAlive)
                _occupancy.Remove(_data[i].Id, _data[i].Cell);
        _data.Clear();
        _index.Clear();
        Revision.Bump();
    }

    internal void Bump() => Revision.Bump();

    public void WriteTo(BinaryWriter w)
    {
        w.Write(_data.Count);
        foreach (var d in _data)
        {
            w.Write(d.Id.Value); w.Write((byte)d.Kind);
            w.Write(d.Cell.X); w.Write(d.Cell.Y); w.Write(d.Cell.Z);
            w.Write(d.MoveProgress);
            w.Write(d.NextCell.X); w.Write(d.NextCell.Y); w.Write(d.NextCell.Z);
            w.Write(d.Hp); w.Write(d.MaxHp); w.Write((byte)d.State);
            w.Write(d.BreachTarget.X); w.Write(d.BreachTarget.Y); w.Write(d.BreachTarget.Z);
            w.Write(d.ActionTimer); w.Write(d.RaidIndex);
        }
    }

    public void ReadFrom(BinaryReader r)
    {
        _gate.AssertOpen();
        _data.Clear();
        _index.Clear();
        int n = r.ReadInt32();
        for (int i = 0; i < n; i++)
        {
            var d = new RaiderData
            {
                Id = new EntityId(r.ReadInt64()),
                Kind = (RaiderKind)r.ReadByte(),
                Cell = new CellCoord(r.ReadInt32(), r.ReadInt32(), r.ReadInt32()),
                MoveProgress = r.ReadSingle(),
                NextCell = new CellCoord(r.ReadInt32(), r.ReadInt32(), r.ReadInt32()),
                Hp = r.ReadInt32(), MaxHp = r.ReadInt32(),
                State = (RaiderState)r.ReadByte(),
                BreachTarget = new CellCoord(r.ReadInt32(), r.ReadInt32(), r.ReadInt32()),
                ActionTimer = r.ReadSingle(), RaidIndex = r.ReadInt32(),
            };
            _index[d.Id] = _data.Count;
            _data.Add(d);
        }
        Revision.Bump();
    }

    /// <summary>Occupancy rebuild filters dead/exited units (ADR-0004 load rule).</summary>
    public void RebuildOccupancy()
    {
        for (int i = 0; i < _data.Count; i++)
            if (_data[i].IsAlive)
                _occupancy.Add(_data[i].Id, _data[i].Cell);
    }
}
