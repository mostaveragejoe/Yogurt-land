using Hollowdeep.Core.Primitives;
using Hollowdeep.Core.Sim;
using Hollowdeep.Core.Terrain;

namespace Hollowdeep.Core.Entities;

public struct DoorData
{
    public EntityId Id;
    public CellCoord Cell;
    public MaterialId Material;
    public int Hp;              // GameConfig.DoorHp for any tier (§8)
    public bool IsBroken;

    /// <summary>A broken door unblocks movement AND line-of-sight immediately (ADR-0003).</summary>
    public readonly bool Blocks => !IsBroken;
}

/// <summary>Doors are MVP entities: damageable, auto-open in RT pathing, block when closed in TB.</summary>
public sealed class DoorStore
{
    private readonly List<DoorData> _data = new();
    private readonly Dictionary<EntityId, int> _index = new();
    private readonly Dictionary<CellCoord, EntityId> _byCell = new();
    private readonly MutationGate _gate;

    public Revision Revision;

    public DoorStore(MutationGate gate) => _gate = gate;

    public int Count => _data.Count;
    public DoorData this[int i] => _data[i];
    public bool TryGetAt(CellCoord cell, out DoorData data)
    {
        if (_byCell.TryGetValue(cell, out var id)) { data = _data[_index[id]]; return true; }
        data = default; return false;
    }
    public bool TryGet(EntityId id, out DoorData data)
    {
        if (_index.TryGetValue(id, out int i)) { data = _data[i]; return true; }
        data = default; return false;
    }
    public DoorData Get(EntityId id) => _data[_index[id]];

    internal ref DoorData Ref(EntityId id)
    {
        _gate.AssertOpen();
        id.AssertKind(EntityKind.Door);
        var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_data);
        return ref span[_index[id]];
    }

    internal EntityId Add(EntityIdAllocator ids, CellCoord cell, MaterialId material)
    {
        _gate.AssertOpen();
        if (_byCell.ContainsKey(cell))
            throw new InvalidOperationException($"Door already exists at {cell}.");
        var id = ids.Next(EntityKind.Door);
        _index[id] = _data.Count;
        _byCell[cell] = id;
        _data.Add(new DoorData { Id = id, Cell = cell, Material = material, Hp = GameConfig.DoorHp });
        Revision.Bump();
        return id;
    }

    internal void Remove(EntityId id)
    {
        _gate.AssertOpen();
        int i = _index[id];
        _byCell.Remove(_data[i].Cell);
        _data.RemoveAt(i);
        _index.Remove(id);
        for (int j = i; j < _data.Count; j++) _index[_data[j].Id] = j;
        Revision.Bump();
    }

    internal void Bump() => Revision.Bump();

    public void WriteTo(BinaryWriter w)
    {
        w.Write(_data.Count);
        foreach (var d in _data)
        {
            w.Write(d.Id.Value);
            w.Write(d.Cell.X); w.Write(d.Cell.Y); w.Write(d.Cell.Z);
            w.Write((byte)d.Material); w.Write(d.Hp); w.Write(d.IsBroken);
        }
    }

    public void ReadFrom(BinaryReader r)
    {
        _gate.AssertOpen();
        _data.Clear();
        _index.Clear();
        _byCell.Clear();
        int n = r.ReadInt32();
        for (int i = 0; i < n; i++)
        {
            var d = new DoorData
            {
                Id = new EntityId(r.ReadInt64()),
                Cell = new CellCoord(r.ReadInt32(), r.ReadInt32(), r.ReadInt32()),
                Material = (MaterialId)r.ReadByte(),
                Hp = r.ReadInt32(),
                IsBroken = r.ReadBoolean(),
            };
            _index[d.Id] = _data.Count;
            _byCell[d.Cell] = d.Id;
            _data.Add(d);
        }
        Revision.Bump();
    }
}
