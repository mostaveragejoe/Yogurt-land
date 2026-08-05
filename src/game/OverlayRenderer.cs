using Godot;
using Hollowdeep.Core;
using Hollowdeep.Core.Primitives;
using Hollowdeep.Core.Sim;

namespace Hollowdeep.Game;

/// <summary>
/// Flat tile overlays: designations, stockpile zones, farm plots, and the TB move
/// range. Rebuilt when the owning systems' revisions change (polling, no events).
/// </summary>
public partial class OverlayRenderer : Node3D
{
    private GameWorld _world = null!;
    private MultiMeshInstance3D _dig = null!, _build = null!, _repair = null!, _stockpile = null!, _moveRange = null!;
    private ulong _designationRev = ulong.MaxValue, _stockpileRev = ulong.MaxValue;
    private ulong _combatStamp = ulong.MaxValue;
    private readonly List<(CellCoord Cell, int Cost)> _rangeScratch = new();
    private int _activeZ = GameConfig.SurfaceZ;

    public void Attach(GameWorld world)
    {
        _world = world;
        _dig = MakeLayer(GameView.DigOverlay);
        _build = MakeLayer(GameView.BuildOverlay);
        _repair = MakeLayer(GameView.RepairOverlay);
        _stockpile = MakeLayer(GameView.StockpileOverlay);
        _moveRange = MakeLayer(GameView.MoveRangeOverlay);
    }

    public void SetActiveZ(int z)
    {
        _activeZ = z;
        _designationRev = ulong.MaxValue; // force rebuild with the new cutaway filter
        _stockpileRev = ulong.MaxValue;
    }

    private MultiMeshInstance3D MakeLayer(Color color)
    {
        var mmi = new MultiMeshInstance3D
        {
            Multimesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                Mesh = new BoxMesh
                {
                    Size = new Vector3(0.95f, 0.06f, 0.95f),
                    Material = GameView.FlatMaterial(color, transparent: true),
                },
            },
        };
        AddChild(mmi);
        return mmi;
    }

    public override void _Process(double delta)
    {
        if (_world == null) return;

        if (_designationRev != _world.Designations.Revision.Value)
        {
            _designationRev = _world.Designations.Revision.Value;
            var digCells = new List<CellCoord>();
            foreach (var d in _world.Designations.Digs) digCells.Add(d.Cell);
            Fill(_dig, digCells);
            var buildCells = new List<CellCoord>();
            foreach (var b in _world.Designations.Builds) buildCells.Add(b.Cell);
            Fill(_build, buildCells);
            var repairCells = new List<CellCoord>();
            foreach (var rp in _world.Designations.Repairs) repairCells.Add(rp.Cell);
            Fill(_repair, repairCells);
        }

        if (_stockpileRev != _world.Stockpiles.Revision.Value)
        {
            _stockpileRev = _world.Stockpiles.Revision.Value;
            var cells = new List<CellCoord>(_world.Stockpiles.ZoneCells);
            foreach (var plot in _world.Farms.Plots) cells.Add(plot.Cell);
            Fill(_stockpile, cells);
        }

        UpdateMoveRange();
    }

    private void UpdateMoveRange()
    {
        bool show = _world.Time.Mode == SimMode.TurnBased && _world.Combat.IsPlayerTurn;
        if (!show)
        {
            if (_moveRange.Multimesh.InstanceCount != 0) _moveRange.Multimesh.InstanceCount = 0;
            _combatStamp = ulong.MaxValue;
            return;
        }
        // Rebuild when the acting unit or the world changes.
        ulong stamp = (ulong)_world.Combat.CurrentActor.Value
                      ^ (_world.Terrain.Revision.Value << 20)
                      ^ ((ulong)_world.Combat.RemainingAp(_world.Combat.CurrentActor) << 56)
                      ^ _world.Combat.Encounter.ActivationCounter;
        if (stamp == _combatStamp) return;
        _combatStamp = stamp;
        _world.Combat.GetMoveRange(_rangeScratch);
        var cells = new List<CellCoord>(_rangeScratch.Count);
        foreach (var (cell, _) in _rangeScratch) cells.Add(cell);
        Fill(_moveRange, cells);
    }

    private void Fill(MultiMeshInstance3D mmi, List<CellCoord> cells)
    {
        int count = 0;
        foreach (var c in cells) if (c.Z <= _activeZ) count++;
        mmi.Multimesh.InstanceCount = count;
        int i = 0;
        foreach (var c in cells)
        {
            if (c.Z > _activeZ) continue;
            var pos = GameView.CellToWorld(c) + Vector3.Up * 0.14f;
            mmi.Multimesh.SetInstanceTransform(i++, new Transform3D(Basis.Identity, pos));
        }
    }
}
