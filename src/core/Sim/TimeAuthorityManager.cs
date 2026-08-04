namespace Hollowdeep.Core.Sim;

using Hollowdeep.Core.Primitives;

/// <summary>
/// Encounter framing carried across the mode switch — never entity or terrain state
/// (ADR-0001: the swap is zero state conversion; same stores, same identities).
/// </summary>
public sealed class SwitchTransitionData
{
    public int RaidIndex;
    public string Reason = "";
    public CellCoord FocusCell;
}

/// <summary>Hooks the composition root implements so the manager stays free of store references.</summary>
public interface ISwitchHooks
{
    /// <summary>Deterministic pre-switch placement normalization (decision-set based, ADR-0003).</summary>
    void NormalizePlacements();

    /// <summary>Begin the encounter under the already-active TurnBased authority.</summary>
    void BeginEncounter(SwitchTransitionData data);

    /// <summary>Drain outcome inbox, reap ALL raiders, clear encounter side tables (ADR-0001).</summary>
    void PostEncounterReconcile();
}

/// <summary>
/// Owns {Mode, TurnIndex, TickSequence} and the swappable time authorities (ADR-0001).
/// ITickable.Tick under an authority is the only path that advances simulation state.
/// </summary>
public sealed class TimeAuthorityManager
{
    private readonly MutationGate _gate;
    private ISwitchHooks? _hooks;
    private SwitchTransitionData? _pendingSwitch;
    private bool _inSubStep;

    public SimMode Mode { get; private set; } = SimMode.RealTime;
    public int TurnIndex { get; internal set; }
    public ulong TickSequence { get; private set; }

    public RealTimeAuthority RealTime { get; }

    /// <summary>Fires after the swap into TurnBased completes (save service writes the switch-in autosave + activation-0 checkpoint).</summary>
    public event Action<SwitchTransitionData>? SwitchedToTurnBased;

    /// <summary>Fires after reconcile completes back in RealTime (battle-end autosave, scar summary UI).</summary>
    public event Action? ReturnedToRealTime;

    public TimeAuthorityManager(MutationGate gate)
    {
        _gate = gate;
        RealTime = new RealTimeAuthority(this, gate);
    }

    public void SetHooks(ISwitchHooks hooks) => _hooks = hooks;

    public int Speed
    {
        get => RealTime.Speed;
        set => RealTime.Speed = Math.Clamp(value, 0, GameConfig.SubStepsPerSecond.Length - 1);
    }

    /// <summary>Host frame pump. In TurnBased the colony is fully paused — wall time is ignored.</summary>
    public void AdvanceWallClock(double wallDt)
    {
        if (Mode == SimMode.RealTime)
            RealTime.Advance(wallDt);
    }

    internal ulong NextSequence() => ++TickSequence;

    internal void MarkSubStep(bool active) => _inSubStep = active;

    /// <summary>
    /// Colony→combat. Safe to call from a tick (defers to sub-step end) or from input
    /// handling (executes immediately under its own window).
    /// </summary>
    public void RequestSwitch(SwitchTransitionData data)
    {
        if (Mode == SimMode.TurnBased) return;
        if (_pendingSwitch != null) return;
        _pendingSwitch = data;
        if (!_inSubStep)
            ExecutePendingSwitch();
    }

    internal void ExecutePendingSwitch()
    {
        var data = _pendingSwitch;
        if (data == null || Mode == SimMode.TurnBased) return;
        _pendingSwitch = null;
        var hooks = _hooks ?? throw new InvalidOperationException("Switch hooks not wired.");
        using (var _ = _gate.Open())
        {
            hooks.NormalizePlacements();
            Mode = SimMode.TurnBased;
            hooks.BeginEncounter(data);
        }
        SwitchedToTurnBased?.Invoke(data);
    }

    /// <summary>
    /// Combat→colony. Called by the TurnBased authority once the battle has ended.
    /// Runs PostEncounterReconcile exactly once, inside one window, already in RealTime.
    /// Post-battle colony time resumes zero-elapsed (§8): the RT accumulator restarts empty.
    /// </summary>
    public void CompleteEncounter()
    {
        if (Mode != SimMode.TurnBased)
            throw new InvalidOperationException("CompleteEncounter outside TurnBased mode.");
        var hooks = _hooks ?? throw new InvalidOperationException("Switch hooks not wired.");
        using (var _ = _gate.Open())
        {
            Mode = SimMode.RealTime;
            hooks.PostEncounterReconcile();
        }
        RealTime.ResetAccumulator();
        ReturnedToRealTime?.Invoke();
    }

    /// <summary>
    /// Load-time restore of authority state. Only the save loader calls this, inside the
    /// load window (the RestoredFromCheckpoint path re-enters TurnBased without RequestSwitch).
    /// </summary>
    public void RestoreState(SimMode mode, int turnIndex, ulong tickSequence)
    {
        _gate.AssertOpen();
        Mode = mode;
        TurnIndex = turnIndex;
        TickSequence = tickSequence;
        _pendingSwitch = null;
        RealTime.ResetAccumulator();
    }
}

/// <summary>
/// Fixed-dt sub-stepping (ADR-0001): game speed multiplies the sub-step COUNT, never
/// scales dt. Speed 0 is this authority at zero sub-steps — never engine pause.
/// </summary>
public sealed class RealTimeAuthority
{
    private readonly TimeAuthorityManager _manager;
    private readonly MutationGate _gate;
    private readonly List<ITickable> _systems = new();
    private double _accumulator;

    /// <summary>Cap on sub-steps consumed per Advance call, to bound death-spiral frames.</summary>
    public const int MaxSubStepsPerAdvance = 60;

    public int Speed = 1;

    public RealTimeAuthority(TimeAuthorityManager manager, MutationGate gate)
    {
        _manager = manager;
        _gate = gate;
    }

    /// <summary>Tick order is fixed at composition and is part of determinism.</summary>
    public void AddSystem(ITickable system) => _systems.Add(system);

    public void ResetAccumulator() => _accumulator = 0;

    public void Advance(double wallDt)
    {
        int stepsPerSecond = GameConfig.SubStepsPerSecond[Speed];
        if (stepsPerSecond == 0) return; // paused: sim time frozen, presentation still runs
        _accumulator += wallDt * (stepsPerSecond / 60.0);
        int steps = 0;
        while (_accumulator >= GameConfig.FixedDt && steps < MaxSubStepsPerAdvance)
        {
            _accumulator -= GameConfig.FixedDt;
            steps++;
            SubStep();
            if (_manager.Mode != SimMode.RealTime) { _accumulator = 0; return; }
        }
        if (steps == MaxSubStepsPerAdvance) _accumulator = 0; // shed backlog, keep frame alive
    }

    /// <summary>Exactly N sub-steps, used by headless harnesses and tests.</summary>
    public void Step(int subSteps)
    {
        for (int i = 0; i < subSteps && _manager.Mode == SimMode.RealTime; i++)
            SubStep();
    }

    private void SubStep()
    {
        ulong seq = _manager.NextSequence();
        var ctx = new TimeContext(SimMode.RealTime, GameConfig.FixedDt, seq, _manager.TurnIndex);
        _manager.MarkSubStep(true);
        try
        {
            using var _ = _gate.Open();
            for (int i = 0; i < _systems.Count; i++)
                _systems[i].Tick(in ctx);
        }
        finally
        {
            _manager.MarkSubStep(false);
        }
        _manager.ExecutePendingSwitch();
    }
}
