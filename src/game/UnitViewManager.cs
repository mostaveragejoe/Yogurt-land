using Godot;
using Hollowdeep.Core;
using Hollowdeep.Core.Entities;
using Hollowdeep.Core.Primitives;

namespace Hollowdeep.Game;

/// <summary>
/// Godot views for entities: bind by id and READ (ADR-0003) — no per-entity sim state
/// lives here. Syncs against store Revision counters each frame; there is no entity
/// event bus to subscribe to, by design.
/// </summary>
public partial class UnitViewManager : Node3D
{
    private GameWorld _world = null!;
    private readonly Dictionary<EntityId, Node3D> _views = new();
    private readonly List<EntityId> _removeScratch = new();
    private ulong _colonistRev = ulong.MaxValue, _raiderRev = ulong.MaxValue;
    private ulong _itemRev = ulong.MaxValue, _doorRev = ulong.MaxValue;
    private int _activeZ = GameConfig.SurfaceZ;
    private EntityId _selected;
    private MeshInstance3D? _selectionRing;
    private Node3D? _hearth;

    public EntityId Selected => _selected;

    public void Attach(GameWorld world)
    {
        _world = world;
        _selectionRing = new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 0.42f, OuterRadius = 0.52f },
            MaterialOverride = GameView.FlatMaterial(GameView.SelectionColor, transparent: true),
            Visible = false,
        };
        AddChild(_selectionRing);
    }

    public void SetActiveZ(int z)
    {
        _activeZ = z;
        RefreshVisibility();
    }

    public void Select(EntityId id) => _selected = id;

    public override void _Process(double delta)
    {
        if (_world == null) return;
        SyncStores();
        UpdateTransforms();
    }

    private void SyncStores()
    {
        bool structural =
            _colonistRev != _world.Colonists.Revision.Value ||
            _raiderRev != _world.Raiders.Revision.Value ||
            _itemRev != _world.Items.Revision.Value ||
            _doorRev != _world.Doors.Revision.Value;
        if (!structural) return;
        _colonistRev = _world.Colonists.Revision.Value;
        _raiderRev = _world.Raiders.Revision.Value;
        _itemRev = _world.Items.Revision.Value;
        _doorRev = _world.Doors.Revision.Value;

        var live = new HashSet<EntityId>();
        for (int i = 0; i < _world.Colonists.Count; i++)
        {
            var c = _world.Colonists[i];
            live.Add(c.Id);
            if (!_views.ContainsKey(c.Id)) _views[c.Id] = MakeColonistView(c);
            StyleColonist((Node3D)_views[c.Id], c);
        }
        for (int i = 0; i < _world.Raiders.Count; i++)
        {
            var r = _world.Raiders[i];
            if (!r.IsAlive && r.State != RaiderState.Dead) continue; // exited: no view
            live.Add(r.Id);
            if (!_views.ContainsKey(r.Id)) _views[r.Id] = MakeRaiderView(r);
            StyleRaider((Node3D)_views[r.Id], r);
        }
        for (int i = 0; i < _world.Items.Count; i++)
        {
            var it = _world.Items[i];
            if (!it.IsLoose) continue; // carried stacks ride their carrier visually
            live.Add(it.Id);
            if (!_views.ContainsKey(it.Id)) _views[it.Id] = MakeItemView(it);
            StyleItem((Node3D)_views[it.Id], it);
        }
        for (int i = 0; i < _world.Doors.Count; i++)
        {
            var d = _world.Doors[i];
            live.Add(d.Id);
            if (!_views.ContainsKey(d.Id)) _views[d.Id] = MakeDoorView(d);
            StyleDoor((Node3D)_views[d.Id], d);
        }

        _removeScratch.Clear();
        foreach (var kv in _views)
            if (!live.Contains(kv.Key))
                _removeScratch.Add(kv.Key);
        foreach (var id in _removeScratch)
        {
            _views[id].QueueFree();
            _views.Remove(id);
            if (_selected == id) _selected = EntityId.None;
        }

        EnsureHearth();
        RefreshVisibility();
    }

    private void UpdateTransforms()
    {
        for (int i = 0; i < _world.Colonists.Count; i++)
        {
            var c = _world.Colonists[i];
            if (_views.TryGetValue(c.Id, out var view))
                view.Position = LerpedPosition(c.Cell, c.NextCell, c.MoveProgress) + Vector3.Up * 0.45f;
        }
        for (int i = 0; i < _world.Raiders.Count; i++)
        {
            var r = _world.Raiders[i];
            if (_views.TryGetValue(r.Id, out var view))
                view.Position = LerpedPosition(r.Cell, r.NextCell, r.MoveProgress) + Vector3.Up * 0.45f;
        }
        if (_selectionRing != null)
        {
            bool show = !_selected.IsNone && _views.TryGetValue(_selected, out var sel);
            _selectionRing.Visible = show;
            if (show && _views.TryGetValue(_selected, out var selected))
                _selectionRing.Position = selected.Position with { Y = selected.Position.Y - 0.35f };
        }
    }

    private static Vector3 LerpedPosition(CellCoord cell, CellCoord next, float progress)
        => GameView.CellToWorld(cell).Lerp(GameView.CellToWorld(next), Mathf.Clamp(progress, 0f, 1f));

    private void RefreshVisibility()
    {
        foreach (var kv in _views)
        {
            int z = UnitZ(kv.Key);
            kv.Value.Visible = z >= 0 && z <= _activeZ;
        }
        if (_hearth != null) _hearth.Visible = _world.Stats.HearthCell.Z <= _activeZ;
    }

    private int UnitZ(EntityId id) => id.Kind switch
    {
        EntityKind.Colonist when _world.Colonists.TryGet(id, out var c) => c.Cell.Z,
        EntityKind.Raider when _world.Raiders.TryGet(id, out var r) => r.Cell.Z,
        EntityKind.Item when _world.Items.TryGet(id, out var it) => it.Cell.Z,
        EntityKind.Door when _world.Doors.TryGet(id, out var d) => d.Cell.Z,
        _ => -1,
    };

    /// <summary>Nearest colonist/raider view to a world ray, for click selection.</summary>
    public EntityId PickUnit(Vector3 origin, Vector3 direction, float maxDistance = 200f)
    {
        EntityId best = EntityId.None;
        float bestDist = 0.6f; // ray-to-center tolerance
        foreach (var kv in _views)
        {
            if (kv.Key.Kind is not (EntityKind.Colonist or EntityKind.Raider)) continue;
            if (!kv.Value.Visible) continue;
            var to = kv.Value.Position - origin;
            float along = to.Dot(direction);
            if (along < 0 || along > maxDistance) continue;
            float off = (to - direction * along).Length();
            if (off < bestDist) { bestDist = off; best = kv.Key; }
        }
        return best;
    }

    // ---- view construction/styling ----

    private Node3D MakeColonistView(in ColonistData c)
    {
        var root = new Node3D();
        var body = new MeshInstance3D
        {
            Name = "Body",
            Mesh = new CapsuleMesh { Radius = 0.28f, Height = 0.9f },
        };
        root.AddChild(body);
        var label = new Label3D
        {
            Name = "Label",
            Text = c.Name,
            PixelSize = 0.006f,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Position = new Vector3(0, 0.8f, 0),
            Modulate = Colors.White,
            OutlineSize = 8,
        };
        root.AddChild(label);
        AddChild(root);
        return root;
    }

    private void StyleColonist(Node3D view, in ColonistData c)
    {
        var body = view.GetNode<MeshInstance3D>("Body");
        Color color = c.IsDead ? new Color(0.3f, 0.3f, 0.3f)
            : c.IsDowned ? GameView.DownedColor
            : c.IsMiner ? GameView.MinerColor
            : GameView.ColonistColor;
        body.MaterialOverride = GameView.FlatMaterial(color);
        body.Scale = c.IsDowned || c.IsDead ? new Vector3(1f, 0.35f, 1f) : Vector3.One;
        var label = view.GetNode<Label3D>("Label");
        label.Text = c.IsDowned ? $"{c.Name} (downed)" : c.Name;
    }

    private Node3D MakeRaiderView(in RaiderData r)
    {
        var root = new Node3D();
        var body = new MeshInstance3D
        {
            Name = "Body",
            Mesh = new BoxMesh { Size = new Vector3(0.55f, 0.9f, 0.55f) },
        };
        root.AddChild(body);
        AddChild(root);
        return root;
    }

    private void StyleRaider(Node3D view, in RaiderData r)
    {
        var body = view.GetNode<MeshInstance3D>("Body");
        Color color = r.State == RaiderState.Dead ? new Color(0.25f, 0.1f, 0.1f)
            : r.Kind == RaiderKind.Ranged ? GameView.RangedRaiderColor
            : GameView.RaiderColor;
        body.MaterialOverride = GameView.FlatMaterial(color);
        body.Scale = r.State == RaiderState.Dead ? new Vector3(1f, 0.3f, 1f) : Vector3.One;
    }

    private Node3D MakeItemView(in ItemData it)
    {
        var root = new Node3D();
        var body = new MeshInstance3D
        {
            Name = "Body",
            Mesh = new BoxMesh { Size = new Vector3(0.4f, 0.25f, 0.4f) },
        };
        root.AddChild(body);
        var label = new Label3D
        {
            Name = "Label",
            PixelSize = 0.005f,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Position = new Vector3(0, 0.4f, 0),
        };
        root.AddChild(label);
        AddChild(root);
        return root;
    }

    private void StyleItem(Node3D view, in ItemData it)
    {
        var body = view.GetNode<MeshInstance3D>("Body");
        Color color = it.Kind == ItemKind.Food ? GameView.FoodColor : it.Material switch
        {
            Hollowdeep.Core.Terrain.MaterialId.Dirt => GameView.DirtColor,
            Hollowdeep.Core.Terrain.MaterialId.Granite => GameView.GraniteColor,
            _ => GameView.ReinforcedColor,
        };
        body.MaterialOverride = GameView.FlatMaterial(color);
        view.GetNode<Label3D>("Label").Text = it.Count > 1 ? $"x{it.Count}" : "";
        view.Position = GameView.CellToWorld(it.Cell) + Vector3.Up * 0.2f;
    }

    private Node3D MakeDoorView(in DoorData d)
    {
        var root = new Node3D();
        var body = new MeshInstance3D
        {
            Name = "Body",
            Mesh = new BoxMesh { Size = new Vector3(0.85f, 0.95f, 0.3f) },
        };
        root.AddChild(body);
        AddChild(root);
        return root;
    }

    private void StyleDoor(Node3D view, in DoorData d)
    {
        var body = view.GetNode<MeshInstance3D>("Body");
        body.MaterialOverride = GameView.FlatMaterial(d.IsBroken ? GameView.BrokenDoorColor : GameView.DoorColor);
        body.Scale = d.IsBroken ? new Vector3(1f, 0.25f, 1f) : Vector3.One;
        // Orient across the wall line it plugs: walls east/west → slab runs east/west.
        bool wallsEastWest =
            _world.Terrain.InBounds(d.Cell.Offset(1, 0)) && _world.Terrain.Get(d.Cell.Offset(1, 0)).HasWall ||
            _world.Terrain.InBounds(d.Cell.Offset(-1, 0)) && _world.Terrain.Get(d.Cell.Offset(-1, 0)).HasWall;
        view.Rotation = new Vector3(0, wallsEastWest ? 0f : Mathf.Pi / 2f, 0);
        view.Position = GameView.CellToWorld(d.Cell) + Vector3.Up * 0.5f;
    }

    private void EnsureHearth()
    {
        if (_hearth != null) return;
        var hearthCell = _world.Stats.HearthCell;
        if (hearthCell == default) return;
        _hearth = new Node3D();
        _hearth.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.3f, BottomRadius = 0.4f, Height = 0.5f },
            MaterialOverride = GameView.FlatMaterial(new Color(0.9f, 0.5f, 0.2f)),
        });
        // One warm point light per hearth (§8) — purely aesthetic (§2: lighting never
        // feeds combat, threat, or repair logic).
        _hearth.AddChild(new OmniLight3D
        {
            LightColor = new Color(1f, 0.75f, 0.45f),
            LightEnergy = 2.5f,
            OmniRange = 14f,
            Position = new Vector3(0, 1.2f, 0),
        });
        _hearth.Position = GameView.CellToWorld(hearthCell) + Vector3.Up * 0.3f;
        AddChild(_hearth);
    }
}
