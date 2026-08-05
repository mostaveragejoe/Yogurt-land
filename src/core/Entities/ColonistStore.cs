using Hollowdeep.Core.Primitives;
using Hollowdeep.Core.Sim;

namespace Hollowdeep.Core.Entities;

public enum ColonistActivity : byte
{
    Idle = 0,
    Traveling = 1,
    Working = 2,
    Eating = 3,
    Sleeping = 4,
    Fighting = 5,
    Downed = 6,
    Dead = 7,
}

public struct ColonistData
{
    public EntityId Id;
    public string Name;
    public bool IsMiner;
    public CellCoord Cell;
    public float MoveProgress;   // 0..1 toward the next path cell (RT presentation reads this)
    public CellCoord NextCell;   // == Cell when not mid-step
    public int Hp;
    public int MaxHp;
    public float Food;
    public float Sleep;
    public float Morale;
    public bool IsDowned;
    public bool IsDead;
    public float DownedRecovery; // 0..1, fills over a day; restores at 50% HP
    public float HealProgress;   // fractional HP regen accumulator (Needs owns HP in RT)
    public ColonistActivity Activity;
    public long CurrentJobId;    // 0 = none

    public readonly bool IsActive => !IsDead && !IsDowned;
    public readonly int MeleeDamage => IsMiner ? GameConfig.MinerMeleeDamage : GameConfig.ColonistMeleeDamage;
}

/// <summary>
/// Typed plain-C# store (ADR-0003). Sim state only; Godot views bind by id and read.
/// All writes flow through writer interfaces created at the composition root.
/// </summary>
public sealed class ColonistStore
{
    private readonly List<ColonistData> _data = new();
    private readonly Dictionary<EntityId, int> _index = new();
    private readonly MutationGate _gate;
    private readonly UnitOccupancyIndex _occupancy;

    public Revision Revision;

    public ColonistStore(MutationGate gate, UnitOccupancyIndex occupancy)
    {
        _gate = gate;
        _occupancy = occupancy;
    }

    public int Count => _data.Count;
    public ColonistData this[int i] => _data[i];
    public bool TryGet(EntityId id, out ColonistData data)
    {
        if (_index.TryGetValue(id, out int i)) { data = _data[i]; return true; }
        data = default; return false;
    }
    public ColonistData Get(EntityId id) => _data[_index[id]];
    public bool Contains(EntityId id) => _index.ContainsKey(id);

    public int AliveActiveCount()
    {
        int n = 0;
        for (int i = 0; i < _data.Count; i++) if (_data[i].IsActive) n++;
        return n;
    }

    // ---- internal mutation surface (writers only) ----

    internal ref ColonistData Ref(EntityId id)
    {
        _gate.AssertOpen();
        id.AssertKind(EntityKind.Colonist);
        var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_data);
        return ref span[_index[id]];
    }

    internal EntityId Add(EntityIdAllocator ids, string name, CellCoord cell, bool isMiner)
    {
        _gate.AssertOpen();
        var id = ids.Next(EntityKind.Colonist);
        _index[id] = _data.Count;
        _data.Add(new ColonistData
        {
            Id = id, Name = name, IsMiner = isMiner, Cell = cell, NextCell = cell,
            Hp = GameConfig.ColonistMaxHp, MaxHp = GameConfig.ColonistMaxHp,
            Food = GameConfig.FoodMax, Sleep = GameConfig.SleepMax, Morale = GameConfig.MoraleMax,
            Activity = ColonistActivity.Idle,
        });
        _occupancy.Add(id, cell);
        Revision.Bump();
        return id;
    }

    /// <summary>Position write; keeps occupancy synchronous and atomic with it (ADR-0003).</summary>
    internal void SetCell(EntityId id, CellCoord cell)
    {
        ref var d = ref Ref(id);
        if (d.Cell == cell) return;
        if (d.IsActive) _occupancy.Move(id, d.Cell, cell);
        d.Cell = cell;
        Revision.Bump();
    }

    /// <summary>Downed/death transitions release occupancy; recovery re-adds it.</summary>
    internal void SetIncapacitated(EntityId id, bool downed, bool dead)
    {
        ref var d = ref Ref(id);
        bool wasActive = d.IsActive;
        d.IsDowned = downed;
        d.IsDead = dead;
        bool isActive = d.IsActive;
        if (wasActive && !isActive) _occupancy.Remove(id, d.Cell);
        else if (!wasActive && isActive) _occupancy.Add(id, d.Cell);
        d.Activity = dead ? ColonistActivity.Dead : downed ? ColonistActivity.Downed : d.Activity;
        Revision.Bump();
    }

    internal void Bump() => Revision.Bump();

    // ---- serialization (list order is stable; byte-identical round trips) ----

    public void WriteTo(BinaryWriter w)
    {
        w.Write(_data.Count);
        foreach (var d in _data)
        {
            w.Write(d.Id.Value); w.Write(d.Name); w.Write(d.IsMiner);
            w.Write(d.Cell.X); w.Write(d.Cell.Y); w.Write(d.Cell.Z);
            w.Write(d.MoveProgress);
            w.Write(d.NextCell.X); w.Write(d.NextCell.Y); w.Write(d.NextCell.Z);
            w.Write(d.Hp); w.Write(d.MaxHp);
            w.Write(d.Food); w.Write(d.Sleep); w.Write(d.Morale);
            w.Write(d.IsDowned); w.Write(d.IsDead); w.Write(d.DownedRecovery); w.Write(d.HealProgress);
            w.Write((byte)d.Activity); w.Write(d.CurrentJobId);
        }
    }

    /// <summary>Load-window restore; caller rebuilds occupancy afterwards.</summary>
    public void ReadFrom(BinaryReader r)
    {
        _gate.AssertOpen();
        _data.Clear();
        _index.Clear();
        int n = r.ReadInt32();
        for (int i = 0; i < n; i++)
        {
            var d = new ColonistData
            {
                Id = new EntityId(r.ReadInt64()),
                Name = r.ReadString(),
                IsMiner = r.ReadBoolean(),
                Cell = new CellCoord(r.ReadInt32(), r.ReadInt32(), r.ReadInt32()),
                MoveProgress = r.ReadSingle(),
                NextCell = new CellCoord(r.ReadInt32(), r.ReadInt32(), r.ReadInt32()),
                Hp = r.ReadInt32(), MaxHp = r.ReadInt32(),
                Food = r.ReadSingle(), Sleep = r.ReadSingle(), Morale = r.ReadSingle(),
                IsDowned = r.ReadBoolean(), IsDead = r.ReadBoolean(),
                DownedRecovery = r.ReadSingle(), HealProgress = r.ReadSingle(),
                Activity = (ColonistActivity)r.ReadByte(),
                CurrentJobId = r.ReadInt64(),
            };
            _index[d.Id] = _data.Count;
            _data.Add(d);
        }
        Revision.Bump();
    }

    /// <summary>Registers every living, non-downed colonist into a freshly cleared occupancy index.</summary>
    public void RebuildOccupancy()
    {
        for (int i = 0; i < _data.Count; i++)
            if (_data[i].IsActive)
                _occupancy.Add(_data[i].Id, _data[i].Cell);
    }
}
