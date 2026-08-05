using Godot;
using Hollowdeep.Core;
using Hollowdeep.Core.Colony;
using Hollowdeep.Core.Primitives;
using Hollowdeep.Core.Sim;
using Hollowdeep.Core.Terrain;

namespace Hollowdeep.Game;

public enum Tool
{
    Select = 0,
    Dig = 1,
    StairDown = 2,
    BuildWall = 3,
    BuildFloor = 4,
    BuildStair = 5,
    BuildDoor = 6,
    Repair = 7,
    Stockpile = 8,
    Cancel = 9,
    BuildTorch = 10,
}

/// <summary>
/// Input → designations/orders submitted to the owning systems (ADR-0003: UI never
/// writes stores). Drag-select paints rectangles; right-click rallies the selected
/// colonist (squad prep); in TurnBased clicks become move/attack orders.
/// </summary>
public partial class ToolController : Node
{
    private GameWorld _world = null!;
    private CameraRig _rig = null!;
    private UnitViewManager _units = null!;
    private TerrainRenderer _terrainView = null!;

    public Tool ActiveTool = Tool.Select;
    public MaterialId BuildMaterial = MaterialId.Dirt;
    public int ActivePriority = 5; // 2 = high, 5 = normal, 8 = low
    public event Action? StateChanged;

    private bool _dragging;
    private CellCoord _dragStart;

    public void Attach(GameWorld world, CameraRig rig, UnitViewManager units, TerrainRenderer terrainView)
    {
        _world = world;
        _rig = rig;
        _units = units;
        _terrainView = terrainView;
    }

    public void SetTool(Tool tool)
    {
        ActiveTool = tool;
        StateChanged?.Invoke();
    }

    public void SetPriority(int priority)
    {
        ActivePriority = priority;
        StateChanged?.Invoke();
    }

    public void CycleMaterial()
    {
        BuildMaterial = BuildMaterial switch
        {
            MaterialId.Dirt => MaterialId.Granite,
            MaterialId.Granite => MaterialId.Reinforced,
            _ => MaterialId.Dirt,
        };
        StateChanged?.Invoke();
    }

    private bool TryMouseCell(out CellCoord cell)
    {
        cell = default;
        var viewport = _rig.Camera.GetViewport();
        var mouse = viewport.GetMousePosition();
        var origin = _rig.Camera.ProjectRayOrigin(mouse);
        var dir = _rig.Camera.ProjectRayNormal(mouse);
        int z = _terrainView.ActiveZ;
        float planeY = z * GameView.LayerHeight + 0.1f;
        if (Mathf.Abs(dir.Y) < 1e-4f) return false;
        float t = (planeY - origin.Y) / dir.Y;
        if (t <= 0) return false;
        var hit = origin + dir * t;
        var c = GameView.WorldToCell(hit, z);
        if (!_world.Terrain.InBounds(c)) return false;
        cell = c;
        return true;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_world == null) return;
        if (@event is not InputEventMouseButton mb) return;

        if (_world.Time.Mode == SimMode.TurnBased)
        {
            HandleCombatClick(mb);
            return;
        }

        if (mb.ButtonIndex == MouseButton.Left)
        {
            if (mb.Pressed)
            {
                if (TryMouseCell(out var cell)) { _dragging = true; _dragStart = cell; }
            }
            else if (_dragging)
            {
                _dragging = false;
                if (TryMouseCell(out var cell)) ApplyRect(_dragStart, cell);
            }
        }
        else if (mb.ButtonIndex == MouseButton.Right && mb.Pressed)
        {
            // Rally the selected colonist (squad prep §5.13); otherwise clear the tool.
            if (!_units.Selected.IsNone && _units.Selected.Kind == EntityKind.Colonist
                && TryMouseCell(out var cell))
                _world.Jobs.IssueRallyOrder(_units.Selected, cell);
            else
                SetTool(Tool.Select);
        }
    }

    private void HandleCombatClick(InputEventMouseButton mb)
    {
        if (!mb.Pressed || !_world.Combat.IsPlayerTurn) return;
        var viewport = _rig.Camera.GetViewport();
        var mouse = viewport.GetMousePosition();
        var origin = _rig.Camera.ProjectRayOrigin(mouse);
        var dir = _rig.Camera.ProjectRayNormal(mouse);

        if (mb.ButtonIndex == MouseButton.Left)
        {
            var picked = _units.PickUnit(origin, dir);
            if (picked.Kind == EntityKind.Raider)
            {
                _world.Combat.TryAttack(picked);
                return;
            }
            if (picked.Kind == EntityKind.Colonist)
            {
                _units.Select(picked);
                return;
            }
            if (TryMouseCell(out var cell))
                _world.Combat.TryMove(cell);
        }
    }

    private void ApplyRect(CellCoord a, CellCoord b)
    {
        int x0 = Math.Min(a.X, b.X), x1 = Math.Max(a.X, b.X);
        int y0 = Math.Min(a.Y, b.Y), y1 = Math.Max(a.Y, b.Y);
        int z = a.Z;

        if (ActiveTool == Tool.Select)
        {
            // Click-select a unit under the cursor (rect select not needed for MVP).
            var viewport = _rig.Camera.GetViewport();
            var mouse = viewport.GetMousePosition();
            var picked = _units.PickUnit(_rig.Camera.ProjectRayOrigin(mouse), _rig.Camera.ProjectRayNormal(mouse));
            _units.Select(picked);
            StateChanged?.Invoke();
            return;
        }

        int p = ActivePriority;
        for (int y = y0; y <= y1; y++)
        for (int x = x0; x <= x1; x++)
        {
            var c = new CellCoord(x, y, z);
            switch (ActiveTool)
            {
                case Tool.Dig: _world.Designations.DesignateDig(c, priority: p); break;
                case Tool.StairDown: _world.Designations.DesignateDig(c, stairDown: true, priority: p); break;
                case Tool.BuildWall: _world.Designations.DesignateBuild(c, BlueprintKind.Wall, BuildMaterial, p); break;
                case Tool.BuildFloor: _world.Designations.DesignateBuild(c, BlueprintKind.Floor, BuildMaterial, p); break;
                case Tool.BuildStair: _world.Designations.DesignateBuild(c, BlueprintKind.Stair, BuildMaterial, p); break;
                case Tool.BuildDoor: _world.Designations.DesignateBuild(c, BlueprintKind.Door, BuildMaterial, p); break;
                case Tool.BuildTorch: _world.Designations.DesignateBuild(c, BlueprintKind.Torch, BuildMaterial, p); break;
                case Tool.Repair: _world.Designations.DesignateRepair(c, p); break;
                case Tool.Stockpile:
                    // Only floor-walkable cells make sense as storage: haul targets must be standable.
                    if (_world.Walk.TerrainWalkable(c)) _world.Stockpiles.AddZoneCell(c);
                    break;
                case Tool.Cancel:
                    _world.Designations.CancelAt(c);
                    _world.Stockpiles.RemoveZoneCell(c);
                    break;
            }
        }
    }
}
