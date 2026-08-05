using Godot;
using Hollowdeep.Core;
using Hollowdeep.Core.Save;
using Hollowdeep.Core.Sim;

namespace Hollowdeep.Game;

/// <summary>
/// Composition of the presentation layer. _Process pumps the wall clock into the time
/// authority and paces raider activations for watchability; ALL simulation advances
/// inside authority-driven ticks (ADR-0001 — nothing here mutates sim state directly).
/// Boot resumes the latest save (checkpoint included: mid-battle quits resume
/// in-battle); a fresh install starts a new colony.
/// </summary>
public partial class Main : Node3D
{
    public GameWorld World { get; private set; } = null!;
    public SaveService Saves { get; private set; } = null!;
    public CheckpointService Checkpoints { get; private set; } = null!;

    private TerrainRenderer _terrainView = null!;
    private UnitViewManager _units = null!;
    private OverlayRenderer _overlays = null!;
    private CameraRig _rig = null!;
    private ToolController _tools = null!;
    private GameUi _ui = null!;
    private DebugConsole _console = null!;
    private ConfirmationDialog _quitDialog = null!;

    private double _raiderStepTimer;
    private const double RaiderStepSeconds = 0.35; // presentation pacing between AI actions
    private bool _quitConfirmed;

    // Launch-check screenshot driver: `godot -- --shot-colony=path.png` / `--shot-combat=path.png`.
    private string? _shotPath;
    private bool _shotWantsCombat;
    private int _shotFramesLeft = -1;

    /// <summary>Set by the pause menu's "New colony": the next boot skips save resume.</summary>
    private static bool _forceNewGame;

    public override void _Ready()
    {
        GetTree().AutoAcceptQuit = false;

        World = new GameWorld();
        string saveDir = ProjectSettings.GlobalizePath("user://saves");
        Saves = new SaveService(World, saveDir);
        Checkpoints = new CheckpointService(World, Saves);
        Saves.CorruptSaveEncountered += (path, ex) =>
            _ui?.Toast($"Save '{System.IO.Path.GetFileName(path)}' is corrupt ({ex.Message}) — falling back.", 6);

        // Environment: warm hearth, cold dark.
        AddChild(new DirectionalLight3D
        {
            Rotation = new Vector3(-0.9f, 0.6f, 0),
            LightColor = new Color(0.75f, 0.8f, 0.95f),
            LightEnergy = 0.7f,
            ShadowEnabled = true,
        });
        AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0.07f, 0.08f, 0.11f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.35f, 0.38f, 0.48f),
                AmbientLightEnergy = 0.8f,
            },
        });

        _rig = new CameraRig();
        AddChild(_rig);
        _terrainView = new TerrainRenderer();
        AddChild(_terrainView);
        _units = new UnitViewManager();
        AddChild(_units);
        _overlays = new OverlayRenderer();
        AddChild(_overlays);
        _tools = new ToolController();
        AddChild(_tools);
        _ui = new GameUi();
        AddChild(_ui);
        _console = new DebugConsole();
        AddChild(_console);

        _quitDialog = new ConfirmationDialog
        {
            Title = "Quit Hollowdeep",
            OkButtonText = "Quit",
        };
        _quitDialog.Confirmed += () => { _quitConfirmed = true; DoQuit(); };
        AddChild(_quitDialog);

        // Boot: resume the latest save (the battle checkpoint wins if newest), else new game.
        string loaded;
        if (_forceNewGame)
        {
            _forceNewGame = false;
            World.NewGame();
            loaded = "(new colony)";
        }
        else if (!Saves.TryLoadLatest(out loaded))
        {
            World.NewGame();
            loaded = "(new colony)";
        }

        _terrainView.Attach(World.Terrain);
        _units.Attach(World);
        _overlays.Attach(World);
        _tools.Attach(World, _rig, _units, _terrainView);
        _ui.Attach(this, World, _tools, _terrainView, _units);
        _console.Attach(this, World);
        SetActiveZ(GameConfig.SurfaceZ);

        _ui.Toast($"Resumed: {System.IO.Path.GetFileName(loaded)}", 4);

        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--shot-colony=")) { _shotPath = arg["--shot-colony=".Length..]; _shotFramesLeft = 90; }
            else if (arg.StartsWith("--shot-combat="))
            {
                _shotPath = arg["--shot-combat=".Length..];
                _shotWantsCombat = true;
                World.Time.Speed = 3;
                World.Raids.DebugTriggerRaid();
            }
        }
    }

    public override void _Process(double delta)
    {
        // The ONLY wall-clock feed into simulation time (speed 0 freezes sub-steps;
        // SceneTree.paused is never used for gameplay).
        World.Time.AdvanceWallClock(delta);

        // Pace enemy activations so battles are watchable.
        if (World.Time.Mode == SimMode.TurnBased && !World.Combat.IsPlayerTurn)
        {
            _raiderStepTimer += delta;
            if (_raiderStepTimer >= RaiderStepSeconds)
            {
                _raiderStepTimer = 0;
                World.Combat.StepRaiderAi();
            }
        }

        DriveScreenshot();
    }

    private void DriveScreenshot()
    {
        if (_shotPath == null) return;
        if (_shotWantsCombat)
        {
            if (World.Time.Mode == SimMode.TurnBased)
            {
                _shotWantsCombat = false;
                _shotFramesLeft = 45; // let views/overlays settle on the battle
                var focus = World.Combat.Encounter.FocusCell;
                _rig.FocusOn(GameView.CellToWorldCenter(focus));
            }
            return;
        }
        if (_shotFramesLeft < 0) return;
        if (_shotFramesLeft-- > 0) return;
        var img = GetViewport().GetTexture().GetImage();
        img.SavePng(_shotPath);
        GD.Print($"screenshot saved: {_shotPath}");
        GD.Print($"draw calls this frame: {RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame)}");
        _shotPath = null;
        Checkpoints.Dispose();
        GetTree().Quit();
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed || key.Echo) return;
        if (_console.IsOpen && key.Keycode != Key.F12) return;

        switch (key.Keycode)
        {
            case Key.Space: SetSpeed(World.Time.Speed == 0 ? 1 : 0); break;
            case Key.Key1: SetSpeed(1); break;
            case Key.Key2: SetSpeed(2); break;
            case Key.Key3: SetSpeed(3); break;
            case Key.Q: ChangeZ(-1); break;
            case Key.E: ChangeZ(+1); break;
            case Key.D: _tools.SetTool(Tool.Dig); break;
            case Key.S: _tools.SetTool(Tool.StairDown); break;
            case Key.W: _tools.SetTool(Tool.BuildWall); break;
            case Key.O: _tools.SetTool(Tool.BuildDoor); break;
            case Key.T: _tools.SetTool(Tool.BuildTorch); break;
            case Key.R: _tools.SetTool(Tool.Repair); break;
            case Key.P: _tools.SetTool(Tool.Stockpile); break;
            case Key.C: _tools.SetTool(Tool.Cancel); break;
            case Key.M: _tools.CycleMaterial(); break;
            case Key.Escape:
                if (_ui.IsPauseMenuOpen) _ui.TogglePauseMenu();
                else if (_tools.ActiveTool != Tool.Select) _tools.SetTool(Tool.Select);
                else _ui.TogglePauseMenu();
                break;
            case Key.Enter or Key.KpEnter:
                if (World.Time.Mode == SimMode.TurnBased) World.Combat.EndActivation();
                break;
            case Key.F5: QuickSave(); break;
            case Key.F9: QuickLoad(); break;
            case Key.F12: _console.Toggle(); break;
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest)
        {
            if (_quitConfirmed) return;
            if (World.Time.Mode == SimMode.TurnBased)
            {
                // §4e quit confirm — the checkpoint already preserves the battle.
                _quitDialog.DialogText = "Quitting suspends the battle — it will resume exactly where you left off.";
                _quitDialog.PopupCentered();
            }
            else
            {
                DoQuit();
            }
        }
    }

    private void DoQuit()
    {
        if (World.Time.Mode == SimMode.RealTime)
            Saves.TrySaveColony(System.IO.Path.Combine(Saves.Directory, "auto-exit.save"), out _);
        Checkpoints.Dispose(); // flushes the newest checkpoint and joins the writer
        GetTree().Quit();
    }

    // ---- UI-facing controls ----

    public void SetSpeed(int speed)
    {
        World.Time.Speed = speed;
        _ui.Toast(speed == 0 ? "paused" : $"speed {World.Time.Speed}x", 1.2);
    }

    public void ChangeZ(int delta) => SetActiveZ(_terrainView.ActiveZ + delta);

    public void SetActiveZ(int z)
    {
        _terrainView.SetActiveZ(z);
        _units.SetActiveZ(_terrainView.ActiveZ);
        _overlays.SetActiveZ(_terrainView.ActiveZ);
    }

    public void FocusCell(Hollowdeep.Core.Primitives.CellCoord cell)
    {
        SetActiveZ(cell.Z);
        _rig.FocusOn(GameView.CellToWorldCenter(cell));
    }

    public void RequestQuit() => _Notification((int)NotificationWMCloseRequest);

    public void SaveSlot(int slot)
    {
        string path = System.IO.Path.Combine(Saves.Directory, $"slot{slot}.save");
        if (Saves.TrySaveColony(path, out string refusal)) _ui.Toast($"saved to slot {slot}");
        else _ui.Toast(refusal, 4);
    }

    public void LoadSlot(int slot)
    {
        string path = System.IO.Path.Combine(Saves.Directory, $"slot{slot}.save");
        if (!File.Exists(path)) { _ui.Toast($"slot {slot} is empty"); return; }
        try
        {
            Saves.Load(path);
            AfterWorldReload();
            _ui.Toast($"loaded slot {slot}");
        }
        catch (Exception ex)
        {
            _ui.Toast($"load failed: {ex.Message}", 5);
        }
    }

    /// <summary>Archives the current saves (never deletes) and restarts with a fresh mountain.</summary>
    public void NewColony()
    {
        Checkpoints.Dispose();
        string backup = System.IO.Path.Combine(Saves.Directory,
            $"backup-{Time.GetTicksMsec()}");
        Directory.CreateDirectory(backup);
        foreach (var file in Directory.EnumerateFiles(Saves.Directory))
            if (file.EndsWith(".save") || file.EndsWith(".checkpoint"))
                File.Move(file, System.IO.Path.Combine(backup, System.IO.Path.GetFileName(file)));
        _forceNewGame = true;
        GetTree().ReloadCurrentScene();
    }

    public void QuickSave()
    {
        if (Saves.TryQuickSave(out string refusal)) _ui.Toast("quicksaved");
        else _ui.Toast(refusal, 4);
    }

    public void QuickLoad()
    {
        if (!File.Exists(Saves.QuickSavePath)) { _ui.Toast("no quicksave yet"); return; }
        try
        {
            Saves.Load(Saves.QuickSavePath);
            AfterWorldReload();
            _ui.Toast("quickloaded");
        }
        catch (Exception ex)
        {
            _ui.Toast($"quickload failed: {ex.Message}", 5);
        }
    }

    private void AfterWorldReload()
    {
        _terrainView.QueueFullRepaint();
        SetActiveZ(_terrainView.ActiveZ);
    }
}
