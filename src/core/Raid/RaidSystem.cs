using Hollowdeep.Core.Colony;
using Hollowdeep.Core.Combat;
using Hollowdeep.Core.Entities;
using Hollowdeep.Core.Path;
using Hollowdeep.Core.Primitives;
using Hollowdeep.Core.Rng;
using Hollowdeep.Core.Sim;
using Hollowdeep.Core.Terrain;

namespace Hollowdeep.Core.Raid;

/// <summary>
/// Timed escalating raids (§5.11). Raiders spawn at the map edge, approach the nearest
/// colonist (falling back to the hearth), and batter walls/doors when sealed out —
/// choosing targets via breach-cost pathing, so weak walls invite them (Pillar 1).
/// A raider closing within the breach radius of a colonist or the hearth requests the
/// mode switch. The warning window before contact is the squad-prep phase (§5.13).
/// </summary>
public sealed class RaidSystem : ITickable
{
    private sealed class RaiderMover
    {
        public readonly List<CellCoord> Path = new();
        public int Cursor;
        public ulong PlannedRevision;
        public bool HasPath;
        public void Clear() { Path.Clear(); Cursor = 0; HasPath = false; }
    }

    private readonly TerrainWorld _terrain;
    private readonly ColonistStore _colonists;
    private readonly RaiderStore _raiders;
    private readonly DoorStore _doors;
    private readonly ItemStore _items;
    private readonly Walkability _walk;
    private readonly Pathfinder _pathfinder;
    private readonly ColonyStats _stats;
    private readonly StockpileSystem _stockpiles;
    private readonly IRaiderRtWriter _writer;
    private readonly IDoorsWriter _doorWriter;
    private readonly RngStream _rng;
    private readonly ScarLedger _scars;
    private readonly TimeAuthorityManager _manager;

    private readonly Dictionary<EntityId, RaiderMover> _movers = new();
    private readonly List<CellCoord> _pathScratch = new();

    // Raid schedule state (serialized).
    private int _raidIndex;
    private double _nextRaidAt = -1;   // world-seconds; -1 = unscheduled
    private bool _warningActive;
    private double _contactAt;

    public Revision Revision;

    public int RaidIndex => _raidIndex;
    public bool WarningActive => _warningActive;
    public float WarningSecondsLeft => _warningActive ? (float)Math.Max(0, _contactAt - _stats.WorldSeconds) : 0f;

    public RaidSystem(TerrainWorld terrain, ColonistStore colonists, RaiderStore raiders, DoorStore doors,
        ItemStore items, Walkability walk, Pathfinder pathfinder, ColonyStats stats, StockpileSystem stockpiles,
        IRaiderRtWriter writer, IDoorsWriter doorWriter, RngStream raidsRng, ScarLedger scars,
        TimeAuthorityManager manager)
    {
        _terrain = terrain; _colonists = colonists; _raiders = raiders; _doors = doors; _items = items;
        _walk = walk; _pathfinder = pathfinder; _stats = stats; _stockpiles = stockpiles;
        _writer = writer; _doorWriter = doorWriter; _rng = raidsRng; _scars = scars; _manager = manager;
    }

    public void Tick(in TimeContext ctx)
    {
        if (ctx.Mode != SimMode.RealTime) return;

        ScheduleAndSpawn();
        DriveRaiders(ctx.Dt);
        CheckBreachTrigger();
    }

    /// <summary>Debug console: force a raid warning right now.</summary>
    public void DebugTriggerRaid()
    {
        _nextRaidAt = _stats.WorldSeconds;
        _warningActive = false;
    }

    private void ScheduleAndSpawn()
    {
        if (_nextRaidAt < 0)
        {
            _nextRaidAt = _raidIndex == 0
                ? GameConfig.FirstRaidAtSeconds
                : _stats.WorldSeconds + _rng.Range((int)GameConfig.RaidIntervalMinSeconds, (int)GameConfig.RaidIntervalMaxSeconds + 1);
            Revision.Bump();
            return;
        }

        if (!_warningActive && _stats.WorldSeconds >= _nextRaidAt)
        {
            _warningActive = true;
            _contactAt = _stats.WorldSeconds + GameConfig.RaidWarningSeconds;
            Revision.Bump();
            return;
        }

        if (_warningActive && _stats.WorldSeconds >= _contactAt)
        {
            _warningActive = false;
            _nextRaidAt = -1; // next raid schedules after this one resolves
            SpawnWave();
            Revision.Bump();
        }
    }

    private void SpawnWave()
    {
        _raidIndex++;
        int wealth = _stats.Wealth(_stockpiles.StoredItemCount());
        int size = Math.Min(GameConfig.RaidSizeCap,
            GameConfig.RaidSizeBase + _raidIndex + wealth / GameConfig.WealthRaidThreshold);

        // Pick a spawn edge point on the surface with an open cell, deterministically.
        var spawn = FindEdgeSpawn();
        int rangedBudget = _raidIndex >= 2 ? Math.Max(1, size / 4) : 0; // DECISIONS #16

        for (int i = 0; i < size; i++)
        {
            var kind = rangedBudget-- > 0 ? RaiderKind.Ranged : RaiderKind.Melee;
            var cell = NearbyOpenCell(spawn, i);
            _writer.Spawn(kind, cell, _raidIndex);
        }
        Revision.Bump();
    }

    private CellCoord FindEdgeSpawn()
    {
        int z = GameConfig.SurfaceZ;
        for (int attempt = 0; attempt < 64; attempt++)
        {
            int side = _rng.NextInt(4);
            int t = _rng.NextInt(_terrain.Width);
            var c = side switch
            {
                0 => new CellCoord(t, 0, z),
                1 => new CellCoord(t, _terrain.Height - 1, z),
                2 => new CellCoord(0, t, z),
                _ => new CellCoord(_terrain.Width - 1, t, z),
            };
            if (_walk.TerrainWalkable(c)) return c;
        }
        return new CellCoord(0, 0, z); // degenerate map; still deterministic
    }

    private CellCoord NearbyOpenCell(CellCoord seed, int salt)
    {
        for (int r = 0; r < 6; r++)
        for (int dy = -r; dy <= r; dy++)
        for (int dx = -r; dx <= r; dx++)
        {
            var c = new CellCoord(
                Math.Clamp(seed.X + dx + salt % 3 - 1, 0, _terrain.Width - 1),
                Math.Clamp(seed.Y + dy, 0, _terrain.Height - 1),
                seed.Z);
            if (_walk.TerrainWalkable(c)) return c;
        }
        return seed;
    }

    // ---- raider RT behavior ----

    private void DriveRaiders(float dt)
    {
        for (int i = 0; i < _raiders.Count; i++)
        {
            var r = _raiders[i];
            if (!r.IsAlive || r.State == RaiderState.Fighting) continue;
            DriveRaider(r, dt);
        }
    }

    private void DriveRaider(in RaiderData raider, float dt)
    {
        var id = raider.Id;

        if (raider.State == RaiderState.AttackingWall)
        {
            var target = raider.BreachTarget;
            bool structureStands = _terrain.Get(target).HasWall ||
                                   (_doors.TryGetAt(target, out var door) && door.Blocks);
            if (!structureStands || CellCoord.Chebyshev(raider.Cell, target) != 1)
            {
                _writer.SetState(id, RaiderState.Approaching);
                GetMover(id).Clear();
                return;
            }
            float timer = raider.ActionTimer + dt;
            if (timer >= GameConfig.RaiderWallHitSeconds)
            {
                timer -= GameConfig.RaiderWallHitSeconds;
                HitStructure(id, target);
            }
            _writer.SetActionTimer(id, timer);
            return;
        }

        // Approaching: nearest active colonist, else the hearth.
        var goal = _stats.HearthCell;
        int bestDist = int.MaxValue;
        for (int i = 0; i < _colonists.Count; i++)
        {
            var c = _colonists[i];
            if (!c.IsActive) continue;
            int dist = CellCoord.Octile(raider.Cell, c.Cell) + 100 * Math.Abs(raider.Cell.Z - c.Cell.Z);
            if (dist < bestDist) { bestDist = dist; goal = c.Cell; }
        }

        var move = MoveToward(id, goal, dt);
        if (move == MoveResult.Blocked)
        {
            // Sealed out: plan a breach and batter the first structure on it.
            if (_pathfinder.FindPath(raider.Cell, goal, MoveContext.RaiderRealTime, id, GoalMode.Adjacent8, _pathScratch, breach: true))
            {
                foreach (var cell in _pathScratch)
                {
                    bool structure = _terrain.Get(cell).HasWall || (_doors.TryGetAt(cell, out var d) && d.Blocks);
                    if (!structure) continue;
                    _writer.SetBreachTarget(id, cell);
                    if (CellCoord.Chebyshev(raider.Cell, cell) == 1 && raider.Cell.Z == cell.Z)
                    {
                        _writer.SetState(id, RaiderState.AttackingWall);
                        _writer.SetActionTimer(id, 0f);
                    }
                    else
                    {
                        // Walk to the structure face first.
                        var mover = GetMover(id);
                        if (!mover.HasPath &&
                            _pathfinder.FindPath(raider.Cell, cell, MoveContext.RaiderRealTime, id, GoalMode.Adjacent8, mover.Path))
                        {
                            mover.HasPath = mover.Path.Count > 0;
                            mover.Cursor = 0;
                            mover.PlannedRevision = _terrain.Revision.Value;
                            if (!mover.HasPath)
                            {
                                _writer.SetState(id, RaiderState.AttackingWall);
                                _writer.SetActionTimer(id, 0f);
                            }
                        }
                    }
                    return;
                }
            }
            // No breach plan either (fully sealed map edge pocket): idle in place.
        }
    }

    private void HitStructure(EntityId raider, CellCoord cell)
    {
        if (_doors.TryGetAt(cell, out var door) && door.Blocks)
        {
            bool broke = _doorWriter.Damage(door.Id, GameConfig.RaiderWallDamagePerHit);
            _scars.Record(broke ? ScarKind.DoorBroken : ScarKind.DoorDamaged, cell, raider,
                _raidIndex, GameConfig.RaiderWallDamagePerHit);
            if (broke) _scars.Record(ScarKind.BreachEntry, cell, raider, _raidIndex);
        }
        else if (_terrain.Get(cell).HasWall)
        {
            bool destroyed = _terrain.DamageWall(cell, GameConfig.RaiderWallDamagePerHit);
            _scars.Record(destroyed ? ScarKind.WallDestroyed : ScarKind.WallDamaged, cell, raider,
                _raidIndex, GameConfig.RaiderWallDamagePerHit);
            if (destroyed) _scars.Record(ScarKind.BreachEntry, cell, raider, _raidIndex);
        }
    }

    private enum MoveResult { Moving, Arrived, Blocked }

    private RaiderMover GetMover(EntityId id)
    {
        if (!_movers.TryGetValue(id, out var m)) { m = new RaiderMover(); _movers[id] = m; }
        return m;
    }

    private MoveResult MoveToward(EntityId id, CellCoord goal, float dt)
    {
        var r = _raiders.Get(id);
        var mover = GetMover(id);

        if (r.NextCell != r.Cell)
        {
            if (StepAdvance(id, r.Cell, r.NextCell, r.MoveProgress, dt)) return MoveResult.Moving;
            r = _raiders.Get(id);
        }

        if (r.Cell.Z == goal.Z && CellCoord.Chebyshev(r.Cell, goal) <= 1)
        { mover.Clear(); return MoveResult.Arrived; }

        if (mover.HasPath && mover.PlannedRevision != _terrain.Revision.Value)
        {
            if (RemainingPathValid(mover, r.Cell)) mover.PlannedRevision = _terrain.Revision.Value;
            else mover.Clear();
        }

        if (!mover.HasPath || mover.Cursor >= mover.Path.Count)
        {
            mover.Clear();
            if (!_pathfinder.FindPath(r.Cell, goal, MoveContext.RaiderRealTime, id, GoalMode.Adjacent8, mover.Path))
                return MoveResult.Blocked;
            if (mover.Path.Count == 0) return MoveResult.Arrived;
            mover.HasPath = true;
            mover.Cursor = 0;
            mover.PlannedRevision = _terrain.Revision.Value;
        }

        var next = mover.Path[mover.Cursor];
        if (!_walk.CanEnter(next, MoveContext.RaiderRealTime, id)) { mover.Clear(); return MoveResult.Moving; }
        _writer.BeginStep(id, next);
        mover.Cursor++;
        StepAdvance(id, r.Cell, next, 0f, dt);
        return MoveResult.Moving;
    }

    private bool StepAdvance(EntityId id, CellCoord from, CellCoord to, float progress01, float dt)
    {
        int cost = from.Z != to.Z ? 10 : (from.X != to.X && from.Y != to.Y) ? 14 : 10;
        float stepSeconds = cost / 10f / GameConfig.RaiderMoveCellsPerSecond;
        float elapsed = progress01 * stepSeconds + dt;
        if (elapsed >= stepSeconds)
        {
            _writer.CompleteStep(id);
            return false;
        }
        _writer.SetMoveProgress(id, elapsed / stepSeconds);
        return true;
    }

    private bool RemainingPathValid(RaiderMover mover, CellCoord current)
    {
        var prev = current;
        for (int i = mover.Cursor; i < mover.Path.Count; i++)
        {
            var cell = mover.Path[i];
            if (!_walk.CanEnter(cell, MoveContext.RaiderRealTime, EntityId.None)) return false;
            int dx = cell.X - prev.X, dy = cell.Y - prev.Y;
            if (cell.Z == prev.Z && dx != 0 && dy != 0 && !_walk.DiagonalLegal(prev, dx, dy, MoveContext.RaiderRealTime))
                return false;
            prev = cell;
        }
        return true;
    }

    private void CheckBreachTrigger()
    {
        for (int i = 0; i < _raiders.Count; i++)
        {
            var r = _raiders[i];
            if (!r.IsAlive || r.State == RaiderState.Fighting) continue;
            if (WithinBreachRange(r.Cell, _stats.HearthCell))
            {
                RequestBattle(r.Cell, "Raiders reached the hearth");
                return;
            }
            for (int j = 0; j < _colonists.Count; j++)
            {
                var c = _colonists[j];
                if (!c.IsActive) continue;
                if (WithinBreachRange(r.Cell, c.Cell))
                {
                    RequestBattle(r.Cell, $"Raiders closed on {c.Name}");
                    return;
                }
            }
        }
    }

    private static bool WithinBreachRange(CellCoord a, CellCoord b)
        => CellCoord.Chebyshev(a, b) <= GameConfig.BreachTriggerRadius;

    /// <summary>Player-triggered engagement (UI/debug), also used by the breach check.</summary>
    public void RequestBattle(CellCoord focus, string reason)
    {
        if (_raiders.AliveCount() == 0) return;
        _manager.RequestSwitch(new SwitchTransitionData { RaidIndex = _raidIndex, Reason = reason, FocusCell = focus });
    }

    /// <summary>Reconcile cleanup: encounter over, raiders reaped, movers dropped.</summary>
    public void OnReconcile()
    {
        _movers.Clear();
    }

    // ---- serialization ----

    public void WriteTo(BinaryWriter w)
    {
        w.Write(_raidIndex);
        w.Write(_nextRaidAt);
        w.Write(_warningActive);
        w.Write(_contactAt);

        var moverIds = _movers.Keys.Where(k => _movers[k].HasPath && _raiders.Contains(k)).OrderBy(k => k.Value).ToList();
        w.Write(moverIds.Count);
        foreach (var id in moverIds)
        {
            var m = _movers[id];
            w.Write(id.Value);
            w.Write(m.Cursor);
            w.Write(m.PlannedRevision);
            w.Write(m.Path.Count);
            foreach (var c in m.Path) { w.Write(c.X); w.Write(c.Y); w.Write(c.Z); }
        }
    }

    public void ReadFrom(BinaryReader r)
    {
        _movers.Clear();
        _raidIndex = r.ReadInt32();
        _nextRaidAt = r.ReadDouble();
        _warningActive = r.ReadBoolean();
        _contactAt = r.ReadDouble();
        int n = r.ReadInt32();
        for (int i = 0; i < n; i++)
        {
            var id = new EntityId(r.ReadInt64());
            var m = new RaiderMover { Cursor = r.ReadInt32(), PlannedRevision = r.ReadUInt64(), HasPath = true };
            int cells = r.ReadInt32();
            for (int j = 0; j < cells; j++)
                m.Path.Add(new CellCoord(r.ReadInt32(), r.ReadInt32(), r.ReadInt32()));
            _movers[id] = m;
        }
        Revision.Bump();
    }
}
