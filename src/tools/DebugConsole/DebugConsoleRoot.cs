using Godot;
using Hollowdeep.Core.Diagnostics;

namespace Hollowdeep.Tools;

/// <summary>
/// Godot overlay for the Tier 0 debug console (autoload "DebugConsole",
/// registered in project.godot). Presentation-only: it owns a
/// <see cref="DebugConsole"/> core instance, forwards submitted lines to it,
/// and polls <see cref="DebugConsole.Revision"/> per frame to rebuild the log
/// view — it never touches simulation state itself. Systems reach the core to
/// register commands/sweeps via
/// <c>GetNode&lt;DebugConsoleRoot&gt;("/root/DebugConsole").Core</c> until the
/// composition root (ADR-0003 open question) takes over that wiring.
/// ProcessMode is Always so the console works while gameplay is paused —
/// which is legal because gameplay pause is never SceneTree.paused (ADR-0001).
/// </summary>
public partial class DebugConsoleRoot : CanvasLayer
{
    private static readonly StringName ToggleAction = new("debug_console_toggle");

    private readonly List<string> _history = [];
    private DebugConsole _core = new();
    private RichTextLabel _output = null!;
    private LineEdit _input = null!;
    private long _renderedRevision = -1;
    private int _historyIndex;

    /// <summary>The plain-C# console core; register commands and sweeps here.</summary>
    public DebugConsole Core => _core;

    /// <inheritdoc/>
    public override void _EnterTree()
    {
        ProcessMode = ProcessModeEnum.Always;
        Layer = 100;
        EnsureToggleActionExists();
    }

    /// <inheritdoc/>
    public override void _Ready()
    {
        BuildUi();
        Visible = false;
        _core.Log(DebugLogSeverity.Info, "Hollowdeep debug console. 'help' lists commands.");
    }

    /// <inheritdoc/>
    public override void _Input(InputEvent @event)
    {
        if (@event.IsActionPressed(ToggleAction))
        {
            Toggle();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (!Visible || @event is not InputEventKey { Pressed: true } key)
            return;

        switch (key.Keycode)
        {
            case Key.Escape:
                Toggle();
                GetViewport().SetInputAsHandled();
                break;
            case Key.Up:
                RecallHistory(-1);
                GetViewport().SetInputAsHandled();
                break;
            case Key.Down:
                RecallHistory(+1);
                GetViewport().SetInputAsHandled();
                break;
        }
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        if (!Visible || _core.Revision == _renderedRevision)
            return;
        _renderedRevision = _core.Revision;
        RebuildOutput();
    }

    private void Toggle()
    {
        Visible = !Visible;
        if (Visible)
        {
            _renderedRevision = -1;
            _input.Clear();
            _input.GrabFocus();
        }
        else
        {
            _input.ReleaseFocus();
        }
    }

    private void OnTextSubmitted(string text)
    {
        _input.Clear();
        if (string.IsNullOrWhiteSpace(text))
            return;
        _history.Add(text);
        _historyIndex = _history.Count;
        _core.Execute(text);
    }

    private void RecallHistory(int direction)
    {
        if (_history.Count == 0)
            return;
        _historyIndex = Mathf.Clamp(_historyIndex + direction, 0, _history.Count);
        _input.Text = _historyIndex == _history.Count ? string.Empty : _history[_historyIndex];
        _input.CaretColumn = _input.Text.Length;
    }

    private void RebuildOutput()
    {
        var builder = new System.Text.StringBuilder(_core.Entries.Count * 48);
        foreach (DebugLogEntry entry in _core.Entries)
        {
            string escaped = entry.Message.Replace("[", "[lb]");
            switch (entry.Severity)
            {
                case DebugLogSeverity.Warning:
                    builder.Append("[color=yellow]").Append(escaped).Append("[/color]\n");
                    break;
                case DebugLogSeverity.Error:
                    builder.Append("[color=salmon]").Append(escaped).Append("[/color]\n");
                    break;
                default:
                    builder.Append(escaped).Append('\n');
                    break;
            }
        }
        _output.Text = builder.ToString();
    }

    private void EnsureToggleActionExists()
    {
        // Runtime registration only when the project defines no binding, so a
        // project-settings rebind always wins over this F12 default.
        if (InputMap.HasAction(ToggleAction))
            return;
        InputMap.AddAction(ToggleAction);
        InputMap.ActionAddEvent(ToggleAction, new InputEventKey { PhysicalKeycode = Key.F12 });
    }

    private void BuildUi()
    {
        var panel = new PanelContainer();
        panel.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        panel.AnchorBottom = 0.45f;
        panel.GrowVertical = Control.GrowDirection.End;
        var background = new StyleBoxFlat { BgColor = new Color(0.05f, 0.05f, 0.08f, 0.92f) };
        panel.AddThemeStyleboxOverride("panel", background);

        var layout = new VBoxContainer();
        panel.AddChild(layout);

        _output = new RichTextLabel
        {
            BbcodeEnabled = true,
            ScrollFollowing = true,
            FocusMode = Control.FocusModeEnum.None,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        layout.AddChild(_output);

        _input = new LineEdit { PlaceholderText = "command ('help')" };
        _input.TextSubmitted += OnTextSubmitted;
        layout.AddChild(_input);

        AddChild(panel);
    }
}
