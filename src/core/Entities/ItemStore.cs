using Hollowdeep.Core.Primitives;
using Hollowdeep.Core.Sim;
using Hollowdeep.Core.Terrain;

namespace Hollowdeep.Core.Entities;

public enum ItemKind : byte
{
    Material = 0, // MaterialId says which tier
    Food = 1,
}

public struct ItemData
{
    public EntityId Id;
    public ItemKind Kind;
    public MaterialId Material; // None for food
    public int Count;
    public CellCoord Cell;
    public EntityId ReservedBy;  // job assignee holding the reservation (None = free)
    public EntityId CarriedBy;   // colonist carrying it (None = on the ground)

    public readonly bool IsLoose => CarriedBy.IsNone;
}

/// <summary>Loose/carried stacks of material and food. Reservation-gated consumption (§5.8).</summary>
public sealed class ItemStore
{
    private readonly List<ItemData> _data = new();
    private readonly Dictionary<EntityId, int> _index = new();
    private readonly MutationGate _gate;

    public Revision Revision;

    public ItemStore(MutationGate gate) => _gate = gate;

    public int Count => _data.Count;
    public ItemData this[int i] => _data[i];
    public bool Contains(EntityId id) => _index.ContainsKey(id);
    public bool TryGet(EntityId id, out ItemData data)
    {
        if (_index.TryGetValue(id, out int i)) { data = _data[i]; return true; }
        data = default; return false;
    }
    public ItemData Get(EntityId id) => _data[_index[id]];

    internal ref ItemData Ref(EntityId id)
    {
        _gate.AssertOpen();
        id.AssertKind(EntityKind.Item);
        var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_data);
        return ref span[_index[id]];
    }

    internal EntityId Add(EntityIdAllocator ids, ItemKind kind, MaterialId material, int count, CellCoord cell)
    {
        _gate.AssertOpen();
        var id = ids.Next(EntityKind.Item);
        _index[id] = _data.Count;
        _data.Add(new ItemData { Id = id, Kind = kind, Material = material, Count = count, Cell = cell });
        Revision.Bump();
        return id;
    }

    internal void RemoveAt(EntityId id)
    {
        _gate.AssertOpen();
        int i = _index[id];
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
            w.Write(d.Id.Value); w.Write((byte)d.Kind); w.Write((byte)d.Material); w.Write(d.Count);
            w.Write(d.Cell.X); w.Write(d.Cell.Y); w.Write(d.Cell.Z);
            w.Write(d.ReservedBy.Value); w.Write(d.CarriedBy.Value);
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
            var d = new ItemData
            {
                Id = new EntityId(r.ReadInt64()),
                Kind = (ItemKind)r.ReadByte(),
                Material = (MaterialId)r.ReadByte(),
                Count = r.ReadInt32(),
                Cell = new CellCoord(r.ReadInt32(), r.ReadInt32(), r.ReadInt32()),
                ReservedBy = new EntityId(r.ReadInt64()),
                CarriedBy = new EntityId(r.ReadInt64()),
            };
            _index[d.Id] = _data.Count;
            _data.Add(d);
        }
        Revision.Bump();
    }
}
