using Hollowdeep.Core.Primitives;
using Hollowdeep.Core.Terrain;

namespace Hollowdeep.Core.Colony;

public enum BlueprintKind : byte
{
    Wall = 0,
    Floor = 1,
    Stair = 2,
    Door = 3,
}

/// <summary>
/// Player plans (Pillar 4: colony mode is plans and orders). Input submits designations
/// here; the JobSystem turns them into jobs. Insertion order is stable and serialized —
/// it is part of determinism.
/// </summary>
public sealed class DesignationSystem
{
    public readonly record struct DigDesignation(CellCoord Cell, bool StairDown, int Priority);
    public readonly record struct BuildDesignation(CellCoord Cell, BlueprintKind Kind, MaterialId Material, int Priority);
    public readonly record struct RepairDesignation(CellCoord Cell, int Priority);

    private readonly List<DigDesignation> _digs = new();
    private readonly List<BuildDesignation> _builds = new();
    private readonly List<RepairDesignation> _repairs = new();
    private readonly HashSet<CellCoord> _digCells = new();
    private readonly HashSet<CellCoord> _buildCells = new();
    private readonly HashSet<CellCoord> _repairCells = new();

    private readonly TerrainWorld _terrain;

    public Revision Revision; // UI overlay redraws poll this

    public DesignationSystem(TerrainWorld terrain) => _terrain = terrain;

    public IReadOnlyList<DigDesignation> Digs => _digs;
    public IReadOnlyList<BuildDesignation> Builds => _builds;
    public IReadOnlyList<RepairDesignation> Repairs => _repairs;

    public bool HasDig(CellCoord c) => _digCells.Contains(c);
    public bool HasBuild(CellCoord c) => _buildCells.Contains(c);
    public bool HasRepair(CellCoord c) => _repairCells.Contains(c);

    /// <summary>Dig the wall at a cell (or carve a stair downward from an open cell).</summary>
    public bool DesignateDig(CellCoord c, bool stairDown = false, int priority = 5)
    {
        if (!_terrain.InBounds(c) || _digCells.Contains(c)) return false;
        if (stairDown)
        {
            // Stair-down carves the cell BELOW from the open cell above it.
            var below = c.Down;
            if (!_terrain.InBounds(below)) return false;
            if (_terrain.Get(c).HasWall) return false;             // must stand at c
            if (!_terrain.Get(below).HasWall) return false;        // must have rock to carve
        }
        else if (!_terrain.Get(c).HasWall) return false;
        _digs.Add(new DigDesignation(c, stairDown, priority));
        _digCells.Add(c);
        Revision.Bump();
        return true;
    }

    public bool DesignateBuild(CellCoord c, BlueprintKind kind, MaterialId material, int priority = 5)
    {
        if (!_terrain.InBounds(c) || _buildCells.Contains(c)) return false;
        var cell = _terrain.Get(c);
        bool valid = kind switch
        {
            BlueprintKind.Wall => !cell.HasWall,
            BlueprintKind.Floor => !cell.HasWall && !cell.HasFloor,
            BlueprintKind.Stair => !cell.HasWall && cell.Floor != FloorKind.Stair,
            BlueprintKind.Door => !cell.HasWall && cell.HasFloor,
            _ => false,
        };
        if (!valid || material == MaterialId.None) return false;
        _builds.Add(new BuildDesignation(c, kind, material, priority));
        _buildCells.Add(c);
        Revision.Bump();
        return true;
    }

    public bool DesignateRepair(CellCoord c, int priority = 5)
    {
        if (!_terrain.InBounds(c) || _repairCells.Contains(c)) return false;
        var cell = _terrain.Get(c);
        bool damagedWall = cell.HasWall && cell.WallHp < MaterialCatalog.Get(cell.WallMaterial).MaxWallHp;
        if (!damagedWall) return false;
        _repairs.Add(new RepairDesignation(c, priority));
        _repairCells.Add(c);
        Revision.Bump();
        return true;
    }

    public void CancelAt(CellCoord c)
    {
        if (_digCells.Remove(c)) _digs.RemoveAll(d => d.Cell == c);
        if (_buildCells.Remove(c)) _builds.RemoveAll(d => d.Cell == c);
        if (_repairCells.Remove(c)) _repairs.RemoveAll(d => d.Cell == c);
        Revision.Bump();
    }

    /// <summary>JobSystem consumption: removes the designation once its job completes or dies.</summary>
    internal void RemoveDig(CellCoord c) { if (_digCells.Remove(c)) { _digs.RemoveAll(d => d.Cell == c); Revision.Bump(); } }
    internal void RemoveBuild(CellCoord c) { if (_buildCells.Remove(c)) { _builds.RemoveAll(d => d.Cell == c); Revision.Bump(); } }
    internal void RemoveRepair(CellCoord c) { if (_repairCells.Remove(c)) { _repairs.RemoveAll(d => d.Cell == c); Revision.Bump(); } }

    public void WriteTo(BinaryWriter w)
    {
        w.Write(_digs.Count);
        foreach (var d in _digs) { w.Write(d.Cell.X); w.Write(d.Cell.Y); w.Write(d.Cell.Z); w.Write(d.StairDown); w.Write(d.Priority); }
        w.Write(_builds.Count);
        foreach (var b in _builds) { w.Write(b.Cell.X); w.Write(b.Cell.Y); w.Write(b.Cell.Z); w.Write((byte)b.Kind); w.Write((byte)b.Material); w.Write(b.Priority); }
        w.Write(_repairs.Count);
        foreach (var rp in _repairs) { w.Write(rp.Cell.X); w.Write(rp.Cell.Y); w.Write(rp.Cell.Z); w.Write(rp.Priority); }
    }

    public void ReadFrom(BinaryReader r)
    {
        _digs.Clear(); _builds.Clear(); _repairs.Clear();
        _digCells.Clear(); _buildCells.Clear(); _repairCells.Clear();
        int n = r.ReadInt32();
        for (int i = 0; i < n; i++)
        {
            var d = new DigDesignation(new CellCoord(r.ReadInt32(), r.ReadInt32(), r.ReadInt32()), r.ReadBoolean(), r.ReadInt32());
            _digs.Add(d); _digCells.Add(d.Cell);
        }
        n = r.ReadInt32();
        for (int i = 0; i < n; i++)
        {
            var b = new BuildDesignation(new CellCoord(r.ReadInt32(), r.ReadInt32(), r.ReadInt32()), (BlueprintKind)r.ReadByte(), (MaterialId)r.ReadByte(), r.ReadInt32());
            _builds.Add(b); _buildCells.Add(b.Cell);
        }
        n = r.ReadInt32();
        for (int i = 0; i < n; i++)
        {
            var rp = new RepairDesignation(new CellCoord(r.ReadInt32(), r.ReadInt32(), r.ReadInt32()), r.ReadInt32());
            _repairs.Add(rp); _repairCells.Add(rp.Cell);
        }
        Revision.Bump();
    }
}
