using Hollowdeep.Core.Primitives;
using Hollowdeep.Core.Sim;
using Hollowdeep.Core.Terrain;

namespace Hollowdeep.Core.Entities;

public enum PropKind : byte
{
    Torch = 0,
    Hearth = 1,
}

/// <summary>
/// Purely aesthetic furniture (§8 art: one warm point light per built hearth/torch
/// prop). Props never block movement, never grant cover, and no combat, threat, or
/// repair logic reads them (§2: lighting is aesthetic only).
/// </summary>
public struct PropData
{
    public EntityId Id;
    public CellCoord Cell;
    public PropKind Kind;
    public MaterialId Material;
}

public sealed class PropStore
{
    private readonly List<PropData> _data = new();
    private readonly Dictionary<EntityId, int> _index = new();
    private readonly Dictionary<CellCoord, EntityId> _byCell = new();
    private readonly MutationGate _gate;

    public Revision Revision;

    public PropStore(MutationGate gate) => _gate = gate;

    public int Count => _data.Count;
    public PropData this[int i] => _data[i];
    public bool HasAt(CellCoord cell) => _byCell.ContainsKey(cell);
    public bool TryGet(EntityId id, out PropData data)
    {
        if (_index.TryGetValue(id, out int i)) { data = _data[i]; return true; }
        data = default; return false;
    }

    internal EntityId Add(EntityIdAllocator ids, CellCoord cell, PropKind kind, MaterialId material)
    {
        _gate.AssertOpen();
        if (_byCell.ContainsKey(cell))
            throw new InvalidOperationException($"Prop already exists at {cell}.");
        var id = ids.Next(EntityKind.Prop);
        _index[id] = _data.Count;
        _byCell[cell] = id;
        _data.Add(new PropData { Id = id, Cell = cell, Kind = kind, Material = material });
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

    public void WriteTo(BinaryWriter w)
    {
        w.Write(_data.Count);
        foreach (var d in _data)
        {
            w.Write(d.Id.Value);
            w.Write(d.Cell.X); w.Write(d.Cell.Y); w.Write(d.Cell.Z);
            w.Write((byte)d.Kind); w.Write((byte)d.Material);
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
            var d = new PropData
            {
                Id = new EntityId(r.ReadInt64()),
                Cell = new CellCoord(r.ReadInt32(), r.ReadInt32(), r.ReadInt32()),
                Kind = (PropKind)r.ReadByte(),
                Material = (MaterialId)r.ReadByte(),
            };
            _index[d.Id] = _data.Count;
            _byCell[d.Cell] = d.Id;
            _data.Add(d);
        }
        Revision.Bump();
    }
}
