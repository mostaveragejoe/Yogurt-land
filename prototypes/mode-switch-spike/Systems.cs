// PROTOTYPE - NOT FOR PRODUCTION
// Question: do real systems behave correctly across the switch — freeze, normalization,
//   reconcile, zero orphans? (ADR-0001 worked-example table, made executable.)
// Date: 2026-07-26

namespace ModeSwitchSpike;

/// <summary>RealTime only. Integrates decay over DeltaSeconds — must never register TurnBased.</summary>
public sealed class NeedsSystem(ColonistStore colonists) : ITickable
{
    public double Food { get; private set; } = 100;
    public int TickCount { get; private set; }
    public double SimulatedSeconds { get; private set; }

    public void Tick(TimeContext ctx)
    {
        TickCount++;
        SimulatedSeconds += ctx.DeltaSeconds;
        Food -= 0.5 * ctx.DeltaSeconds;     // rate * dt — the pattern that breaks at Δ=0
        _ = colonists.Revision;
    }
}

public sealed record Job(int Id, CellCoord Target, EntityId AssignedTo);

/// <summary>RealTime only. Owns job state; its bus handler only MARKS (idempotent bookkeeping).</summary>
public sealed class JobAssignmentSystem : ITickable
{
    public List<Job> Jobs { get; } = [];
    public HashSet<CellCoord> CellsFlaggedByBus { get; } = [];
    public int TickCount { get; private set; }
    public int CancelledAtReconcile { get; private set; }

    public void Tick(TimeContext ctx) => TickCount++;

    /// <summary>Bus handler — marks only, never advances state (ADR-0001 dispatch rule).</summary>
    public void OnTerrainChanged(CellCoord cell) => CellsFlaggedByBus.Add(cell);

    /// <summary>Reconcile duty: cancel jobs whose target cell changed during the encounter.</summary>
    public int CancelOrphanedJobs(TerrainWorld world)
    {
        int before = Jobs.Count;
        Jobs.RemoveAll(j => CellsFlaggedByBus.Contains(j.Target) && world.GetCell(j.Target).WallTypeId == 0);
        CancelledAtReconcile += before - Jobs.Count;
        return before - Jobs.Count;
    }

    public int CancelJobsOwnedBy(EntityId dead)
    {
        int before = Jobs.Count;
        Jobs.RemoveAll(j => j.AssignedTo.Value == dead.Value);
        return before - Jobs.Count;
    }
}

/// <summary>Registers with BOTH authorities — colony paths in RealTime, reachability in TurnBased.</summary>
public sealed class PathfindingSystem : ITickable
{
    public List<List<CellCoord>> CachedPaths { get; } = [];
    public HashSet<CellCoord> DirtyCells { get; } = [];
    public int RealTimeTicks { get; private set; }
    public int TurnBasedTicks { get; private set; }
    public int InvalidatedAtReconcile { get; private set; }

    public void Tick(TimeContext ctx)
    {
        if (ctx.Mode == TimeAuthorityMode.RealTime) RealTimeTicks++;
        else TurnBasedTicks++;
    }

    public void OnTerrainChanged(CellCoord cell) => DirtyCells.Add(cell);

    public int InvalidateCrossingPaths()
    {
        int before = CachedPaths.Count;
        CachedPaths.RemoveAll(p => p.Any(DirtyCells.Contains));
        InvalidatedAtReconcile += before - CachedPaths.Count;
        return before - CachedPaths.Count;
    }
}

/// <summary>RealTime only. Accumulates threat; on trigger requests the switch and gates on the result.</summary>
public sealed class RaidTriggerSystem(TimeAuthorityManager manager, RaiderStore raiders, ColonistStore colonists) : ITickable
{
    public double Threat { get; private set; }
    public double ThreatPerSecond { get; set; } = 10;
    public SwitchResult? LastResult { get; private set; }
    public int SwitchRequests { get; private set; }
    public List<EntityId> SpawnedRaiders { get; } = [];
    public bool Armed { get; set; } = true;

    public void Tick(TimeContext ctx)
    {
        if (!Armed) return;
        Threat += ThreatPerSecond * ctx.DeltaSeconds;
        if (Threat < 100) return;
        Threat = 0;
        Armed = false;

        // Raid Trigger's placement duty: spawn raiders at exclusively-placed breach cells.
        var breach = new List<CellCoord>();
        SpawnedRaiders.Clear();
        for (int i = 0; i < 3; i++)
        {
            var cell = new CellCoord(1 + i, 1, 0);
            breach.Add(cell);
            SpawnedRaiders.Add(raiders.Spawn(cell, 8));
        }

        var participants = new List<EntityId>();
        foreach (ColonistRecord c in colonists.All) if (!c.IsDead) participants.Add(c.Id);
        participants.AddRange(SpawnedRaiders);

        SwitchRequests++;
        LastResult = manager.RequestSwitch(TimeAuthorityMode.TurnBased,
            new SwitchTransitionData(1, SwitchTriggerReason.Breach, breach, participants));
    }
}

/// <summary>
/// RealTime, Reaction phase. On an accepted-but-pending switch, DECIDES pre-switch placements
/// and submits them to Colonist Movement. Never writes positions itself (ADR-0003).
/// </summary>
public sealed class SquadPrepSystem(TimeAuthorityManager manager, ColonistStore colonists,
                                    UnitOccupancyIndex occupancy, TerrainWorld world,
                                    ColonistMovementSystem mover) : ITickable
{
    public int NormalizationRuns { get; private set; }
    public List<(EntityId id, CellCoord to)> LastDecision { get; } = [];

    public void Tick(TimeContext ctx)
    {
        if (!manager.SwitchPending || manager.PendingSwitchTarget != TimeAuthorityMode.TurnBased) return;
        NormalizationRuns++;
        LastDecision.Clear();

        // Deterministic nudge rule (ADR-0003): ascending EntityId, each move visible to the next;
        // lowest id keeps its cell; others take the nearest free cell scanning (Z,Y,X) at expanding radius.
        var claimed = new HashSet<CellCoord>();
        var living = colonists.All.Where(c => !c.IsDead).OrderBy(c => c.Id.Value).ToList();

        foreach (ColonistRecord c in living)
        {
            // First claimant of a cell keeps it — processing ascending by EntityId means that is
            // always the lowest id. Later units on the same cell must move. The decision set
            // (`claimed`) is what makes "each move visible to the next" true during a decide-only
            // pass: live occupancy still shows the pre-normalization world.
            if (claimed.Add(c.Cell)) continue;

            CellCoord target = FindFreeCell(c.Cell, claimed);
            claimed.Add(target);
            LastDecision.Add((c.Id, target));
            mover.SubmitPlacementOrder(c.Id, target);          // DECIDE only — movement executes
        }
    }

    private CellCoord FindFreeCell(CellCoord origin, HashSet<CellCoord> claimed)
    {
        for (int radius = 1; radius <= 8; radius++)
            for (int dz = -radius; dz <= radius; dz++)          // fixed ascending (Z, Y, X) order
                for (int dy = -radius; dy <= radius; dy++)
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Max(Math.Abs(dy), Math.Abs(dz))) != radius) continue;
                        var cand = new CellCoord(origin.X + dx, origin.Y + dy, origin.Z + dz);
                        if (!world.InBounds(cand) || !world.IsPassableTerrain(cand)) continue;
                        if (claimed.Contains(cand) || occupancy.IsOccupied(cand)) continue;
                        return cand;
                    }
        throw new InvalidOperationException($"No free cell within radius 8 of {origin} (normalization edge case).");
    }
}

/// <summary>RealTime. Simulation phase = job movement; Reaction phase = executes placement orders.</summary>
public sealed class ColonistMovementSystem(ColonistStore colonists) : ITickable
{
    private readonly List<(EntityId id, CellCoord to)> _orders = [];
    public int ExecutedPlacements { get; private set; }
    public int TickCount { get; private set; }

    public void SubmitPlacementOrder(EntityId id, CellCoord to) => _orders.Add((id, to));

    public void Tick(TimeContext ctx)
    {
        TickCount++;
        foreach ((EntityId id, CellCoord to) in _orders)
        {
            colonists.SetCell(id, to);        // the single position writer
            ExecutedPlacements++;
        }
        _orders.Clear();
    }
}

/// <summary>TurnBased. The encounter driver; writes the outcome report at battle end.</summary>
public sealed class CombatTurnOrderSystem(TimeAuthorityManager manager, ColonistStore colonists,
                                          RaiderStore raiders, EncounterOutcomeInbox inbox) : ITickable
{
    public int TickCount { get; private set; }
    public bool BattleEnded { get; private set; }
    public List<EntityId> Participants { get; } = [];

    public void Tick(TimeContext ctx)
    {
        TickCount++;
        if (BattleEnded) return;
        bool raidersLeft = raiders.All.Any(r => !r.IsDead && !r.Withdrew);
        if (!raidersLeft) EndBattle(true);
    }

    public void EndBattle(bool repelled)
    {
        if (BattleEnded) return;
        BattleEnded = true;
        var outcomes = new List<ParticipantOutcome>();
        foreach (EntityId id in Participants)
        {
            if (!colonists.Exists(id)) continue;
            ColonistRecord c = colonists.Get(id);
            outcomes.Add(new ParticipantOutcome(id, !c.IsDead, c.Cell));
        }
        inbox.Deposit(new EncounterOutcomeReport(manager.ActiveEncounterId, Participants, outcomes, repelled));
    }

    public void Reset() { BattleEnded = false; TickCount = 0; }
}

/// <summary>TurnBased. Damage writer; also the only legal TurnBased terrain writer.</summary>
public sealed class CombatTargetingSystem(ColonistStore colonists, RaiderStore raiders, TerrainWorld world) : ITickable
{
    public int TickCount { get; private set; }
    public void Tick(TimeContext ctx) => TickCount++;

    public void DamageColonist(EntityId id, int amount) =>
        colonists.ApplyDamage(id, amount, TimeAuthorityMode.TurnBased);

    public void KillRaider(EntityId id) => raiders.Kill(id);

    /// <summary>Applying Material-Tier Destructibility results — legal only under TurnBased.</summary>
    public DamageResult BreachWall(CellCoord c, int amount) => world.ApplyWallDamage(c, amount);
}

/// <summary>Reconcile consumer: BattlesSurvived++ for survivors (CD-4). MVP writer of that field.</summary>
public sealed class IdentityBookkeeping(ColonistStore colonists, EncounterOutcomeInbox inbox) : IReconcileConsumer
{
    public int ReportsConsumed { get; private set; }
    public int SurvivorsCredited { get; private set; }
    public EncounterOutcomeReport? LastReport { get; private set; }

    public void Reconcile()
    {
        EncounterOutcomeReport? report = inbox.Drain();
        if (report is null) return;
        ReportsConsumed++;
        LastReport = report;
        foreach (ParticipantOutcome o in report.Outcomes)
            if (o.Survived && colonists.Exists(o.Id))
            { colonists.IncrementBattlesSurvived(o.Id); SurvivorsCredited++; }
    }
}

/// <summary>Reconcile consumer: named death notification with location (CD-4).</summary>
public sealed class NotificationsSystem : IReconcileConsumer
{
    public List<string> Messages { get; } = [];
    public Func<EncounterOutcomeReport?>? PeekReport;

    public void Reconcile()
    {
        EncounterOutcomeReport? r = PeekReport?.Invoke();
        if (r is null) return;
        foreach (ParticipantOutcome o in r.Outcomes)
            if (!o.Survived) Messages.Add($"Colonist {o.Id.Value} died at {o.DeathCell}");
    }
}

/// <summary>
/// Reconcile consumer: the housekeeping ADR-0001 step 4 names — release dead colonists'
/// reservations, cancel orphaned jobs, invalidate crossing paths, reap the dead.
/// </summary>
public sealed class ReconcileHousekeeping(ColonistStore colonists, RaiderStore raiders,
                                          StackReservationTable reservations, JobAssignmentSystem jobs,
                                          PathfindingSystem pathfinding, TerrainWorld world) : IReconcileConsumer
{
    public int ReservationsReleased { get; private set; }
    public int JobsCancelled { get; private set; }
    public int PathsInvalidated { get; private set; }
    public int ColonistsReaped { get; private set; }
    public int RaidersReaped { get; private set; }

    public void Reconcile()
    {
        JobsCancelled += jobs.CancelOrphanedJobs(world);
        PathsInvalidated += pathfinding.InvalidateCrossingPaths();

        var deadColonists = colonists.All.Where(c => c.IsDead).Select(c => c.Id).ToList();
        foreach (EntityId id in deadColonists)
        {
            ReservationsReleased += reservations.ReleaseAllHeldBy(id);
            jobs.CancelJobsOwnedBy(id);
            colonists.Despawn(id);
            ColonistsReaped++;
        }

        // SPIKE FINDING: ADR-0003's Raider Lifecycle row says reconcile despawns
        // "dead/withdrawn" raiders — which silently leaks any raider still alive and not
        // withdrawn when the battle ends (e.g. a scripted/debug end, or a future
        // objective-complete end condition). Raiders are ENCOUNTER-SCOPED: no raider may
        // outlive the encounter, so the correct rule is reap ALL of them.
        var goneRaiders = raiders.All.Select(r => r.Id).ToList();
        foreach (EntityId id in goneRaiders) { raiders.Despawn(id); RaidersReaped++; }
    }
}
