using Godot;
using Hollowdeep.Core;
using Hollowdeep.Core.Entities;
using Hollowdeep.Core.Harness;
using Hollowdeep.Core.Primitives;
using Hollowdeep.Core.Save;
using Hollowdeep.Core.Sim;
using Hollowdeep.Core.Terrain;

namespace Hollowdeep.Game;

/// <summary>
/// F12 console (§5.16): a command registry over the sim. Debug writes open a sanctioned
/// gate window directly — the same privilege the load path holds; never used by gameplay.
/// </summary>
public partial class DebugConsole : CanvasLayer
{
    private GameWorld _world = null!;
    private Main _main = null!;
    private RichTextLabel _log = null!;
    private LineEdit _input = null!;
    private PanelContainer _panel = null!;
    private readonly Dictionary<string, (string Help, Action<string[]> Run)> _commands = new();

    public void Attach(Main main, GameWorld world)
    {
        _main = main;
        _world = world;
        Layer = 10;

        _panel = new PanelContainer { Visible = false };
        _panel.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        _panel.CustomMinimumSize = new Vector2(0, 280);
        AddChild(_panel);
        var box = new VBoxContainer();
        _panel.AddChild(box);
        _log = new RichTextLabel { ScrollFollowing = true, CustomMinimumSize = new Vector2(0, 230), SelectionEnabled = true };
        box.AddChild(_log);
        _input = new LineEdit { PlaceholderText = "command… (try: help)" };
        _input.TextSubmitted += OnSubmitted;
        box.AddChild(_input);

        Register("help", "list commands", _ =>
        {
            foreach (var kv in _commands.OrderBy(k => k.Key))
                Print($"  {kv.Key} — {kv.Value.Help}");
        });
        Register("spawn_raid", "trigger a raid warning now", _ =>
        {
            _world.Raids.DebugTriggerRaid();
            Print("raid warning triggered");
        });
        Register("set_speed", "set_speed 0|1|2|3", args =>
        {
            if (args.Length < 1 || !int.TryParse(args[0], out int s)) { Print("usage: set_speed 0..3"); return; }
            _main.SetSpeed(s);
            Print($"speed {s}");
        });
        Register("give_material", "give_material dirt|granite|reinforced [count]", args =>
        {
            if (args.Length < 1 || !Enum.TryParse<MaterialId>(args[0], ignoreCase: true, out var mat) || mat == MaterialId.None)
            { Print("usage: give_material dirt|granite|reinforced [count]"); return; }
            int count = args.Length > 1 && int.TryParse(args[1], out int n) ? n : 10;
            if (_world.Time.Mode != SimMode.RealTime) { Print("refused: RealTime only"); return; }
            using (_world.Gate.Open())
            {
                IItemsWriter items = _world.Writers;
                while (count > 0)
                {
                    int stack = Math.Min(GameConfig.MaxStackCount, count);
                    items.Spawn(ItemKind.Material, mat, stack, _world.Stats.HearthCell);
                    count -= stack;
                }
            }
            Print($"spawned {args[0]} at the hearth");
        });
        Register("heal_all", "restore every colonist", _ =>
        {
            if (_world.Time.Mode != SimMode.RealTime) { Print("refused: RealTime only"); return; }
            using (_world.Gate.Open())
            {
                for (int i = 0; i < _world.Colonists.Count; i++)
                {
                    var id = _world.Colonists[i].Id;
                    ref var d = ref _world.Colonists.Ref(id);
                    if (d.IsDead) continue;
                    if (d.IsDowned) _world.Colonists.SetIncapacitated(id, downed: false, dead: false);
                    d = ref _world.Colonists.Ref(id);
                    d.Hp = d.MaxHp;
                    d.Food = GameConfig.FoodMax;
                    d.Sleep = GameConfig.SleepMax;
                }
                _world.Colonists.Bump();
            }
            Print("colonists healed");
        });
        Register("reveal", "show all layers (cutaway off)", _ =>
        {
            _main.SetActiveZ(_world.Terrain.Depth - 1);
            Print("all layers visible");
        });
        Register("checkpoint_info", "battle checkpoint status", _ =>
        {
            var saves = _main.Saves;
            Print($"mode {_world.Time.Mode}, tick {_world.Time.TickSequence}");
            Print($"checkpoints written: {_main.Checkpoints.CheckpointsWritten}, last activation {_main.Checkpoints.LastCheckpointActivation}");
            Print($"checkpoint file: {(File.Exists(saves.CheckpointPath) ? "present" : "absent")}");
            foreach (var (path, header) in saves.ListSaves())
                Print($"  {System.IO.Path.GetFileName(path)} · order {header.SaveOrder} · {header.Mode} · writer {header.WriterId}");
        });
        Register("hash", "print the world hash", _ => Print($"world hash {SaveCodec.WorldHash(_world):x16}"));
        Register("smoke", "run the scripted smoke scenario in a fresh world", _ =>
        {
            var hash = SmokeScenario.Run(new GameWorld(), Print);
            Print($"smoke OK: {hash:x16}");
        });
        Register("save", "quicksave", _ => _main.QuickSave());
        Register("load", "quickload", _ => _main.QuickLoad());
        Register("end_battle", "auto-resolve the current battle", _ =>
        {
            if (_world.Time.Mode != SimMode.TurnBased) { Print("no battle running"); return; }
            _world.Combat.AutoResolve();
            Print("battle auto-resolved");
        });

        Print("Hollowdeep debug console — 'help' lists commands");
    }

    private void Register(string name, string help, Action<string[]> run) => _commands[name] = (help, run);

    public void Toggle()
    {
        _panel.Visible = !_panel.Visible;
        if (_panel.Visible) _input.GrabFocus();
    }

    public bool IsOpen => _panel.Visible;

    private void OnSubmitted(string text)
    {
        _input.Clear();
        text = text.Trim();
        if (text.Length == 0) return;
        Print("> " + text);
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (!_commands.TryGetValue(parts[0], out var cmd))
        {
            Print($"unknown command '{parts[0]}' — try 'help'");
            return;
        }
        try
        {
            cmd.Run(parts.Skip(1).ToArray());
        }
        catch (Exception ex)
        {
            Print($"[color=red]{ex.Message}[/color]");
        }
    }

    private void Print(string line) => _log.AppendText(line + "\n");
}
