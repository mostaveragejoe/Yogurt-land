using Godot;
using Hollowdeep.Core;
using Hollowdeep.Core.Combat;
using Hollowdeep.Core.Entities;
using Hollowdeep.Core.Primitives;
using Hollowdeep.Core.Sim;

namespace Hollowdeep.Game;

/// <summary>
/// All 2D UI, built in code: top bar (speed/Z/raid warning), tool palette, colonist
/// list with need bars, combat panel, and the post-battle scar summary (Pillar 3).
/// Reads sim state every frame; writes only via designations/orders.
/// </summary>
public partial class GameUi : CanvasLayer
{
    private GameWorld _world = null!;
    private ToolController _tools = null!;
    private TerrainRenderer _terrainView = null!;
    private UnitViewManager _units = null!;
    private Main _main = null!;

    private Label _status = null!;
    private Label _raidWarning = null!;
    private Label _combatInfo = null!;
    private PanelContainer _combatPanel = null!;
    private VBoxContainer _colonistRows = null!;
    private readonly List<(Label Name, ProgressBar Food, ProgressBar Sleep, Label Activity)> _rows = new();
    private AcceptDialog _scarDialog = null!;
    private Label _toast = null!;
    private double _toastUntil;
    private readonly List<Button> _toolButtons = new();

    public void Attach(Main main, GameWorld world, ToolController tools, TerrainRenderer terrainView, UnitViewManager units)
    {
        _main = main;
        _world = world;
        _tools = tools;
        _terrainView = terrainView;
        _units = units;
        Build();
        world.OutcomeReported += _ => CallDeferred(nameof(ShowScarSummary));
    }

    private void Build()
    {
        var root = new Control { Name = "UiRoot" };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(root);

        // ---- top bar ----
        var top = new PanelContainer();
        top.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        root.AddChild(top);
        var topRow = new HBoxContainer();
        top.AddChild(topRow);
        _status = new Label { Text = "..." };
        topRow.AddChild(_status);
        topRow.AddChild(new VSeparator());
        AddButton(topRow, "⏸ (Space)", () => _main.SetSpeed(_world.Time.Speed == 0 ? 1 : 0));
        AddButton(topRow, "1x", () => _main.SetSpeed(1));
        AddButton(topRow, "2x", () => _main.SetSpeed(2));
        AddButton(topRow, "3x", () => _main.SetSpeed(3));
        topRow.AddChild(new VSeparator());
        AddButton(topRow, "Z− (Q)", () => _main.ChangeZ(-1));
        AddButton(topRow, "Z+ (E)", () => _main.ChangeZ(+1));
        topRow.AddChild(new VSeparator());
        AddButton(topRow, "Quicksave (F5)", () => _main.QuickSave());
        AddButton(topRow, "Quickload (F9)", () => _main.QuickLoad());

        _raidWarning = new Label
        {
            Text = "",
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = new Color(1f, 0.35f, 0.25f),
        };
        _raidWarning.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        _raidWarning.Position += new Vector2(0, 40);
        root.AddChild(_raidWarning);

        // ---- tool palette (left) ----
        var toolPanel = new PanelContainer();
        toolPanel.SetAnchorsPreset(Control.LayoutPreset.CenterLeft);
        root.AddChild(toolPanel);
        var toolBox = new VBoxContainer();
        toolPanel.AddChild(toolBox);
        toolBox.AddChild(new Label { Text = "Tools" });
        AddToolButton(toolBox, "Select", Tool.Select);
        AddToolButton(toolBox, "Dig (D)", Tool.Dig);
        AddToolButton(toolBox, "Stair down (S)", Tool.StairDown);
        AddToolButton(toolBox, "Build wall (W)", Tool.BuildWall);
        AddToolButton(toolBox, "Build floor", Tool.BuildFloor);
        AddToolButton(toolBox, "Build stair", Tool.BuildStair);
        AddToolButton(toolBox, "Door (O)", Tool.BuildDoor);
        AddToolButton(toolBox, "Repair (R)", Tool.Repair);
        AddToolButton(toolBox, "Stockpile (P)", Tool.Stockpile);
        AddToolButton(toolBox, "Cancel (C)", Tool.Cancel);
        var matButton = new Button { Text = "Material: dirt (M)" };
        matButton.Pressed += () =>
        {
            _tools.CycleMaterial();
            matButton.Text = $"Material: {_tools.BuildMaterial.ToString().ToLowerInvariant()} (M)";
        };
        toolBox.AddChild(matButton);
        _tools.StateChanged += () =>
        {
            matButton.Text = $"Material: {_tools.BuildMaterial.ToString().ToLowerInvariant()} (M)";
            RefreshToolButtons();
        };

        // ---- colonist panel (right) ----
        var colonistPanel = new PanelContainer();
        colonistPanel.SetAnchorsPreset(Control.LayoutPreset.CenterRight);
        colonistPanel.GrowHorizontal = Control.GrowDirection.Begin; // extend inward, not off-screen
        colonistPanel.GrowVertical = Control.GrowDirection.Both;
        root.AddChild(colonistPanel);
        _colonistRows = new VBoxContainer();
        colonistPanel.AddChild(_colonistRows);
        _colonistRows.AddChild(new Label { Text = "Colonists" });

        // ---- combat panel (bottom center) ----
        _combatPanel = new PanelContainer { Visible = false };
        _combatPanel.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        _combatPanel.GrowVertical = Control.GrowDirection.Begin;
        _combatPanel.GrowHorizontal = Control.GrowDirection.Both;
        root.AddChild(_combatPanel);
        var combatBox = new VBoxContainer();
        _combatPanel.AddChild(combatBox);
        _combatInfo = new Label { Text = "", HorizontalAlignment = HorizontalAlignment.Center };
        combatBox.AddChild(_combatInfo);
        var combatButtons = new HBoxContainer();
        combatBox.AddChild(combatButtons);
        AddButton(combatButtons, "End activation (Enter)", () => _world.Combat.EndActivation());
        combatBox.AddChild(new Label
        {
            Text = "LMB raider: attack · LMB tile: move · LMB colonist: inspect",
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        // ---- scar summary ----
        _scarDialog = new AcceptDialog { Title = "After the battle" };
        root.AddChild(_scarDialog);

        // ---- toast ----
        _toast = new Label { Text = "", HorizontalAlignment = HorizontalAlignment.Center };
        _toast.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        _toast.GrowVertical = Control.GrowDirection.Begin;
        _toast.GrowHorizontal = Control.GrowDirection.Both;
        _toast.Position += new Vector2(0, -80);
        root.AddChild(_toast);
    }

    private void AddButton(Container parent, string text, Action onPressed)
    {
        var b = new Button { Text = text };
        b.Pressed += onPressed;
        parent.AddChild(b);
    }

    private void AddToolButton(Container parent, string text, Tool tool)
    {
        var b = new Button { Text = text, ToggleMode = true };
        b.Pressed += () => _tools.SetTool(tool);
        b.SetMeta("tool", (int)tool);
        parent.AddChild(b);
        _toolButtons.Add(b);
    }

    private void RefreshToolButtons()
    {
        foreach (var b in _toolButtons)
            b.ButtonPressed = (int)b.GetMeta("tool") == (int)_tools.ActiveTool;
    }

    public void Toast(string message, double seconds = 3)
    {
        _toast.Text = message;
        _toastUntil = Time.GetTicksMsec() / 1000.0 + seconds;
    }

    public override void _Process(double delta)
    {
        if (_world == null) return;

        string mode = _world.Time.Mode == SimMode.TurnBased ? "BATTLE" : $"speed {_world.Time.Speed}x";
        _status.Text = $"Hollowdeep · {mode} · Z {_terrainView.ActiveZ} · day {(int)(_world.Stats.WorldSeconds / GameConfig.DayLengthSeconds) + 1} " +
                       $"· wealth {_world.Stats.Wealth(_world.Stockpiles.StoredItemCount())} · raid #{_world.Raids.RaidIndex}";

        _raidWarning.Text = _world.Raids.WarningActive
            ? $"⚠ RAID INCOMING — {_world.Raids.WarningSecondsLeft:0}s — position your colonists (right-click to rally)"
            : _world.Time.Mode == SimMode.TurnBased ? "⚔ TURN-BASED BATTLE" : "";

        UpdateColonistRows();
        UpdateCombatPanel();

        if (_toast.Text != "" && Time.GetTicksMsec() / 1000.0 > _toastUntil)
            _toast.Text = "";
    }

    private void UpdateColonistRows()
    {
        while (_rows.Count < _world.Colonists.Count)
        {
            var row = new HBoxContainer();
            var name = new Label { CustomMinimumSize = new Vector2(110, 0) };
            var food = new ProgressBar { MaxValue = GameConfig.FoodMax, CustomMinimumSize = new Vector2(60, 10), ShowPercentage = false };
            var sleep = new ProgressBar { MaxValue = GameConfig.SleepMax, CustomMinimumSize = new Vector2(60, 10), ShowPercentage = false };
            var activity = new Label { CustomMinimumSize = new Vector2(80, 0) };
            row.AddChild(name);
            row.AddChild(new Label { Text = "🍞" });
            row.AddChild(food);
            row.AddChild(new Label { Text = "💤" });
            row.AddChild(sleep);
            row.AddChild(activity);
            int index = _rows.Count;
            var button = new Button { Text = "◎", TooltipText = "select" };
            button.Pressed += () =>
            {
                if (index < _world.Colonists.Count)
                    _units.Select(_world.Colonists[index].Id);
            };
            row.AddChild(button);
            _colonistRows.AddChild(row);
            _rows.Add((name, food, sleep, activity));
        }
        for (int i = 0; i < _rows.Count && i < _world.Colonists.Count; i++)
        {
            var c = _world.Colonists[i];
            var (name, food, sleep, activity) = _rows[i];
            name.Text = (c.IsMiner ? "⛏ " : "") + c.Name + $"  {c.Hp}hp";
            food.Value = c.Food;
            sleep.Value = c.Sleep;
            activity.Text = c.Activity.ToString().ToLowerInvariant();
            name.Modulate = c.IsDead ? new Color(0.5f, 0.5f, 0.5f)
                : c.IsDowned ? new Color(1f, 0.4f, 0.3f) : Colors.White;
        }
    }

    private void UpdateCombatPanel()
    {
        bool inBattle = _world.Time.Mode == SimMode.TurnBased;
        _combatPanel.Visible = inBattle;
        if (!inBattle) return;
        var actor = _world.Combat.CurrentActor;
        if (actor.IsNone) { _combatInfo.Text = "…"; return; }
        string name = actor.Kind == EntityKind.Colonist
            ? _world.Colonists.Get(actor).Name
            : $"Raider {actor.Counter}";
        string side = _world.Combat.IsPlayerTurn ? "YOUR TURN" : "enemy turn";
        _combatInfo.Text = $"Round {_world.Combat.Encounter.RoundNumber} · {side} · {name} · AP {_world.Combat.RemainingAp(actor)}";
    }

    private void ShowScarSummary()
    {
        var outcome = _world.LastOutcome;
        if (outcome == null) return;
        var lines = new List<string>
        {
            outcome.Result switch
            {
                EncounterResult.Victory => "The raiders are dead. The mountain holds.",
                EncounterResult.RaidersRouted => "The raiders broke and fled.",
                EncounterResult.ColonyOverrun => "The colony was overrun. The downed will recover — rebuild stronger.",
                _ => "The battle ended.",
            },
            "",
            $"Raiders killed: {outcome.RaidersKilled}   routed: {outcome.RaidersRouted}",
            $"Colonists downed: {outcome.ColonistsDowned}   dead: {outcome.ColonistsDead}",
            $"Walls destroyed: {outcome.WallsDestroyed}   doors broken: {outcome.DoorsBroken}",
            "",
            "Scars of this raid:",
        };
        int shown = 0;
        foreach (var scar in _world.LastBattleScars)
        {
            if (scar.Kind is ScarKind.WallDamaged or ScarKind.DoorDamaged && shown > 12) continue;
            lines.Add($"  · {ScarText(scar)}");
            if (++shown >= 25) { lines.Add("  · …"); break; }
        }
        if (shown == 0) lines.Add("  · none — the walls held everywhere");
        lines.Add("");
        lines.Add("Paint Repair over damaged walls to close the wounds (Pillar: Scars Teach).");
        _scarDialog.DialogText = string.Join("\n", lines);
        _scarDialog.PopupCentered();
    }

    private static string ScarText(in Scar scar) => scar.Kind switch
    {
        ScarKind.WallDamaged => $"wall damaged at {scar.Cell}",
        ScarKind.WallDestroyed => $"wall DESTROYED at {scar.Cell}",
        ScarKind.DoorDamaged => $"door damaged at {scar.Cell}",
        ScarKind.DoorBroken => $"door BROKEN at {scar.Cell}",
        ScarKind.ColonistDowned => $"colonist downed at {scar.Cell}",
        ScarKind.ColonistDead => $"colonist KILLED at {scar.Cell}",
        ScarKind.BreachEntry => $"breach opened at {scar.Cell}",
        _ => scar.Kind.ToString(),
    };
}
