// PROTOTYPE - NOT FOR PRODUCTION
// Question: ADR-0001's validation criteria and dispatch rules, made executable.
// Date: 2026-07-26

namespace ModeSwitchSpike;

/// <summary>Bus adapter: fans terrain batches out to subscribers as idempotent bookkeeping.</summary>
public sealed class WorldChangeBus : ITerrainChangeSink
{
    public List<Action<CellCoord>> Subscribers { get; } = [];
    public int WorldReloadedCount { get; private set; }
    public int BatchCount { get; private set; }

    public void Publish(in TerrainChangeBatch batch)
    {
        BatchCount++;
        foreach (TerrainChange c in batch.Changes)
            foreach (Action<CellCoord> s in Subscribers) s(c.Cell);
    }
    public void PublishWorldReloaded() => WorldReloadedCount++;
}

public sealed class Fixture
{
    public TimeAuthorityManager Manager { get; }
    public TerrainWorld World { get; }
    public WorldChangeBus Bus { get; } = new();
    public MaterialCatalog Catalog { get; } = new();
    public EntityIdSource Ids { get; } = new();
    public UnitOccupancyIndex Occupancy { get; }
    public ColonistStore Colonists { get; }
    public RaiderStore Raiders { get; }
    public StackReservationTable Reservations { get; } = new();
    public EncounterOutcomeInbox Inbox { get; } = new();

    public NeedsSystem Needs { get; }
    public JobAssignmentSystem Jobs { get; } = new();
    public PathfindingSystem Pathfinding { get; } = new();
    public RaidTriggerSystem RaidTrigger { get; }
    public SquadPrepSystem SquadPrep { get; }
    public ColonistMovementSystem Movement { get; }
    public CombatTurnOrderSystem TurnOrder { get; }
    public CombatTargetingSystem Targeting { get; }
    public IdentityBookkeeping Identity { get; }
    public NotificationsSystem Notifications { get; } = new();
    public ReconcileHousekeeping Housekeeping { get; }

    public ushort Dirt, Floor;

    public Fixture()
    {
        Manager = new TimeAuthorityManager(new InstantPresentationGate());
        Dirt = Catalog.Register("dirt", 1, 30);
        Floor = Catalog.Register("rock_floor", 1, 0);
        World = new TerrainWorld(new WorldBounds(32, 32, 2), Catalog, Bus, Manager.Window, 32);
        World.PopulateForLoad(w =>
        {
            for (int z = 0; z < 2; z++)
                for (int y = 0; y < 32; y++)
                    for (int x = 0; x < 32; x++)
                        w.LoadSetCell(new CellCoord(x, y, z), new TerrainCell { FloorTypeId = Floor });
        });

        Occupancy = new UnitOccupancyIndex(() => Manager.CurrentMode);
        Colonists = new ColonistStore(Ids, Occupancy, Manager.Window, () => Manager.CurrentMode);
        Raiders = new RaiderStore(Ids, Occupancy, Manager.Window);

        Needs = new NeedsSystem(Colonists);
        RaidTrigger = new RaidTriggerSystem(Manager, Raiders, Colonists);
        Movement = new ColonistMovementSystem(Colonists);
        SquadPrep = new SquadPrepSystem(Manager, Colonists, Occupancy, World, Movement);
        TurnOrder = new CombatTurnOrderSystem(Manager, Colonists, Raiders, Inbox);
        Targeting = new CombatTargetingSystem(Colonists, Raiders, World);
        Identity = new IdentityBookkeeping(Colonists, Inbox);
        Housekeeping = new ReconcileHousekeeping(Colonists, Raiders, Reservations, Jobs, Pathfinding, World);

        // Registration = the worked-example table, executable.
        Manager.Register(Needs, TimeAuthorityMode.RealTime, TickPhase.Simulation, 0);
        Manager.Register(Jobs, TimeAuthorityMode.RealTime, TickPhase.Simulation, 1);
        Manager.Register(Movement, TimeAuthorityMode.RealTime, TickPhase.Simulation, 2);
        Manager.Register(RaidTrigger, TimeAuthorityMode.RealTime, TickPhase.Simulation, 3);
        Manager.Register(Pathfinding, TimeAuthorityMode.RealTime, TickPhase.Simulation, 4);
        Manager.Register(SquadPrep, TimeAuthorityMode.RealTime, TickPhase.Reaction, 0);
        Manager.Register(Movement, TimeAuthorityMode.RealTime, TickPhase.Reaction, 1);   // placement execution
        Manager.Register(TurnOrder, TimeAuthorityMode.TurnBased, TickPhase.Simulation, 0);
        Manager.Register(Targeting, TimeAuthorityMode.TurnBased, TickPhase.Simulation, 1);
        Manager.Register(Pathfinding, TimeAuthorityMode.TurnBased, TickPhase.Simulation, 2);

        Manager.RegisterReconcileConsumer(Identity, TickPhase.Reaction, 0);
        Notifications.PeekReport = () => Identity.LastReport;
        Manager.RegisterReconcileConsumer(Notifications, TickPhase.Reaction, 1);
        Manager.RegisterReconcileConsumer(Housekeeping, TickPhase.Reaction, 2);

        Bus.Subscribers.Add(Jobs.OnTerrainChanged);
        Bus.Subscribers.Add(Pathfinding.OnTerrainChanged);
    }

    public void PumpRealTime(int frames, double frameDelta = 1.0 / 60.0)
    {
        for (int i = 0; i < frames && Manager.CurrentMode == TimeAuthorityMode.RealTime; i++)
            Manager.Advance(frameDelta);
    }

    public void PumpTurnBased(int steps, double frameDelta = 1.0 / 60.0)
    {
        for (int i = 0; i < steps && Manager.CurrentMode == TimeAuthorityMode.TurnBased; i++)
            Manager.Advance(frameDelta);
    }
}

public static class Tests
{
    private static int _pass, _fail;

    private static void Check(bool cond, string name)
    {
        if (cond) { _pass++; Console.WriteLine($"  PASS  {name}"); }
        else { _fail++; Console.WriteLine($"  FAIL  {name}"); }
    }

    private static void Throws<T>(Action a, string name) where T : Exception
    {
        try { a(); Check(false, name + " (no throw)"); }
        catch (T) { Check(true, name); }
        catch (Exception e) { Check(false, $"{name} (wrong exception {e.GetType().Name}: {e.Message})"); }
    }

    public static int RunAll()
    {
        Console.WriteLine("\n=== ADR-0001 CONTRACT: dispatch, speed, switch, reconcile ===");
        DispatchRules();
        SpeedControl();
        SwitchSemantics();
        FullEncounterHeadless();
        PreSwitchNormalization();
        ZeroOrphanReconcile();
        DeterminismAndContinuity();
        SerializationInvariant();
        WriterDiscipline();
        Console.WriteLine($"\n  {_pass} passed, {_fail} failed");
        return _fail;
    }

    private static void DispatchRules()
    {
        var f = new Fixture();
        f.Colonists.Spawn("Berta", new CellCoord(10, 10, 0), 10);
        f.RaidTrigger.Armed = false;

        f.PumpRealTime(10);
        Check(f.Needs.TickCount > 0, "RealTime systems tick under RealTimeAuthority");
        Check(f.TurnOrder.TickCount == 0, "inactive authority's systems receive ZERO ticks (true pause)");

        Throws<InvalidOperationException>(
            () => f.Manager.Register(f.Needs, TimeAuthorityMode.RealTime, TickPhase.Simulation, 0),
            "duplicate phase+priority is rejected (deterministic total order)");

        // Deterministic order: Simulation before Reaction, priority ascending.
        var order = new List<string>();
        var g = new Fixture();
        var a = new OrderProbe("A", order); var b = new OrderProbe("B", order); var c = new OrderProbe("C", order);
        g.Manager.Register(a, TimeAuthorityMode.RealTime, TickPhase.Reaction, 5);
        g.Manager.Register(b, TimeAuthorityMode.RealTime, TickPhase.Input, 0);
        g.Manager.Register(c, TimeAuthorityMode.RealTime, TickPhase.Reaction, 2);
        g.RaidTrigger.Armed = false;
        g.PumpRealTime(1);
        Check(order.SequenceEqual(["B", "C", "A"]), "dispatch order is (phase, priority) — not registration order");

        Check(f.Bus.WorldReloadedCount == 1, "load published WorldReloaded once, zero per-cell events");
    }

    private sealed class OrderProbe(string name, List<string> sink) : ITickable
    {
        public void Tick(TimeContext ctx) => sink.Add(name);
    }

    private static void SpeedControl()
    {
        // Criterion 3: adding 3x speed touches ZERO ITickable implementations.
        var deltas = new List<double>();
        foreach (int speed in new[] { 0, 1, 2, 3 })
        {
            var f = new Fixture();
            f.RaidTrigger.Armed = false;
            f.Manager.RealTime.Speed = speed;
            int before = f.Needs.TickCount;
            f.PumpRealTime(60, 1.0 / 60.0);          // one second of frames
            int ticks = f.Needs.TickCount - before;
            deltas.Add(f.Needs.SimulatedSeconds);
            if (speed == 0) Check(ticks == 0, "speed 0 = zero sub-steps (gameplay pause, not SceneTree.paused)");
            else Check(ticks == 60 * speed, $"speed {speed} runs {60 * speed} sub-steps in 60 frames (N scales, dt fixed)");
        }
        Check(Math.Abs(deltas[2] - 2 * deltas[1]) < 1e-9 && Math.Abs(deltas[3] - 3 * deltas[1]) < 1e-9,
              "simulated time scales linearly with speed — dt itself never scales");

        // Slow frame: sim slows rather than spiral-catching-up.
        var s = new Fixture();
        s.RaidTrigger.Armed = false;
        s.Manager.RealTime.Speed = 3;
        s.Manager.Advance(1.0);                       // a catastrophic 1-second frame
        Check(s.Manager.RealTime.SubStepsLastFrame <= RealTimeAuthority.MaxSubStepsPerFrame,
              $"slow frame clamps at {RealTimeAuthority.MaxSubStepsPerFrame} sub-steps (no death spiral)");
        Check(s.Manager.RealTime.DroppedStepsLastFrame > 0, "backlog is dropped — the simulation SLOWS DOWN");
    }

    private static void SwitchSemantics()
    {
        var f = new Fixture();
        f.Colonists.Spawn("Berta", new CellCoord(10, 10, 0), 10);
        var empty = new SwitchTransitionData(1, SwitchTriggerReason.DebugCommand, [], []);

        Check(f.Manager.RequestSwitch(TimeAuthorityMode.RealTime, empty) == SwitchResult.RejectedAlreadyInMode,
              "switching to the current mode is rejected");

        Check(f.Manager.RequestSwitch(TimeAuthorityMode.TurnBased, empty) == SwitchResult.Accepted,
              "out-of-dispatch switch applies immediately");
        Check(f.Manager.CurrentMode == TimeAuthorityMode.TurnBased, "mode swapped");
        Check(f.Manager.RequestSwitch(TimeAuthorityMode.TurnBased, empty) == SwitchResult.RejectedAlreadyInMode,
              "single-encounter invariant: already in TurnBased");

        // Mid-dispatch request defers to end-of-dispatch.
        var g = new Fixture();
        g.Colonists.Spawn("Konrad", new CellCoord(5, 5, 0), 10);
        g.RaidTrigger.ThreatPerSecond = 10_000;       // trigger on the first sub-step
        g.PumpRealTime(1);
        Check(g.RaidTrigger.LastResult == SwitchResult.DeferredMidDispatch,
              "RequestSwitch from inside a Tick returns DeferredMidDispatch");
        Check(g.Manager.CurrentMode == TimeAuthorityMode.TurnBased,
              "deferred switch applies BETWEEN dispatches, atomically");

        // A second encounter while one is active is rejected, never queued.
        var h = new Fixture();
        h.Colonists.Spawn("Ulli", new CellCoord(5, 5, 0), 10);
        h.Manager.RequestSwitch(TimeAuthorityMode.TurnBased, empty);
        Check(h.Manager.RequestSwitch(TimeAuthorityMode.TurnBased, new SwitchTransitionData(2, SwitchTriggerReason.Breach, [], []))
              == SwitchResult.RejectedAlreadyInMode,
              "exactly one active encounter — the manager rejects, never queues");
    }

    private static void FullEncounterHeadless()
    {
        // Criterion 1: full turn loop runs headlessly with a stub presentation gate.
        var f = new Fixture();
        EntityId c1 = f.Colonists.Spawn("Berta", new CellCoord(10, 10, 0), 10);
        EntityId c2 = f.Colonists.Spawn("Konrad", new CellCoord(12, 12, 0), 10);
        f.RaidTrigger.ThreatPerSecond = 10_000;

        f.PumpRealTime(1);
        Check(f.Manager.CurrentMode == TimeAuthorityMode.TurnBased, "raid trigger drove the switch into tactics");
        f.TurnOrder.Participants.AddRange([c1, c2]);

        int submissions = 0;
        f.Manager.TurnBased.SubmitActionFor = _ => { submissions++; return true; };
        f.Manager.TurnBased.ResolveAction = _ => { };

        int turnsBefore = f.Manager.TurnIndex;
        f.PumpTurnBased(40);
        Check(f.Manager.TurnBasedTickCount > 0, "TurnBased authority dispatched discrete ticks");
        Check(f.Manager.TurnIndex > turnsBefore, "TurnIndex advances per actor activation");
        Check(f.Needs.TickCount == 1, "colony is FULLY PAUSED during the encounter (needs froze)");

        double deltaSeen = -1;
        var probe = new DeltaProbe(d => deltaSeen = d);
        f.Manager.Register(probe, TimeAuthorityMode.TurnBased, TickPhase.Presentation, 99);
        f.PumpTurnBased(8);
        Check(deltaSeen == 0.0, "TurnBased publishes DeltaSeconds = 0 ALWAYS");
        Check(f.Manager.TurnBased.RealTimeInEncounter > 0,
              "the authority still receives REAL delta for its own gating (deliberate, not a bug)");
    }

    private sealed class DeltaProbe(Action<double> sink) : ITickable
    {
        public void Tick(TimeContext ctx) => sink(ctx.DeltaSeconds);
    }

    private static void PreSwitchNormalization()
    {
        var f = new Fixture();
        // Three colonists deliberately co-located — legal under RealTime (advisory occupancy).
        EntityId a = f.Colonists.Spawn("A", new CellCoord(8, 8, 0), 10);
        EntityId b = f.Colonists.Spawn("B", new CellCoord(8, 8, 0), 10);
        EntityId c = f.Colonists.Spawn("C", new CellCoord(8, 8, 0), 10);
        Check(f.Occupancy.At(new CellCoord(8, 8, 0)).Count == 3,
              "RealTime occupancy is ADVISORY — transient overlap is legal");

        f.RaidTrigger.ThreatPerSecond = 10_000;
        f.PumpRealTime(1);

        Check(f.SquadPrep.NormalizationRuns == 1, "Squad Prep ran normalization on SwitchPending (one pass)");
        Check(f.Movement.ExecutedPlacements == 2, "Colonist Movement EXECUTED the placements (Squad Prep only decided)");
        Check(f.Colonists.Get(a).Cell == new CellCoord(8, 8, 0), "lowest EntityId keeps its cell (deterministic rule)");
        Check(f.Colonists.Get(b).Cell != f.Colonists.Get(c).Cell, "co-located units were separated");
        Check(f.Occupancy.At(new CellCoord(8, 8, 0)).Count == 1, "cell is exclusive after normalization");
        Check(f.Manager.CurrentMode == TimeAuthorityMode.TurnBased, "switch completed after normalization");

        // Determinism: same setup → same placement.
        static (CellCoord, CellCoord, CellCoord) Run()
        {
            var g = new Fixture();
            EntityId x = g.Colonists.Spawn("A", new CellCoord(8, 8, 0), 10);
            EntityId y = g.Colonists.Spawn("B", new CellCoord(8, 8, 0), 10);
            EntityId z = g.Colonists.Spawn("C", new CellCoord(8, 8, 0), 10);
            g.RaidTrigger.ThreatPerSecond = 10_000;
            g.PumpRealTime(1);
            return (g.Colonists.Get(x).Cell, g.Colonists.Get(y).Cell, g.Colonists.Get(z).Cell);
        }
        Check(Run() == Run(), "normalization is DETERMINISTIC (same setup -> same placement)");

        // Exclusivity assertion arms once TurnBased is active.
        var h = new Fixture();
        EntityId h1 = h.Colonists.Spawn("A", new CellCoord(4, 4, 0), 10);
        EntityId h2 = h.Colonists.Spawn("B", new CellCoord(5, 5, 0), 10);
        h.Manager.RequestSwitch(TimeAuthorityMode.TurnBased,
            new SwitchTransitionData(1, SwitchTriggerReason.Breach, [], [h1, h2]));
        Throws<InvalidOperationException>(
            () => { using (h.Manager.Window.Open()) h.Colonists.SetCell(h2, new CellCoord(4, 4, 0)); },
            "occupancy exclusivity is a HARD invariant under TurnBased (asserted)");
    }

    private static void ZeroOrphanReconcile()
    {
        // Criterion 4: destroy terrain mid-encounter -> resume -> zero orphans.
        var f = new Fixture();
        EntityId keeper = f.Colonists.Spawn("Berta", new CellCoord(10, 10, 0), 10);
        EntityId doomed = f.Colonists.Spawn("Konrad", new CellCoord(20, 20, 0), 10);

        var jobCell = new CellCoord(15, 15, 0);
        var pathCell = new CellCoord(16, 15, 0);
        using (f.Manager.Window.Open())
        {
            f.World.SetWall(jobCell, f.Dirt, 0);
            f.World.SetWall(pathCell, f.Dirt, 0);
        }
        f.Jobs.Jobs.Add(new Job(1, jobCell, doomed));
        f.Jobs.Jobs.Add(new Job(2, new CellCoord(3, 3, 0), keeper));
        f.Pathfinding.CachedPaths.Add([new CellCoord(1, 1, 0), pathCell, new CellCoord(17, 15, 0)]);
        f.Pathfinding.CachedPaths.Add([new CellCoord(1, 1, 0), new CellCoord(2, 2, 0)]);
        var stack = new EntityId(9999);
        f.Reservations.Reserve(stack, doomed, 5);
        Check(f.Reservations.Count == 1 && f.Jobs.Jobs.Count == 2 && f.Pathfinding.CachedPaths.Count == 2,
              "pre-encounter: 1 reservation, 2 jobs, 2 cached paths");

        f.RaidTrigger.ThreatPerSecond = 10_000;
        f.PumpRealTime(1);
        Check(f.Manager.CurrentMode == TimeAuthorityMode.TurnBased, "in encounter");
        f.TurnOrder.Participants.AddRange([keeper, doomed]);

        using (f.Manager.Window.Open())
        {
            f.Targeting.BreachWall(jobCell, 999);                 // destroys the job's target
            f.Targeting.BreachWall(pathCell, 999);                // destroys geometry a path crosses
            f.Targeting.DamageColonist(doomed, 999);              // kills the reservation holder
        }
        Check(f.Colonists.Get(doomed).IsDead, "colonist died in the encounter");
        Check(f.Colonists.Exists(doomed), "dead colonist is NOT despawned inside the encounter (CD-4 death cell survives)");

        // Battle ends; Turn Order deposits the report; switch back.
        f.TurnOrder.EndBattle(true);
        f.Manager.RequestSwitch(TimeAuthorityMode.RealTime, new SwitchTransitionData(1, SwitchTriggerReason.Breach, [], []));
        Check(f.Manager.ReconcileRunCount == 0, "reconcile has not run yet (it runs on the FIRST real-time dispatch)");

        f.RaidTrigger.Armed = false;
        f.PumpRealTime(1);
        Check(f.Manager.ReconcileRunCount == 1, "PostEncounterReconcile ran exactly once, on the first real-time dispatch");
        Check(f.Identity.ReportsConsumed == 1, "exactly ONE EncounterOutcomeReport was drained");
        Check(!f.Inbox.HasReport, "inbox is empty after the drain (one slot, transient)");
        Check(f.Housekeeping.ReservationsReleased == 1 && f.Reservations.Count == 0,
              "ZERO orphaned reservations — ReleaseAllHeldBy(dead) ran");
        Check(!f.Jobs.Jobs.Any(j => f.World.GetCell(j.Target).WallTypeId == 0 && f.Jobs.CellsFlaggedByBus.Contains(j.Target))
              && !f.Jobs.Jobs.Any(j => j.AssignedTo.Value == doomed.Value)
              && f.Jobs.Jobs.Count == 1 && f.Jobs.Jobs[0].Id == 2,
              "ZERO jobs targeting destroyed cells or owned by the dead — and the untouched job SURVIVES");
        Check(f.Pathfinding.CachedPaths.Count == 1 && f.Housekeeping.PathsInvalidated == 1,
              "ZERO paths crossing destroyed geometry (the untouched path survives)");
        Check(!f.Colonists.Exists(doomed) && f.Housekeeping.ColonistsReaped == 1,
              "the dead colonist was reaped in RealTime (combat marks, RealTime reaps)");
        Check(f.Colonists.Get(keeper).BattlesSurvived == 1,
              "BattlesSurvived incremented for the SURVIVOR only (CD-4)");
        Check(f.Notifications.Messages.Count == 1 && f.Notifications.Messages[0].Contains("died at"),
              "named death notification with location was emitted (CD-4)");
        Check(f.Housekeeping.RaidersReaped == 3 && f.Raiders.Count == 0,
              "ALL raiders reaped at reconcile — none may outlive the encounter (spike-corrected rule)");
        Check(f.Occupancy.Verify(f.Colonists, f.Raiders, out string detail), $"occupancy sweep clean after reconcile ({detail})");
    }

    private static void DeterminismAndContinuity()
    {
        // Criterion 2: TickSequence continuity across a full RT -> TB -> RT cycle.
        var f = new Fixture();
        EntityId a = f.Colonists.Spawn("A", new CellCoord(10, 10, 0), 10);
        f.RaidTrigger.ThreatPerSecond = 10_000;

        var seen = new List<ulong>();
        var probe = new SeqProbe(seen);
        f.Manager.Register(probe, TimeAuthorityMode.RealTime, TickPhase.Presentation, 90);
        f.Manager.Register(probe, TimeAuthorityMode.TurnBased, TickPhase.Presentation, 90);

        f.PumpRealTime(1);
        f.TurnOrder.Participants.Add(a);
        f.Manager.TurnBased.SubmitActionFor = _ => true;
        f.Manager.TurnBased.ResolveAction = _ => { };
        f.PumpTurnBased(20);
        f.TurnOrder.EndBattle(true);
        f.Manager.RequestSwitch(TimeAuthorityMode.RealTime, new SwitchTransitionData(1, SwitchTriggerReason.Breach, [], []));
        f.RaidTrigger.Armed = false;
        f.PumpRealTime(5);

        bool contiguous = true;
        for (int i = 1; i < seen.Count; i++) if (seen[i] != seen[i - 1] + 1) contiguous = false;
        Check(seen.Count > 5 && contiguous,
              $"TickSequence is monotonic and gapless across RT->TB->RT ({seen.Count} ticks observed)");

        // Same inputs -> same state.
        static (ulong seq, int turn, string cells) Run()
        {
            var g = new Fixture();
            EntityId x = g.Colonists.Spawn("A", new CellCoord(8, 8, 0), 10);
            EntityId y = g.Colonists.Spawn("B", new CellCoord(8, 8, 0), 10);
            g.RaidTrigger.ThreatPerSecond = 10_000;
            g.PumpRealTime(1);
            g.TurnOrder.Participants.AddRange([x, y]);
            g.Manager.TurnBased.SubmitActionFor = _ => true;
            g.Manager.TurnBased.ResolveAction = _ => { };
            g.PumpTurnBased(24);
            g.TurnOrder.EndBattle(true);
            g.Manager.RequestSwitch(TimeAuthorityMode.RealTime, new SwitchTransitionData(1, SwitchTriggerReason.Breach, [], []));
            g.RaidTrigger.Armed = false;
            g.PumpRealTime(3);
            string cells = string.Join(";", g.Colonists.All.Select(c => $"{c.Id.Value}:{c.Cell}:{c.BattlesSurvived}"));
            return (g.Manager.TickSequence, g.Manager.TurnIndex, cells);
        }
        Check(Run() == Run(), "same inputs -> identical TickSequence, TurnIndex, and entity state");

        // Zero state conversion: the SAME store objects carry through the switch.
        var z = new Fixture();
        EntityId id = z.Colonists.Spawn("A", new CellCoord(7, 7, 0), 10);
        object storeBefore = z.Colonists;
        CellCoord cellBefore = z.Colonists.Get(id).Cell;
        long revBefore = z.Colonists.Revision;
        z.Manager.RequestSwitch(TimeAuthorityMode.TurnBased,
            new SwitchTransitionData(1, SwitchTriggerReason.Breach, [], [id]));
        Check(ReferenceEquals(storeBefore, z.Colonists) && z.Colonists.Get(id).Cell == cellBefore
              && z.Colonists.Revision == revBefore,
              "ZERO STATE CONVERSION: same store instance, same values, no writes during the swap");
    }

    private sealed class SeqProbe(List<ulong> sink) : ITickable
    {
        public void Tick(TimeContext ctx) => sink.Add(ctx.TickSequence);
    }

    private static void SerializationInvariant()
    {
        var f = new Fixture();
        EntityId a = f.Colonists.Spawn("A", new CellCoord(6, 6, 0), 10);
        f.RaidTrigger.Armed = false;
        f.PumpRealTime(4);

        TimeAuthoritySnapshot snap = f.Manager.Snapshot();
        ulong seqBefore = f.Manager.TickSequence;
        f.PumpRealTime(4);
        f.Manager.Restore(snap);
        Check(f.Manager.TickSequence == seqBefore && f.Manager.CurrentMode == TimeAuthorityMode.RealTime,
              "Snapshot/Restore preserves {Mode, TurnIndex, TickSequence}");

        f.Manager.RequestSwitch(TimeAuthorityMode.TurnBased,
            new SwitchTransitionData(1, SwitchTriggerReason.Breach, [], [a]));
        Throws<InvalidOperationException>(() => f.Manager.Snapshot(),
            "CD-9: saving inside a battle is refused — a TurnBased save is corrupt, not supported");
    }

    private static void WriterDiscipline()
    {
        var f = new Fixture();
        EntityId a = f.Colonists.Spawn("A", new CellCoord(6, 6, 0), 10);

        Throws<InvalidOperationException>(() => f.Colonists.SetCell(a, new CellCoord(7, 7, 0)),
            "entity write outside the mutation window throws");

        using (f.Manager.Window.Open())
            Throws<InvalidOperationException>(
                () => f.Colonists.ApplyDamage(a, 1, TimeAuthorityMode.TurnBased),
                "Combat's health writer is refused while the mode is RealTime (writer-per-authority)");

        using (f.Manager.Window.Open()) f.Colonists.ApplyDamage(a, 1, TimeAuthorityMode.RealTime);
        Check(f.Colonists.Get(a).Hp == 9, "Needs' health writer is accepted under RealTime");

        f.Manager.RequestSwitch(TimeAuthorityMode.TurnBased,
            new SwitchTransitionData(1, SwitchTriggerReason.Breach, [], [a]));
        using (f.Manager.Window.Open())
        {
            Throws<InvalidOperationException>(
                () => f.Colonists.ApplyDamage(a, 1, TimeAuthorityMode.RealTime),
                "Needs' health writer is refused under TurnBased (the arbitration is structural)");
            Throws<InvalidOperationException>(() => f.Colonists.Despawn(a),
                "no despawn inside an encounter (universal entity-layer rule)");
        }

        // Terrain: only Combat may write under TurnBased; the mutation window still applies.
        Throws<InvalidOperationException>(() => f.World.SetWall(new CellCoord(2, 2, 0), f.Dirt, 0),
            "terrain write outside the mutation window throws under TurnBased too");
    }
}
