// PROTOTYPE - NOT FOR PRODUCTION
// Question: does ADR-0001's Time Authority / mode-switch architecture hold — one world,
//   swappable time authority, zero state conversion, headless, deterministic?
// Date: 2026-07-26

namespace SaveLoadSpike;

public enum TimeAuthorityMode { RealTime, TurnBased }

public readonly record struct TimeContext(
    TimeAuthorityMode Mode,
    double DeltaSeconds,   // fixed sub-step dt in RealTime; ALWAYS 0 in TurnBased
    int TurnIndex,         // increments per ACTOR ACTIVATION in TurnBased; -1 in RealTime
    ulong TickSequence);   // monotonic across both modes

public interface ITickable
{
    void Tick(TimeContext context);
}

public enum TickPhase { Input, Simulation, Reaction, Presentation }

public interface ITimeAuthority
{
    void Advance(double engineDeltaSeconds);
    bool IsActive { get; }
}

public enum SwitchResult { Accepted, RejectedAlreadyInMode, RejectedEncounterActive, DeferredMidDispatch }

public enum SwitchTriggerReason { Breach, DebugCommand }

public readonly record struct SwitchTransitionData(
    int EncounterId,
    SwitchTriggerReason TriggerReason,
    IReadOnlyList<CellCoord> BreachCells,
    IReadOnlyList<EntityId> ParticipantIds);

public readonly record struct ModeTransition(TimeAuthorityMode From, TimeAuthorityMode To, int EncounterId);

/// <summary>Plain C#; the view layer satisfies it, tests stub it (ADR-0001 C2).</summary>
public interface IPresentationGate
{
    bool IsComplete { get; }
}

/// <summary>Instant-completion stub — the canary for "no Godot in the authority".</summary>
public sealed class InstantPresentationGate : IPresentationGate
{
    public bool IsComplete => true;
}

public readonly record struct TimeAuthoritySnapshot(TimeAuthorityMode Mode, int TurnIndex, ulong TickSequence);

/// <summary>Consumers of the PostEncounterReconcile pass (ADR-0001 step 4).</summary>
public interface IReconcileConsumer
{
    void Reconcile();
}

public readonly record struct EntityId(long Value)
{
    public static readonly EntityId None = new(0);
}

public sealed class TimeAuthorityManager
{
    private sealed record Registration(ITickable System, TimeAuthorityMode Authority, TickPhase Phase, int Priority, int Sequence);

    private readonly List<Registration> _realTime = [];
    private readonly List<Registration> _turnBased = [];
    private readonly List<(IReconcileConsumer consumer, TickPhase phase, int priority, int seq)> _reconcile = [];
    private int _registrationSeq;
    private bool _dispatching;
    private bool _reconcilePending;

    public RealTimeAuthority RealTime { get; }
    public TurnBasedAuthority TurnBased { get; }
    public MutationWindow Window { get; } = new();

    public TimeAuthorityMode CurrentMode { get; private set; } = TimeAuthorityMode.RealTime;
    public bool SwitchPending { get; private set; }
    public TimeAuthorityMode PendingSwitchTarget { get; private set; }
    public int TurnIndex { get; internal set; } = -1;
    public ulong TickSequence { get; private set; }
    public int ActiveEncounterId { get; private set; }

    public event Action<ModeTransition>? ModeTransitioned;

    /// <summary>Instrumentation for the spike only — counts dispatches per mode.</summary>
    public int RealTimeTickCount { get; private set; }
    public int TurnBasedTickCount { get; private set; }
    public int ReconcileRunCount { get; private set; }

    public TimeAuthorityManager(IPresentationGate gate)
    {
        RealTime = new RealTimeAuthority(this);
        TurnBased = new TurnBasedAuthority(this, gate);
    }

    public void Register(ITickable system, TimeAuthorityMode authority, TickPhase phase, int priority = 0)
    {
        List<Registration> list = authority == TimeAuthorityMode.RealTime ? _realTime : _turnBased;
        foreach (Registration r in list)
            if (r.Phase == phase && r.Priority == priority)
                throw new InvalidOperationException(
                    $"Dispatch-order collision: {phase}/{priority} already taken by {r.System.GetType().Name} " +
                    $"(ADR-0001 requires a deterministic total order).");
        list.Add(new Registration(system, authority, phase, priority, _registrationSeq++));
        list.Sort(static (a, b) => a.Phase != b.Phase ? a.Phase.CompareTo(b.Phase)
                                 : a.Priority != b.Priority ? a.Priority.CompareTo(b.Priority)
                                 : a.Sequence.CompareTo(b.Sequence));
    }

    public void Unregister(ITickable system, TimeAuthorityMode authority)
    {
        List<Registration> list = authority == TimeAuthorityMode.RealTime ? _realTime : _turnBased;
        list.RemoveAll(r => ReferenceEquals(r.System, system));
    }

    public void RegisterReconcileConsumer(IReconcileConsumer consumer, TickPhase phase = TickPhase.Reaction, int priority = 0)
    {
        _reconcile.Add((consumer, phase, priority, _registrationSeq++));
        _reconcile.Sort(static (a, b) => a.phase != b.phase ? a.phase.CompareTo(b.phase)
                                       : a.priority != b.priority ? a.priority.CompareTo(b.priority)
                                       : a.seq.CompareTo(b.seq));
    }

    public SwitchResult RequestSwitch(TimeAuthorityMode target, SwitchTransitionData transition)
    {
        if (target == CurrentMode) return SwitchResult.RejectedAlreadyInMode;
        // Single-encounter invariant enforced HERE — the manager rejects, never queues.
        if (target == TimeAuthorityMode.TurnBased && (SwitchPending || ActiveEncounterId != 0))
            return SwitchResult.RejectedEncounterActive;

        if (_dispatching)
        {
            SwitchPending = true;
            PendingSwitchTarget = target;
            _pendingTransition = transition;
            return SwitchResult.DeferredMidDispatch;
        }
        PerformSwap(target, transition);
        return SwitchResult.Accepted;
    }

    private SwitchTransitionData _pendingTransition;

    private void PerformSwap(TimeAuthorityMode target, SwitchTransitionData transition)
    {
        TimeAuthorityMode from = CurrentMode;
        ModeTransitioned?.Invoke(new ModeTransition(from, target, transition.EncounterId));
        CurrentMode = target;
        SwitchPending = false;

        if (target == TimeAuthorityMode.TurnBased)
        {
            ActiveEncounterId = transition.EncounterId;
            TurnIndex = 0;
            TurnBased.BeginEncounter(transition);
        }
        else
        {
            ActiveEncounterId = 0;
            TurnIndex = -1;
            _reconcilePending = true;      // ADR-0001 step 4: runs on the FIRST real-time dispatch
        }
    }

    /// <summary>Called once per engine physics frame.</summary>
    public void Advance(double engineDeltaSeconds)
    {
        if (CurrentMode == TimeAuthorityMode.RealTime) RealTime.Advance(engineDeltaSeconds);
        else TurnBased.Advance(engineDeltaSeconds);
    }

    /// <summary>One dispatch = one pass over the active authority's registry, in deterministic order.</summary>
    internal void Dispatch(double deltaSeconds)
    {
        bool realTime = CurrentMode == TimeAuthorityMode.RealTime;

        if (realTime && _reconcilePending)
        {
            _reconcilePending = false;
            ReconcileRunCount++;
            using (Window.Open())
                foreach ((IReconcileConsumer consumer, _, _, _) in _reconcile)
                    consumer.Reconcile();
        }

        List<Registration> list = realTime ? _realTime : _turnBased;
        var ctx = new TimeContext(CurrentMode, realTime ? deltaSeconds : 0.0, TurnIndex, ++TickSequence);

        _dispatching = true;
        try
        {
            using (Window.Open())
                foreach (Registration r in list)
                    r.System.Tick(ctx);
        }
        finally { _dispatching = false; }

        if (realTime) RealTimeTickCount++; else TurnBasedTickCount++;

        // Deferred switch applies BETWEEN dispatches, atomically (ADR-0001 step 1).
        if (SwitchPending) PerformSwap(PendingSwitchTarget, _pendingTransition);
    }

    public TimeAuthoritySnapshot Snapshot()
    {
        if (CurrentMode != TimeAuthorityMode.RealTime)
            throw new InvalidOperationException("CD-9: a save whose Mode is TurnBased is corrupt, not a supported state.");
        return new TimeAuthoritySnapshot(CurrentMode, TurnIndex, TickSequence);
    }

    public void Restore(TimeAuthoritySnapshot snapshot)
    {
        if (snapshot.Mode != TimeAuthorityMode.RealTime)
            throw new InvalidOperationException("CD-9: refusing to restore a TurnBased snapshot.");
        CurrentMode = snapshot.Mode;
        TurnIndex = snapshot.TurnIndex;
        TickSequence = snapshot.TickSequence;
    }
}

/// <summary>Fixed-dt sub-stepping. Speed multiplies the NUMBER of sub-steps, never dt.</summary>
public sealed class RealTimeAuthority(TimeAuthorityManager manager) : ITimeAuthority
{
    public const double FixedDt = 1.0 / 60.0;
    public const int MaxSubStepsPerFrame = 8;      // sim SLOWS rather than spiral-catching-up

    private double _accumulator;

    public int Speed { get; set; } = 1;            // 0 = gameplay pause; 1/2/3 = game speed
    public bool IsActive => manager.CurrentMode == TimeAuthorityMode.RealTime;
    public int SubStepsLastFrame { get; private set; }
    public int DroppedStepsLastFrame { get; private set; }

    public void Advance(double engineDeltaSeconds)
    {
        SubStepsLastFrame = 0;
        DroppedStepsLastFrame = 0;

        if (Speed == 0) { _accumulator = 0; return; }   // pause: time does not accumulate

        _accumulator += engineDeltaSeconds;
        while (_accumulator >= FixedDt)
        {
            _accumulator -= FixedDt;
            for (int s = 0; s < Speed; s++)
            {
                if (SubStepsLastFrame >= MaxSubStepsPerFrame)
                {
                    DroppedStepsLastFrame++;
                    _accumulator = 0;                    // drop backlog; the sim slows down
                    return;
                }
                SubStepsLastFrame++;
                manager.Dispatch(FixedDt);
                if (manager.CurrentMode != TimeAuthorityMode.RealTime) return;  // switched mid-frame
            }
        }
    }
}

public enum TurnState { AwaitingInput, ResolvingAction, AwaitingPresentation, NextActor }

/// <summary>Explicit state machine; publishes DeltaSeconds = 0; gated on presentation.</summary>
public sealed class TurnBasedAuthority(TimeAuthorityManager manager, IPresentationGate gate) : ITimeAuthority
{
    private readonly List<EntityId> _activationOrder = [];
    private int _actorCursor;

    public bool IsActive => manager.CurrentMode == TimeAuthorityMode.TurnBased;
    public TurnState State { get; private set; } = TurnState.AwaitingInput;
    public EntityId CurrentActor => _activationOrder.Count == 0 ? EntityId.None : _activationOrder[_actorCursor];
    public double RealTimeInEncounter { get; private set; }     // receives REAL delta for its own timeouts
    public Func<EntityId, bool>? SubmitActionFor;               // test/AI hook: returns true when an action is submitted
    public Action<EntityId>? ResolveAction;

    internal void BeginEncounter(SwitchTransitionData transition)
    {
        _activationOrder.Clear();
        _activationOrder.AddRange(transition.ParticipantIds);
        _actorCursor = 0;
        State = TurnState.AwaitingInput;
        RealTimeInEncounter = 0;
    }

    /// <summary>Receives REAL delta even though it publishes 0 — deliberate; do not "fix".</summary>
    public void Advance(double engineDeltaSeconds)
    {
        RealTimeInEncounter += engineDeltaSeconds;
        if (_activationOrder.Count == 0) return;

        switch (State)
        {
            case TurnState.AwaitingInput:
                if (SubmitActionFor?.Invoke(CurrentActor) == true) State = TurnState.ResolvingAction;
                break;

            case TurnState.ResolvingAction:
                ResolveAction?.Invoke(CurrentActor);
                manager.Dispatch(0.0);                       // discrete tick on a scheduler event
                State = TurnState.AwaitingPresentation;
                break;

            case TurnState.AwaitingPresentation:
                if (gate.IsComplete) State = TurnState.NextActor;
                break;

            case TurnState.NextActor:
                _actorCursor = (_actorCursor + 1) % _activationOrder.Count;
                manager.TurnIndex++;                          // per ACTOR ACTIVATION
                State = TurnState.AwaitingInput;
                manager.Dispatch(0.0);
                break;
        }
    }

    /// <summary>Encounter-scoped: removing a dead participant must not corrupt the cursor.</summary>
    public void RemoveParticipant(EntityId id)
    {
        int i = _activationOrder.IndexOf(id);
        if (i < 0) return;
        _activationOrder.RemoveAt(i);
        if (_activationOrder.Count == 0) { _actorCursor = 0; return; }
        if (i < _actorCursor) _actorCursor--;
        if (_actorCursor >= _activationOrder.Count) _actorCursor = 0;
    }

    public int ParticipantCount => _activationOrder.Count;
}
