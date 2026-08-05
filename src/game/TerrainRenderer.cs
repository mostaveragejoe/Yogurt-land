using Godot;
using Hollowdeep.Core;
using Hollowdeep.Core.Primitives;
using Hollowdeep.Core.Terrain;

namespace Hollowdeep.Game;

/// <summary>
/// Two stacked GridMaps per layer (wall cubes + floor slabs), octant size locked to the
/// 32-cell chunk (ADR-0002 invariant: dirty chunk ↔ dirty octant). GridMaps are pure
/// write targets reading from TerrainWorld — never authoritative. The cutaway hides
/// layers above the active Z by toggling per-layer visibility (no repaint on Z change).
/// </summary>
public partial class TerrainRenderer : Node3D
{
    private TerrainWorld _terrain = null!;
    private GridMap[] _wallMaps = null!;
    private GridMap[] _floorMaps = null!;
    private readonly HashSet<ChunkCoord> _dirtyChunks = new();
    private bool _fullRepaintQueued;
    private int _activeZ = GameConfig.SurfaceZ;

    // MeshLibrary item ids.
    private const int ItemWallDirt = 0;
    private const int ItemWallGranite = 1;
    private const int ItemWallReinforced = 2;
    private const int ItemFloorNatural = 3;
    private const int ItemFloorBuilt = 4;
    private const int ItemStair = 5;
    private const int ItemWallDamaged = 6; // any tier below half HP shows cracked (darker)

    public int ActiveZ => _activeZ;

    public void Attach(TerrainWorld terrain)
    {
        _terrain = terrain;
        var library = BuildLibrary();
        _wallMaps = new GridMap[terrain.Depth];
        _floorMaps = new GridMap[terrain.Depth];
        for (int z = 0; z < terrain.Depth; z++)
        {
            _wallMaps[z] = MakeMap(library, z);
            _floorMaps[z] = MakeMap(library, z);
        }
        // Bus handler: idempotent dirty-marking only (ADR-0002).
        terrain.Changed += batch =>
        {
            for (int i = 0; i < batch.Count; i++)
                _dirtyChunks.Add(_terrain.ChunkOf(batch[i].Cell));
        };
        QueueFullRepaint();
    }

    private GridMap MakeMap(MeshLibrary library, int z)
    {
        var map = new GridMap
        {
            MeshLibrary = library,
            CellSize = new Vector3(GameView.CellSize, GameView.LayerHeight, GameView.CellSize),
            CellOctantSize = TerrainWorld.ChunkSize, // locked invariant: octant == chunk == 32
            CellCenterX = false,
            CellCenterY = false,
            CellCenterZ = false,
        };
        AddChild(map);
        return map;
    }

    private static MeshLibrary BuildLibrary()
    {
        var lib = new MeshLibrary();
        AddBox(lib, ItemWallDirt, new Vector3(1, 1, 1), new Vector3(0.5f, 0.5f, 0.5f), GameView.DirtColor);
        AddBox(lib, ItemWallGranite, new Vector3(1, 1, 1), new Vector3(0.5f, 0.5f, 0.5f), GameView.GraniteColor);
        AddBox(lib, ItemWallReinforced, new Vector3(1, 1, 1), new Vector3(0.5f, 0.5f, 0.5f), GameView.ReinforcedColor);
        AddBox(lib, ItemFloorNatural, new Vector3(1, 0.1f, 1), new Vector3(0.5f, 0.05f, 0.5f), GameView.DirtColor * GameView.FloorTint);
        AddBox(lib, ItemFloorBuilt, new Vector3(1, 0.12f, 1), new Vector3(0.5f, 0.06f, 0.5f), GameView.GraniteColor * GameView.FloorTint);
        AddBox(lib, ItemStair, new Vector3(0.9f, 0.35f, 0.9f), new Vector3(0.5f, 0.18f, 0.5f), GameView.StairColor);
        AddBox(lib, ItemWallDamaged, new Vector3(1, 0.85f, 1), new Vector3(0.5f, 0.42f, 0.5f), GameView.GraniteColor * 0.6f);
        return lib;
    }

    private static void AddBox(MeshLibrary lib, int id, Vector3 size, Vector3 center, Color color)
    {
        lib.CreateItem(id);
        var mesh = new BoxMesh { Size = size, Material = GameView.FlatMaterial(color) };
        // Offset the mesh so cell (0,0,0) fills [0..1): CellCenter* are disabled.
        var array = new ArrayMesh();
        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        tool.AppendFrom(mesh, 0, new Transform3D(Basis.Identity, center));
        tool.Commit(array);
        array.SurfaceSetMaterial(0, GameView.FlatMaterial(color));
        lib.SetItemMesh(id, array);
    }

    public void QueueFullRepaint() => _fullRepaintQueued = true;

    public void SetActiveZ(int z)
    {
        _activeZ = Mathf.Clamp(z, 0, _terrain.Depth - 1);
        for (int layer = 0; layer < _terrain.Depth; layer++)
        {
            bool visible = layer <= _activeZ;
            _wallMaps[layer].Visible = visible;
            _floorMaps[layer].Visible = visible;
        }
    }

    public override void _Process(double delta)
    {
        if (_terrain == null) return;
        if (_fullRepaintQueued)
        {
            _fullRepaintQueued = false;
            _dirtyChunks.Clear();
            for (int z = 0; z < _terrain.Depth; z++)
                for (int cy = 0; cy < _terrain.ChunksY; cy++)
                    for (int cx = 0; cx < _terrain.ChunksX; cx++)
                        RepaintChunk(new ChunkCoord(cx, cy, z));
            SetActiveZ(_activeZ);
            return;
        }
        if (_dirtyChunks.Count > 0)
        {
            foreach (var cc in _dirtyChunks)
                RepaintChunk(cc);
            _dirtyChunks.Clear();
        }
    }

    private void RepaintChunk(ChunkCoord cc)
    {
        var wallMap = _wallMaps[cc.Z];
        var floorMap = _floorMaps[cc.Z];
        int baseX = cc.Cx * TerrainWorld.ChunkSize;
        int baseY = cc.Cy * TerrainWorld.ChunkSize;
        for (int ly = 0; ly < TerrainWorld.ChunkSize; ly++)
        for (int lx = 0; lx < TerrainWorld.ChunkSize; lx++)
        {
            var c = new CellCoord(baseX + lx, baseY + ly, cc.Z);
            var cell = _terrain.Get(c);
            // GridMap axes: x → cell X, y → layer (always 0 within a per-layer map), z → cell Y.
            var pos = new Vector3I(c.X, 0, c.Y);
            wallMap.SetCellItem(pos, WallItem(cell));   // dig = SetCellItem(pos, -1)
            floorMap.SetCellItem(pos, FloorItem(cell));
        }
        wallMap.Position = new Vector3(0, cc.Z * GameView.LayerHeight, 0);
        floorMap.Position = new Vector3(0, cc.Z * GameView.LayerHeight, 0);
    }

    private static int WallItem(in TerrainCell cell)
    {
        if (!cell.HasWall) return -1;
        int max = MaterialCatalog.Get(cell.WallMaterial).MaxWallHp;
        if (cell.WallHp < max / 2) return ItemWallDamaged;
        return cell.WallMaterial switch
        {
            MaterialId.Dirt => ItemWallDirt,
            MaterialId.Granite => ItemWallGranite,
            MaterialId.Reinforced => ItemWallReinforced,
            _ => ItemWallDirt,
        };
    }

    private static int FloorItem(in TerrainCell cell)
    {
        if (cell.HasWall) return -1; // wall fills the cell
        return cell.Floor switch
        {
            FloorKind.Natural => ItemFloorNatural,
            FloorKind.Built => ItemFloorBuilt,
            FloorKind.Stair => ItemStair,
            _ => -1,
        };
    }
}
