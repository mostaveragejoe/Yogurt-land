using Hollowdeep.Core.Entities;
using Hollowdeep.Core.Path;
using Hollowdeep.Core.Primitives;
using Hollowdeep.Core.Rng;
using Hollowdeep.Core.Sim;
using Hollowdeep.Core.Terrain;

namespace Hollowdeep.Core.Combat;

/// <summary>
/// The TurnBased authority: XCOM-style activations over the SAME world the colony built
/// (ADR-0001: zero state conversion). Initiative is colonists-then-raiders, stable by
/// id; each actor gets 2 AP (§8). Every resolved order advances TickSequence exactly
/// once and runs inside the mutation window. RNG draws happen only inside resolution.
/// </summary>
public sealed class TurnBasedAuthority
{
    private readonly MutationGate _gate;
    private readonly TimeAuthorityManager _manager;
    private readonly ColonistStore _colonists;
    private readonly RaiderStore _raiders;
    private readonly DoorStore _doors;
    private readonly TerrainWorld _terrain;
    private readonly UnitOccupancyIndex _occupancy;
    private readonly Walkability _walk;
    private readonly Pathfinder _pathfinder;
    private readonly IColonistCombatWriter _colonistWriter;
    private readonly IRaiderCombatWriter _raiderWriter;
    private readonly IDoorsWriter _doorWriter;
    private readonly RngStream _rng;
    private readonly ScarLedger _scars;
    private readonly EncounterOutcomeInbox _inbox;

    public readonly EncounterState Encounter = new();

    private readonly List<CellCoord> _pathScratch = new();
    private readonly List<CombatEvent> _events = new();
    private readonly List<CellCoord> _lastMovePath = new();

    /// <summary>Checkpoint beat: fires when an activation fully resolves (AwaitingPresentation → NextActor).</summary>
    public event Action? ActivationResolved;

    /// <summary>Monotonic event log for presentation; index into Events with your own cursor.</summary>
    public IReadOnlyList<CombatEvent> Events => _events;
    public IReadOnlyList<CellCoord> LastMovePath => _lastMovePath;

    public TurnBasedAuthority(MutationGate gate, TimeAuthorityManager manager,
        ColonistStore colonists, RaiderStore raiders, DoorStore doors, TerrainWorld terrain,
        UnitOccupancyIndex occupancy, Walkability walk, Pathfinder pathfinder,
        IColonistCombatWriter colonistWriter, IRaiderCombatWriter raiderWriter, IDoorsWriter doorWriter,
        RngStream combatRng, ScarLedger scars, EncounterOutcomeInbox inbox)
    {
        _gate = gate; _manager = manager;
        _colonists = colonists; _raiders = raiders; _doors = doors; _terrain = terrain;
        _occupancy = occupancy; _walk = walk; _pathfinder = pathfinder;
        _colonistWriter = colonistWriter; _raiderWriter = raiderWriter; _doorWriter = doorWriter;
        _rng = combatRng; _scars = scars; _inbox = inbox;
    }

    public bool IsActive => Encounter.Phase == TbPhase.ActorTurn;

    public EntityId CurrentActor =>
        Encounter.Phase == TbPhase.ActorTurn && Encounter.CurrentIndex < Encounter.Initiative.Count
            ? Encounter.Initiative[Encounter.CurrentIndex]
            : EntityId.None;

    public bool IsPlayerTurn => CurrentActor.Kind == EntityKind.Colonist;
    public int RemainingAp(EntityId id) => Encounter.Ap.TryGetValue(id, out int ap) ? ap : 0;

    /// <summary>Called by the switch hooks inside the swap window (never directly by UI).</summary>
    public void BeginEncounter(SwitchTransitionData data)
    {
        _gate.AssertOpen();
        Encounter.Clear();
        Encounter.Phase = TbPhase.ActorTurn;
        Encounter.RaidIndex = data.RaidIndex;
        Encounter.Reason = data.Reason;
        Encounter.FocusCell = data.FocusCell;
        _events.Clear();

        // Initiative: colonists then raiders, stable by id (§5.12). Kind lives in the id's
        // top byte, so a plain sort by value yields exactly that order.
        for (int i = 0; i < _colonists.Count; i++)
            if (_colonists[i].IsActive) Encounter.Initiative.Add(_colonists[i].Id);
        for (int i = 0; i < _raiders.Count; i++)
            if (_raiders[i].IsAlive) Encounter.Initiative.Add(_raiders[i].Id);
        Encounter.Initiative.Sort();

        foreach (var id in Encounter.Initiative)
        {
            Encounter.Ap[id] = GameConfig.ActionPointsPerTurn;
            if (id.Kind == EntityKind.Colonist) _colonistWriter.SetActivity(id, ColonistActivity.Fighting);
            else _raiderWriter.SetState(id, RaiderState.Fighting);
        }
        Encounter.RaidersAtStart = _raiders.AliveCount();
        Encounter.RoundNumber = 1;
        _manager.TurnIndex = 1;
        Encounter.CurrentIndex = 0;
        SkipIncapacitatedActors();
    }

    // ---- player orders ----

    /// <summary>Reachable cells for the current actor within its remaining AP, for display.</summary>
    public void GetMoveRange(List<(CellCoord Cell, int Cost)> outCells)
    {
        outCells.Clear();
        var actor = CurrentActor;
        if (actor.IsNone) return;
        int budget = RemainingAp(actor) * GameConfig.MoveBudgetPerAp;
        var cell = UnitCell(actor);
        _pathfinder.FloodCosts(cell, MoveContext.TurnBased, actor, budget, outCells);
    }

    public bool TryMove(CellCoord target)
    {
        var actor = CurrentActor;
        if (actor.IsNone || !IsPlayerTurn) return false;
        return ResolveMove(actor, target);
    }

    public bool TryAttack(EntityId target)
    {
        var actor = CurrentActor;
        if (actor.IsNone || !IsPlayerTurn) return false;
        if (target.Kind != EntityKind.Raider) return false;
        return ResolveAttackUnit(actor, target);
    }

    public void EndActivation()
    {
        var actor = CurrentActor;
        if (actor.IsNone || !IsPlayerTurn) return;
        using (var _ = _gate.Open())
        {
            _manager.NextSequence();
            Encounter.Ap[actor] = 0;
        }
        FinishActivation();
    }

    /// <summary>
    /// Resolves ONE raider action (or a whole pass-through when the actor cannot act).
    /// The host calls this repeatedly, with presentation delays between calls.
    /// </summary>
    public void StepRaiderAi()
    {
        var actor = CurrentActor;
        if (actor.IsNone || IsPlayerTurn || Encounter.Phase != TbPhase.ActorTurn) return;
        if (!_raiders.TryGet(actor, out var raider) || !raider.IsAlive)
        {
            AdvanceActor();
            return;
        }

        bool acted = RaiderAct(actor, raider);
        if (!acted || RemainingAp(actor) <= 0)
        {
            if (Encounter.Phase == TbPhase.ActorTurn) FinishActivation();
        }
    }

    // ---- resolution (each order = one sequence step inside one window) ----

    private bool ResolveMove(EntityId actor, CellCoord target)
    {
        int ap = RemainingAp(actor);
        if (ap <= 0) return false;
        var from = UnitCell(actor);
        int budget = ap * GameConfig.MoveBudgetPerAp;
        if (!_pathfinder.FindPath(from, target, MoveContext.TurnBased, actor, GoalMode.Exact, _pathScratch, maxCost: budget))
            return false;
        if (_pathScratch.Count == 0) return false;

        int cost = PathCost(from, _pathScratch);
        int apCost = Math.Max(1, (cost + GameConfig.MoveBudgetPerAp - 1) / GameConfig.MoveBudgetPerAp);
        if (apCost > ap) return false;

        using (var _ = _gate.Open())
        {
            _manager.NextSequence();
            if (actor.Kind == EntityKind.Colonist) _colonistWriter.SetCell(actor, target);
            else _raiderWriter.SetCell(actor, target);
            Encounter.Ap[actor] = ap - apCost;
            _lastMovePath.Clear();
            _lastMovePath.AddRange(_pathScratch);
            Log(new CombatEvent { Kind = CombatEventKind.Move, Actor = actor, Cell = target, Value = apCost });
        }
        AfterAction(actor);
        return true;
    }

    private bool ResolveAttackUnit(EntityId actor, EntityId target)
    {
        int ap = RemainingAp(actor);
        if (ap <= 0) return false;
        var from = UnitCell(actor);
        var targetCell = UnitCell(target);
        bool ranged = IsRangedAttacker(actor);

        if (!ranged)
        {
            if (from.Z != targetCell.Z || CellCoord.Chebyshev(from, targetCell) != 1) return false;
        }
        else
        {
            if (from.Z != targetCell.Z) return false;
            if (CellCoord.Octile(from, targetCell) > GameConfig.RangedAttackRange * 10) return false;
            if (CellCoord.Chebyshev(from, targetCell) > 1 && !HasLineOfSight(from, targetCell)) return false;
        }

        using (var _ = _gate.Open())
        {
            _manager.NextSequence();
            int chance = GameConfig.BaseHitPercent;
            if (HasCover(targetCell, from)) chance -= GameConfig.CoverHitPenaltyPercent;
            bool hit = _rng.NextInt(100) < chance;
            int damage = 0;
            if (hit)
            {
                damage = AttackDamage(actor);
                ApplyUnitDamage(actor, target, damage);
            }
            Encounter.Ap[actor] = ap - 1;
            Log(new CombatEvent { Kind = CombatEventKind.Attack, Actor = actor, Target = target, Cell = targetCell, Value = damage, Hit = hit });
        }
        AfterAction(actor);
        return true;
    }

    /// <summary>Raider order: batter a wall or door cell (25 dmg per hit, §8).</summary>
    private bool ResolveAttackStructure(EntityId actor, CellCoord cell)
    {
        int ap = RemainingAp(actor);
        if (ap <= 0) return false;
        var from = UnitCell(actor);
        if (from.Z != cell.Z || CellCoord.Chebyshev(from, cell) != 1) return false;

        using (var _ = _gate.Open())
        {
            _manager.NextSequence();
            if (_doors.TryGetAt(cell, out var door) && door.Blocks)
            {
                bool broke = _doorWriter.Damage(door.Id, GameConfig.RaiderWallDamagePerHit);
                if (broke)
                {
                    Encounter.DoorsBroken++;
                    _scars.Record(ScarKind.DoorBroken, cell, actor, Encounter.RaidIndex);
                    Log(new CombatEvent { Kind = CombatEventKind.DoorBroken, Actor = actor, Cell = cell });
                }
                else
                {
                    _scars.Record(ScarKind.DoorDamaged, cell, actor, Encounter.RaidIndex, GameConfig.RaiderWallDamagePerHit);
                    Log(new CombatEvent { Kind = CombatEventKind.DoorHit, Actor = actor, Cell = cell, Value = GameConfig.RaiderWallDamagePerHit });
                }
            }
            else if (_terrain.Get(cell).HasWall)
            {
                bool destroyed = _terrain.DamageWall(cell, GameConfig.RaiderWallDamagePerHit);
                if (destroyed)
                {
                    Encounter.WallsDestroyed++;
                    _scars.Record(ScarKind.WallDestroyed, cell, actor, Encounter.RaidIndex);
                    Log(new CombatEvent { Kind = CombatEventKind.WallDestroyed, Actor = actor, Cell = cell });
                }
                else
                {
                    _scars.Record(ScarKind.WallDamaged, cell, actor, Encounter.RaidIndex, GameConfig.RaiderWallDamagePerHit);
                    Log(new CombatEvent { Kind = CombatEventKind.WallHit, Actor = actor, Cell = cell, Value = GameConfig.RaiderWallDamagePerHit });
                }
            }
            else
            {
                Encounter.Ap[actor] = ap - 1;
                return false; // nothing to hit; burn the AP to avoid stalls
            }
            Encounter.Ap[actor] = ap - 1;
        }
        AfterAction(actor);
        return true;
    }

    private void ApplyUnitDamage(EntityId actor, EntityId target, int damage)
    {
        if (target.Kind == EntityKind.Colonist)
        {
            var before = _colonists.Get(target);
            _colonistWriter.ApplyDamage(target, damage);
            var after = _colonists.Get(target);
            if (!before.IsDowned && after.IsDowned)
            {
                Encounter.ColonistsDownedThisBattle++;
                _scars.Record(ScarKind.ColonistDowned, after.Cell, actor, Encounter.RaidIndex);
                Log(new CombatEvent { Kind = CombatEventKind.Downed, Actor = actor, Target = target, Cell = after.Cell });
            }
            else if (!before.IsDead && after.IsDead)
            {
                Encounter.ColonistsDeadThisBattle++;
                _scars.Record(ScarKind.ColonistDead, after.Cell, actor, Encounter.RaidIndex);
                Log(new CombatEvent { Kind = CombatEventKind.Died, Actor = actor, Target = target, Cell = after.Cell });
            }
        }
        else
        {
            _raiderWriter.ApplyDamage(target, damage);
            var after = _raiders.Get(target);
            if (after.State == RaiderState.Dead)
            {
                Encounter.RaiderCasualties++;
                Log(new CombatEvent { Kind = CombatEventKind.Died, Actor = actor, Target = target, Cell = after.Cell });
            }
        }
    }

    // ---- raider AI ----

    private bool RaiderAct(EntityId actor, in RaiderData raider)
    {
        // Routed raiders flee (handled at battle level: rout ends the battle; stragglers
        // are reaped by reconcile). Alive-and-fighting raiders pursue.
        var from = raider.Cell;

        // Nearest active colonist, deterministic (octile, then id).
        EntityId targetId = EntityId.None;
        CellCoord targetCell = default;
        int bestDist = int.MaxValue;
        for (int i = 0; i < _colonists.Count; i++)
        {
            var c = _colonists[i];
            if (!c.IsActive) continue;
            int dist = CellCoord.Octile(from, c.Cell) + 100 * Math.Abs(from.Z - c.Cell.Z);
            if (dist < bestDist) { bestDist = dist; targetId = c.Id; targetCell = c.Cell; }
        }
        if (targetId.IsNone) return false; // no targets left; battle end check will fire

        bool ranged = IsRangedAttacker(actor);

        // In reach? Attack.
        if (from.Z == targetCell.Z && CellCoord.Chebyshev(from, targetCell) == 1)
            return ResolveAttackUnit(actor, targetId);
        if (ranged && from.Z == targetCell.Z
            && CellCoord.Octile(from, targetCell) <= GameConfig.RangedAttackRange * 10
            && HasLineOfSight(from, targetCell))
            return ResolveAttackUnit(actor, targetId);

        // Try to close: path adjacent to the target within budget; else partial approach.
        int budget = RemainingAp(actor) * GameConfig.MoveBudgetPerAp;
        if (_pathfinder.FindPath(from, targetCell, MoveContext.TurnBased, actor, GoalMode.Adjacent8, _pathScratch))
        {
            var dest = FurthestAffordable(from, _pathScratch, budget);
            if (dest != from) return ResolveMove(actor, dest);
        }

        // Blocked: find the structure sealing the way (breach plan) and batter it.
        if (_pathfinder.FindPath(from, targetCell, MoveContext.TurnBased, actor, GoalMode.Adjacent8, _pathScratch, breach: true))
        {
            foreach (var cell in _pathScratch)
            {
                bool structure = _terrain.Get(cell).HasWall || (_doors.TryGetAt(cell, out var d) && d.Blocks);
                if (!structure) continue;
                if (from.Z == cell.Z && CellCoord.Chebyshev(from, cell) == 1)
                    return ResolveAttackStructure(actor, cell);
                // Walk toward the structure first.
                if (_pathfinder.FindPath(from, cell, MoveContext.TurnBased, actor, GoalMode.Adjacent8, _pathScratch))
                {
                    var dest = FurthestAffordable(from, _pathScratch, budget);
                    if (dest != from) return ResolveMove(actor, dest);
                }
                break;
            }
        }
        return false; // cannot act this turn
    }

    private CellCoord FurthestAffordable(CellCoord from, List<CellCoord> path, int budget)
    {
        int cost = 0;
        var prev = from;
        var dest = from;
        foreach (var cell in path)
        {
            cost += StepCost(prev, cell);
            if (cost > budget) break;
            dest = cell;
            prev = cell;
        }
        return dest;
    }

    private int PathCost(CellCoord from, List<CellCoord> path)
    {
        int cost = 0;
        var prev = from;
        foreach (var cell in path)
        {
            cost += StepCost(prev, cell);
            prev = cell;
        }
        return cost;
    }

    private int StepCost(CellCoord a, CellCoord b)
        => a.Z != b.Z ? 10 : (a.X != b.X && a.Y != b.Y) ? 14 : 10;

    // ---- cover & LOS ----

    /// <summary>§8: an adjacent wall (or working door) between attacker and target grants −25% hit.</summary>
    public bool HasCover(CellCoord target, CellCoord attacker)
    {
        int dx = Math.Sign(attacker.X - target.X);
        int dy = Math.Sign(attacker.Y - target.Y);
        if (dx != 0 && StructureBlocksAt(target.Offset(dx, 0))) return true;
        if (dy != 0 && StructureBlocksAt(target.Offset(0, dy))) return true;
        return false;
    }

    private bool StructureBlocksAt(CellCoord c)
    {
        if (!_terrain.InBounds(c)) return false;
        if (_terrain.Get(c).HasWall) return true;
        return _doors.TryGetAt(c, out var door) && door.Blocks; // broken doors grant nothing
    }

    /// <summary>Same-layer Bresenham LOS; walls and working doors block, broken doors never do.</summary>
    public bool HasLineOfSight(CellCoord a, CellCoord b)
    {
        int x = a.X, y = a.Y;
        int dx = Math.Abs(b.X - a.X), dy = -Math.Abs(b.Y - a.Y);
        int sx = a.X < b.X ? 1 : -1, sy = a.Y < b.Y ? 1 : -1;
        int err = dx + dy;
        while (true)
        {
            if (!(x == a.X && y == a.Y) && !(x == b.X && y == b.Y))
                if (StructureBlocksAt(new CellCoord(x, y, a.Z)))
                    return false;
            if (x == b.X && y == b.Y) return true;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x += sx; }
            if (e2 <= dx) { err += dx; y += sy; }
        }
    }

    private bool IsRangedAttacker(EntityId id)
        => id.Kind == EntityKind.Raider && _raiders.TryGet(id, out var r) && r.Kind == RaiderKind.Ranged;

    private int AttackDamage(EntityId id)
    {
        if (id.Kind == EntityKind.Colonist) return _colonists.Get(id).MeleeDamage;
        var r = _raiders.Get(id);
        return r.Kind == RaiderKind.Ranged ? GameConfig.RaiderRangedDamage : GameConfig.RaiderMeleeDamage;
    }

    private CellCoord UnitCell(EntityId id)
        => id.Kind == EntityKind.Colonist ? _colonists.Get(id).Cell : _raiders.Get(id).Cell;

    // ---- turn flow ----

    private void AfterAction(EntityId actor)
    {
        if (CheckBattleEnd()) return;
        if (RemainingAp(actor) <= 0 && IsPlayerTurn) FinishActivation();
        // Raider AP exhaustion is handled by StepRaiderAi's caller loop.
    }

    private void FinishActivation()
    {
        if (Encounter.Phase != TbPhase.ActorTurn) return;
        Encounter.ActivationCounter++;
        AdvanceActor();
        // The checkpoint beat (AwaitingPresentation → NextActor): the snapshot captures
        // the world with the NEXT actor already selected, so resume never replays a turn.
        ActivationResolved?.Invoke();
    }

    private void AdvanceActor()
    {
        if (Encounter.Phase != TbPhase.ActorTurn) return;
        Encounter.CurrentIndex++;
        WrapRound();
        SkipIncapacitatedActors();
    }

    private void WrapRound()
    {
        if (Encounter.CurrentIndex < Encounter.Initiative.Count) return;
        Encounter.CurrentIndex = 0;
        Encounter.RoundNumber++;
        _manager.TurnIndex = Encounter.RoundNumber;
        foreach (var id in Encounter.Initiative)
            Encounter.Ap[id] = GameConfig.ActionPointsPerTurn;
    }

    private void SkipIncapacitatedActors()
    {
        int guard = 0;
        while (guard++ <= Encounter.Initiative.Count + 1)
        {
            var actor = CurrentActor;
            if (actor.IsNone) return;
            bool able = actor.Kind == EntityKind.Colonist
                ? _colonists.TryGet(actor, out var c) && c.IsActive
                : _raiders.TryGet(actor, out var r) && r.IsAlive;
            if (able) return;
            Encounter.CurrentIndex++;
            WrapRound();
        }
    }

    private bool CheckBattleEnd()
    {
        if (Encounter.Phase != TbPhase.ActorTurn) return false;

        int aliveRaiders = _raiders.AliveCount();
        if (aliveRaiders == 0)
        {
            EndBattle(EncounterResult.Victory);
            return true;
        }
        if (!Encounter.RoutTriggered && Encounter.RaidersAtStart > 0 &&
            Encounter.RaiderCasualties >= Encounter.RaidersAtStart * GameConfig.RoutCasualtyFraction)
        {
            // §8 rout: survivors break and flee; the battle ends now and reconcile reaps
            // the stragglers (their flight to the map edge is presentation).
            Encounter.RoutTriggered = true;
            using (var _ = _gate.Open())
                for (int i = 0; i < _raiders.Count; i++)
                    if (_raiders[i].IsAlive)
                        _raiderWriter.SetState(_raiders[i].Id, RaiderState.Routing);
            Log(new CombatEvent { Kind = CombatEventKind.Routed });
            EndBattle(EncounterResult.RaidersRouted);
            return true;
        }
        if (_colonists.AliveActiveCount() == 0)
        {
            EndBattle(EncounterResult.ColonyOverrun);
            return true;
        }
        return false;
    }

    private void EndBattle(EncounterResult result)
    {
        Encounter.Phase = TbPhase.BattleEnded;
        Log(new CombatEvent { Kind = CombatEventKind.BattleEnd, Value = (int)result });
        _inbox.Post(new EncounterOutcomeReport
        {
            RaidIndex = Encounter.RaidIndex,
            Result = result,
            RaidersKilled = Encounter.RaiderCasualties,
            RaidersRouted = Encounter.RoutTriggered ? _raiders.AliveCount() : 0,
            ColonistsDowned = Encounter.ColonistsDownedThisBattle,
            ColonistsDead = Encounter.ColonistsDeadThisBattle,
            WallsDestroyed = Encounter.WallsDestroyed,
            DoorsBroken = Encounter.DoorsBroken,
            RoundsFought = Encounter.RoundNumber,
        });
        _manager.CompleteEncounter();
    }

    private void Log(in CombatEvent e) => _events.Add(e);

    /// <summary>Debug/testing: auto-resolve the whole battle without presentation pauses.</summary>
    public void AutoResolve(int maxSteps = 10_000)
    {
        int steps = 0;
        while (Encounter.Phase == TbPhase.ActorTurn && steps++ < maxSteps)
        {
            if (IsPlayerTurn) AutoPlayColonist();
            else StepRaiderAi();
        }
    }

    /// <summary>Simple colonist auto-play used by tests and the smoke harness.</summary>
    public void AutoPlayColonist()
    {
        var actor = CurrentActor;
        if (actor.IsNone || !IsPlayerTurn) return;

        // Attack the nearest adjacent raider; else advance toward the nearest one.
        EntityId targetId = EntityId.None;
        CellCoord targetCell = default;
        int bestDist = int.MaxValue;
        var from = _colonists.Get(actor).Cell;
        for (int i = 0; i < _raiders.Count; i++)
        {
            var r = _raiders[i];
            if (!r.IsAlive) continue;
            int dist = CellCoord.Octile(from, r.Cell) + 100 * Math.Abs(from.Z - r.Cell.Z);
            if (dist < bestDist) { bestDist = dist; targetId = r.Id; targetCell = r.Cell; }
        }
        if (targetId.IsNone) { EndActivation(); return; }

        if (from.Z == targetCell.Z && CellCoord.Chebyshev(from, targetCell) == 1)
        {
            if (!TryAttack(targetId)) EndActivation();
            return;
        }
        int budget = RemainingAp(actor) * GameConfig.MoveBudgetPerAp;
        if (_pathfinder.FindPath(from, targetCell, MoveContext.TurnBased, actor, GoalMode.Adjacent8, _pathScratch))
        {
            var dest = FurthestAffordable(from, _pathScratch, budget);
            if (dest != from && TryMove(dest)) return;
        }
        EndActivation();
    }
}
