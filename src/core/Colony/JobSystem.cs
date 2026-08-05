using Hollowdeep.Core.Entities;
using Hollowdeep.Core.Path;
using Hollowdeep.Core.Primitives;
using Hollowdeep.Core.Sim;
using Hollowdeep.Core.Terrain;

namespace Hollowdeep.Core.Colony;

public enum JobType : byte
{
    Dig = 0,
    DigStairDown = 1,
    Build = 2,
    Haul = 3,
    Repair = 4,
    Eat = 5,
    Sleep = 6,
    Rally = 7,
}

public enum JobState : byte
{
    Open = 0,
    Active = 1,
    Done = 2,
    Cancelled = 3,
}

public sealed class Job
{
    public long Id;
    public JobType Type;
    public JobState State;
    public CellCoord Target;
    public int Priority;          // lower = more urgent
    public EntityId Assignee;
    public float Progress;        // seconds of work applied
    public BlueprintKind BuildKind;
    public MaterialId Material;
    public EntityId Item;         // haul stack / fetch reservation / food reservation
    public CellCoord DropCell;
    public EntityId MergeTarget;
    public byte Phase;            // 0 travel/fetch, 1 deliver, 2 work

    public void WriteTo(BinaryWriter w)
    {
        w.Write(Id); w.Write((byte)Type); w.Write((byte)State);
        w.Write(Target.X); w.Write(Target.Y); w.Write(Target.Z);
        w.Write(Priority); w.Write(Assignee.Value); w.Write(Progress);
        w.Write((byte)BuildKind); w.Write((byte)Material); w.Write(Item.Value);
        w.Write(DropCell.X); w.Write(DropCell.Y); w.Write(DropCell.Z);
        w.Write(MergeTarget.Value); w.Write(Phase);
    }

    public static Job ReadFrom(BinaryReader r) => new()
    {
        Id = r.ReadInt64(),
        Type = (JobType)r.ReadByte(),
        State = (JobState)r.ReadByte(),
        Target = new CellCoord(r.ReadInt32(), r.ReadInt32(), r.ReadInt32()),
        Priority = r.ReadInt32(),
        Assignee = new EntityId(r.ReadInt64()),
        Progress = r.ReadSingle(),
        BuildKind = (BlueprintKind)r.ReadByte(),
        Material = (MaterialId)r.ReadByte(),
        Item = new EntityId(r.ReadInt64()),
        DropCell = new CellCoord(r.ReadInt32(), r.ReadInt32(), r.ReadInt32()),
        MergeTarget = new EntityId(r.ReadInt64()),
        Phase = r.ReadByte(),
    };
}

/// <summary>
/// Autonomous colonist work (Pillar 4): designations become jobs, colonists pick jobs by
/// priority/distance, needs interrupt below thresholds. Movement is fixed-dt stepping
/// along A* paths with Revision-polled revalidation (§4d): off-route digs revalidate,
/// on-route changes recompute once, a sealed goal cancels cleanly.
/// </summary>
public sealed class JobSystem : ITickable
{
    private sealed class MoverState
    {
        public readonly List<CellCoord> Path = new();
        public int Cursor;
        public ulong PlannedRevision;
        public bool HasPath;
        public void Clear() { Path.Clear(); Cursor = 0; HasPath = false; }
    }

    private readonly ColonistStore _colonists;
    private readonly ItemStore _items;
    private readonly DoorStore _doors;
    private readonly TerrainWorld _terrain;
    private readonly UnitOccupancyIndex _occupancy;
    private readonly Walkability _walk;
    private readonly Pathfinder _pathfinder;
    private readonly RegionIndex _regions;
    private readonly DesignationSystem _designations;
    private readonly StockpileSystem _stockpiles;
    private readonly ColonyStats _stats;
    private readonly IJobsWriter _writer;
    private readonly INeedsWriter _needsWriter;
    private readonly IItemsWriter _itemsWriter;
    private readonly IDoorsWriter _doorsWriter;
    private readonly IPropsWriter _propsWriter;

    private readonly Dictionary<long, Job> _jobs = new();
    private readonly List<long> _jobOrder = new();
    private long _nextJobId;
    private readonly Dictionary<EntityId, MoverState> _movers = new();
    private readonly HashSet<CellCoord> _digJobCells = new();
    private readonly HashSet<CellCoord> _buildJobCells = new();
    private readonly HashSet<CellCoord> _repairJobCells = new();
    private readonly HashSet<EntityId> _haulJobItems = new();
    private readonly List<long> _finishedScratch = new();
    private readonly List<CellCoord> _pathScratch = new();

    public Revision Revision; // job list UI polls this

    public JobSystem(ColonistStore colonists, ItemStore items, DoorStore doors, TerrainWorld terrain,
        UnitOccupancyIndex occupancy, Walkability walk, Pathfinder pathfinder, RegionIndex regions,
        DesignationSystem designations, StockpileSystem stockpiles, ColonyStats stats,
        IJobsWriter writer, INeedsWriter needsWriter, IItemsWriter itemsWriter, IDoorsWriter doorsWriter,
        IPropsWriter propsWriter)
    {
        _colonists = colonists; _items = items; _doors = doors; _terrain = terrain;
        _occupancy = occupancy; _walk = walk; _pathfinder = pathfinder; _regions = regions;
        _designations = designations; _stockpiles = stockpiles; _stats = stats;
        _writer = writer; _needsWriter = needsWriter; _itemsWriter = itemsWriter; _doorsWriter = doorsWriter;
        _propsWriter = propsWriter;
    }

    public IReadOnlyList<long> JobOrder => _jobOrder;
    public Job? GetJob(long id) => _jobs.TryGetValue(id, out var j) ? j : null;
    public int OpenJobCount()
    {
        int n = 0;
        foreach (var id in _jobOrder) if (_jobs[id].State == JobState.Open) n++;
        return n;
    }

    /// <summary>Squad-prep rally: direct order, top priority, interrupts current work (§5.13).</summary>
    public void IssueRallyOrder(EntityId colonist, CellCoord cell)
    {
        if (!_colonists.TryGet(colonist, out var c) || !c.IsActive) return;
        var job = NewJob(JobType.Rally, cell, priority: 0);
        job.State = JobState.Open;
        // The per-colonist needs/assignment pass picks it up next tick; pre-empt here.
        PendingRally[colonist] = job.Id;
        Revision.Bump();
    }

    internal readonly Dictionary<EntityId, long> PendingRally = new();

    public void Tick(in TimeContext ctx)
    {
        if (ctx.Mode != SimMode.RealTime) return;
        float dt = ctx.Dt;
        _stats.WorldSeconds += dt;

        SyncDesignationJobs();
        GenerateHaulJobs();
        ReleaseJobsOfInactiveColonists();

        for (int i = 0; i < _colonists.Count; i++)
        {
            var c = _colonists[i];
            if (!c.IsActive) continue;
            HandleColonist(c.Id, dt);
        }

        SweepFinishedJobs();
    }

    // ---- job bookkeeping ----

    private Job NewJob(JobType type, CellCoord target, int priority)
    {
        var job = new Job { Id = ++_nextJobId, Type = type, State = JobState.Open, Target = target, Priority = priority };
        _jobs[job.Id] = job;
        _jobOrder.Add(job.Id);
        Revision.Bump();
        return job;
    }

    private void SyncDesignationJobs()
    {
        foreach (var d in _designations.Digs)
            if (_digJobCells.Add(d.Cell))
            {
                var job = NewJob(d.StairDown ? JobType.DigStairDown : JobType.Dig, d.Cell, d.Priority);
                job.Material = d.StairDown
                    ? _terrain.Get(d.Cell.Down).WallMaterial
                    : _terrain.Get(d.Cell).WallMaterial;
            }
        foreach (var b in _designations.Builds)
            if (_buildJobCells.Add(b.Cell))
            {
                var job = NewJob(JobType.Build, b.Cell, b.Priority);
                job.BuildKind = b.Kind;
                job.Material = b.Material;
            }
        foreach (var rp in _designations.Repairs)
            if (_repairJobCells.Add(rp.Cell))
            {
                var job = NewJob(JobType.Repair, rp.Cell, rp.Priority);
                var cell = _terrain.Get(rp.Cell);
                job.Material = cell.HasWall ? cell.WallMaterial
                    : _doors.TryGetAt(rp.Cell, out var door) ? door.Material
                    : MaterialId.Dirt;
            }

        // Player-cancelled designations kill their jobs.
        foreach (var id in _jobOrder)
        {
            var job = _jobs[id];
            if (job.State is JobState.Done or JobState.Cancelled) continue;
            bool orphaned = job.Type switch
            {
                JobType.Dig or JobType.DigStairDown => !_designations.HasDig(job.Target),
                JobType.Build => !_designations.HasBuild(job.Target),
                JobType.Repair => !_designations.HasRepair(job.Target),
                _ => false,
            };
            if (orphaned) CancelJob(job);
        }
    }

    private void GenerateHaulJobs()
    {
        for (int i = 0; i < _items.Count; i++)
        {
            var it = _items[i];
            if (!it.IsLoose || !it.ReservedBy.IsNone) continue;
            if (_stockpiles.IsStockpile(it.Cell)) continue;
            if (_haulJobItems.Contains(it.Id)) continue;
            if (_stockpiles.ZoneCells.Count == 0) continue;
            var job = NewJob(JobType.Haul, it.Cell, priority: 6);
            job.Item = it.Id;
            _haulJobItems.Add(it.Id);
        }
    }

    private void ReleaseJobsOfInactiveColonists()
    {
        foreach (var id in _jobOrder)
        {
            var job = _jobs[id];
            if (job.State != JobState.Active) continue;
            if (!_colonists.TryGet(job.Assignee, out var c) || !c.IsActive)
                ReleaseJob(job, dropCarriedAt: c.Cell);
        }
    }

    private void ReleaseJob(Job job, CellCoord dropCarriedAt)
    {
        var assignee = job.Assignee;
        if (!assignee.IsNone)
        {
            if (!job.Item.IsNone && _items.TryGet(job.Item, out var it))
            {
                if (it.CarriedBy == assignee) _itemsWriter.PutDown(job.Item, dropCarriedAt);
                _itemsWriter.Release(job.Item, assignee);
            }
            if (_colonists.Contains(assignee))
            {
                _writer.SetJob(assignee, 0);
                if (_colonists.Get(assignee).IsActive) _writer.SetActivity(assignee, ColonistActivity.Idle);
            }
            _movers.TryGetValue(assignee, out var mover);
            mover?.Clear();
        }
        job.Assignee = EntityId.None;
        job.Item = job.Type == JobType.Haul ? job.Item : EntityId.None;
        job.Phase = 0;
        job.Progress = 0f;
        // Personal jobs die on release; designation-backed jobs reopen.
        if (job.Type is JobType.Eat or JobType.Sleep or JobType.Rally) job.State = JobState.Cancelled;
        else job.State = JobState.Open;
        Revision.Bump();
    }

    private void CancelJob(Job job)
    {
        if (job.State == JobState.Active)
            ReleaseJob(job, dropCarriedAt: _colonists.TryGet(job.Assignee, out var c) ? c.Cell : job.Target);
        job.State = JobState.Cancelled;
        Revision.Bump();
    }

    private void CompleteJob(Job job)
    {
        var assignee = job.Assignee;
        if (!assignee.IsNone && _colonists.Contains(assignee))
        {
            _writer.SetJob(assignee, 0);
            _writer.SetActivity(assignee, ColonistActivity.Idle);
            _movers.TryGetValue(assignee, out var mover);
            mover?.Clear();
        }
        job.State = JobState.Done;
        Revision.Bump();
    }

    private void SweepFinishedJobs()
    {
        _finishedScratch.Clear();
        foreach (var id in _jobOrder)
        {
            var job = _jobs[id];
            if (job.State is not (JobState.Done or JobState.Cancelled)) continue;
            _finishedScratch.Add(id);
            switch (job.Type)
            {
                case JobType.Dig or JobType.DigStairDown: _digJobCells.Remove(job.Target); break;
                case JobType.Build: _buildJobCells.Remove(job.Target); break;
                case JobType.Repair: _repairJobCells.Remove(job.Target); break;
                case JobType.Haul: _haulJobItems.Remove(job.Item); break;
            }
        }
        foreach (var id in _finishedScratch)
        {
            _jobs.Remove(id);
            _jobOrder.Remove(id);
        }
    }

    // ---- per-colonist brain ----

    private void HandleColonist(EntityId id, float dt)
    {
        var c = _colonists.Get(id);
        var job = c.CurrentJobId != 0 ? GetJob(c.CurrentJobId) : null;

        // Direct rally orders pre-empt everything.
        if (PendingRally.TryGetValue(id, out long rallyId))
        {
            PendingRally.Remove(id);
            if (_jobs.TryGetValue(rallyId, out var rally) && rally.State == JobState.Open)
            {
                if (job != null) ReleaseJob(job, c.Cell);
                Assign(id, rally);
                job = rally;
            }
        }

        // Need interrupts (§5.6): hunger/sleep below thresholds interrupt work.
        if (job == null || job.Type is not (JobType.Eat or JobType.Sleep or JobType.Rally))
        {
            if (c.Food < GameConfig.FoodSeekThreshold && HasReachableFood(id))
            {
                if (job != null) ReleaseJob(job, c.Cell);
                job = NewJob(JobType.Eat, c.Cell, 1);
                Assign(id, job);
            }
            else if (c.Sleep < GameConfig.SleepSeekThreshold)
            {
                if (job != null) ReleaseJob(job, c.Cell);
                job = NewJob(JobType.Sleep, c.Cell, 2);
                Assign(id, job);
            }
        }

        job ??= TryAssignBest(id);
        if (job == null)
        {
            if (c.Activity != ColonistActivity.Idle) _writer.SetActivity(id, ColonistActivity.Idle);
            return;
        }

        ExecuteJob(id, job, dt);
    }

    private void Assign(EntityId id, Job job)
    {
        job.State = JobState.Active;
        job.Assignee = id;
        _writer.SetJob(id, job.Id);
        GetMover(id).Clear();
        Revision.Bump();
    }

    private MoverState GetMover(EntityId id)
    {
        if (!_movers.TryGetValue(id, out var m)) { m = new MoverState(); _movers[id] = m; }
        return m;
    }

    private Job? TryAssignBest(EntityId id)
    {
        var c = _colonists.Get(id);
        Job? best = null;
        long bestScore = long.MaxValue;
        foreach (var jobId in _jobOrder)
        {
            var job = _jobs[jobId];
            if (job.State != JobState.Open) continue;
            if (job.Type is JobType.Eat or JobType.Sleep or JobType.Rally) continue; // personal, never queued
            if (!JobViable(job, c)) continue;
            long score = ((long)job.Priority << 40) + ((long)CellCoord.Octile(c.Cell, job.Target) << 16) + (jobId & 0xFFFF);
            if (score < bestScore) { bestScore = score; best = job; }
        }
        if (best != null) Assign(id, best);
        return best;
    }

    private bool JobViable(Job job, in ColonistData c)
    {
        switch (job.Type)
        {
            case JobType.Dig:
                // Orthogonal mining reach: never open a corner-sealed pocket.
                return _regions.ReachableAdjacent(c.Cell, job.Target, orthogonalOnly: true);
            case JobType.DigStairDown:
            case JobType.Rally:
                return _regions.Reachable(c.Cell, job.Target);
            case JobType.Build:
            case JobType.Repair:
                return _regions.ReachableAdjacent(c.Cell, job.Target)
                       && FindFetchStack(c.Cell, job.Material, EntityId.None) != default;
            case JobType.Haul:
                return _items.TryGet(job.Item, out var it) && it.IsLoose && it.ReservedBy.IsNone
                       && _regions.Reachable(c.Cell, it.Cell)
                       && _stockpiles.TryFindDropCell(it, out _, out _);
            default:
                return false;
        }
    }

    /// <summary>Nearest matching unreserved material stack; deterministic (octile, then store order).</summary>
    private EntityId FindFetchStack(CellCoord from, MaterialId material, EntityId self)
    {
        EntityId best = default;
        int bestDist = int.MaxValue;
        for (int i = 0; i < _items.Count; i++)
        {
            var it = _items[i];
            if (it.Kind != ItemKind.Material || it.Material != material || it.Count < 1) continue;
            if (!it.IsLoose) continue;
            if (!it.ReservedBy.IsNone && it.ReservedBy != self) continue;
            if (!_regions.Reachable(from, it.Cell)) continue;
            int dist = CellCoord.Octile(from, it.Cell) + 100 * Math.Abs(from.Z - it.Cell.Z);
            if (dist < bestDist) { bestDist = dist; best = it.Id; }
        }
        return best;
    }

    private bool HasReachableFood(EntityId id)
    {
        var c = _colonists.Get(id);
        for (int i = 0; i < _items.Count; i++)
        {
            var it = _items[i];
            if (it.Kind == ItemKind.Food && it.IsLoose && it.ReservedBy.IsNone && it.Count >= 1
                && _regions.Reachable(c.Cell, it.Cell))
                return true;
        }
        return false;
    }

    // ---- execution ----

    private void ExecuteJob(EntityId id, Job job, float dt)
    {
        switch (job.Type)
        {
            case JobType.Dig: ExecuteDig(id, job, dt, stairDown: false); break;
            case JobType.DigStairDown: ExecuteDig(id, job, dt, stairDown: true); break;
            case JobType.Build: ExecuteBuild(id, job, dt); break;
            case JobType.Repair: ExecuteRepair(id, job, dt); break;
            case JobType.Haul: ExecuteHaul(id, job, dt); break;
            case JobType.Eat: ExecuteEat(id, job, dt); break;
            case JobType.Sleep: ExecuteSleep(id, job); break;
            case JobType.Rally: ExecuteRally(id, job, dt); break;
        }
    }

    private enum MoveResult { Moving, Arrived, Blocked }

    /// <summary>Fixed-dt movement along a revision-validated path.</summary>
    private MoveResult MoveToward(EntityId id, CellCoord goal, GoalMode mode, float dt)
    {
        var c = _colonists.Get(id);
        var mover = GetMover(id);

        // Finish any half-taken step first (also covers resuming from a save mid-step).
        if (c.NextCell != c.Cell)
        {
            if (StepAdvance(id, c.Cell, c.NextCell, c.MoveProgress, dt)) return MoveResult.Moving;
            c = _colonists.Get(id);
        }

        bool goalReached = mode switch
        {
            GoalMode.Exact => c.Cell == goal,
            GoalMode.Adjacent4 => c.Cell.Z == goal.Z &&
                                  Math.Abs(c.Cell.X - goal.X) + Math.Abs(c.Cell.Y - goal.Y) == 1,
            _ => c.Cell.Z == goal.Z && c.Cell != goal && CellCoord.Chebyshev(c.Cell, goal) == 1,
        };
        if (goalReached) { mover.Clear(); return MoveResult.Arrived; }

        if (mover.HasPath && mover.PlannedRevision != _terrain.Revision.Value)
        {
            if (RemainingPathValid(mover, c.Cell)) mover.PlannedRevision = _terrain.Revision.Value;
            else mover.Clear(); // one recompute below
        }

        if (!mover.HasPath || mover.Cursor >= mover.Path.Count)
        {
            mover.Clear();
            if (!_pathfinder.FindPath(c.Cell, goal, MoveContext.ColonistRealTime, id, mode, mover.Path))
                return MoveResult.Blocked; // sealed goal: empty path, clean cancel upstream
            if (mover.Path.Count == 0) { return MoveResult.Arrived; }
            mover.HasPath = true;
            mover.Cursor = 0;
            mover.PlannedRevision = _terrain.Revision.Value;
        }

        var next = mover.Path[mover.Cursor];
        if (!_walk.CanEnter(next, MoveContext.ColonistRealTime, id)) { mover.Clear(); return MoveResult.Moving; }
        _writer.BeginStep(id, next);
        mover.Cursor++;
        StepAdvance(id, c.Cell, next, 0f, dt);
        if (_colonists.Get(id).Activity != ColonistActivity.Traveling &&
            _colonists.Get(id).Activity is not (ColonistActivity.Eating or ColonistActivity.Sleeping))
            _writer.SetActivity(id, ColonistActivity.Traveling);
        return MoveResult.Moving;
    }

    /// <summary>Advances one step's progress; returns true while still mid-step.</summary>
    private bool StepAdvance(EntityId id, CellCoord from, CellCoord to, float progress01, float dt)
    {
        int cost = StepCost(from, to);
        float stepSeconds = cost / 10f / GameConfig.ColonistMoveCellsPerSecond;
        float elapsed = progress01 * stepSeconds + dt;
        if (elapsed >= stepSeconds)
        {
            _writer.CompleteStep(id);
            return false;
        }
        _writer.SetMoveProgress(id, elapsed / stepSeconds);
        return true;
    }

    private int StepCost(CellCoord from, CellCoord to)
    {
        int cost = from.Z != to.Z ? 10 : (from.X != to.X && from.Y != to.Y) ? 14 : 10;
        return cost + _walk.EnterSurcharge(to, MoveContext.ColonistRealTime);
    }

    private bool RemainingPathValid(MoverState mover, CellCoord current)
    {
        var prev = current;
        for (int i = mover.Cursor; i < mover.Path.Count; i++)
        {
            var cell = mover.Path[i];
            if (!_walk.CanEnter(cell, MoveContext.ColonistRealTime, EntityId.None)) return false;
            int dx = cell.X - prev.X, dy = cell.Y - prev.Y;
            if (cell.Z == prev.Z && dx != 0 && dy != 0 && !_walk.DiagonalLegal(prev, dx, dy, MoveContext.ColonistRealTime))
                return false;
            prev = cell;
        }
        return true;
    }

    private void ExecuteDig(EntityId id, Job job, float dt, bool stairDown)
    {
        var digCell = stairDown ? job.Target.Down : job.Target;
        var cell = _terrain.Get(digCell);
        if (!cell.HasWall) // someone else cleared it (or a raider did)
        {
            _designations.RemoveDig(job.Target);
            CompleteJob(job);
            return;
        }

        var move = MoveToward(id, job.Target, stairDown ? GoalMode.Exact : GoalMode.Adjacent4, dt);
        if (move == MoveResult.Blocked) { ReleaseJob(job, _colonists.Get(id).Cell); return; }
        if (move != MoveResult.Arrived) return;

        _writer.SetActivity(id, ColonistActivity.Working);
        job.Progress += dt;
        float need = MaterialCatalog.Get(cell.WallMaterial).DigSeconds;
        if (job.Progress < need) return;

        var mat = cell.WallMaterial;
        _terrain.DigWall(digCell);
        if (stairDown) _terrain.SetFloor(digCell, FloorKind.Stair, mat);
        _itemsWriter.Spawn(ItemKind.Material, mat, 1, digCell);
        _stats.CellsDug++;
        _designations.RemoveDig(job.Target);
        CompleteJob(job);
    }

    private void ExecuteBuild(EntityId id, Job job, float dt)
    {
        if (job.Phase == 0) // reserve + fetch material
        {
            if (job.Item.IsNone || !_items.Contains(job.Item))
            {
                var stack = FindFetchStack(_colonists.Get(id).Cell, job.Material, id);
                if (stack == default || !_itemsWriter.TryReserve(stack, id)) { ReleaseJob(job, _colonists.Get(id).Cell); return; }
                job.Item = stack;
            }
            var it = _items.Get(job.Item);
            var move = MoveToward(id, it.Cell, GoalMode.Exact, dt);
            if (move == MoveResult.Blocked) { ReleaseJob(job, _colonists.Get(id).Cell); return; }
            if (move != MoveResult.Arrived) return;
            // Split one unit off (ids never reused; the carried unit is a fresh stack).
            if (it.Count > 1)
            {
                _itemsWriter.Consume(job.Item, 1);
                _itemsWriter.Release(job.Item, id);
                var carried = _itemsWriter.Spawn(ItemKind.Material, job.Material, 1, it.Cell);
                _itemsWriter.TryReserve(carried, id);
                _itemsWriter.PickUp(carried, id);
                job.Item = carried;
            }
            else
            {
                _itemsWriter.PickUp(job.Item, id);
            }
            job.Phase = 1;
            return;
        }

        if (job.Phase == 1) // deliver to site
        {
            var move = MoveToward(id, job.Target, GoalMode.Adjacent8, dt);
            if (move == MoveResult.Blocked) { ReleaseJob(job, _colonists.Get(id).Cell); return; }
            if (move != MoveResult.Arrived) return;
            job.Phase = 2;
            job.Progress = 0f;
            return;
        }

        // Phase 2: work on site.
        if (job.BuildKind == BlueprintKind.Wall && _occupancy.CountAt(job.Target) > 0)
            return; // wait until the cell is clear of units

        _writer.SetActivity(id, ColonistActivity.Working);
        job.Progress += dt;
        if (job.Progress < GameConfig.BuildSeconds) return;

        // Consume the carried unit and place the construction.
        _itemsWriter.Consume(job.Item, 1);
        job.Item = EntityId.None;
        switch (job.BuildKind)
        {
            case BlueprintKind.Wall:
                _terrain.BuildWall(job.Target, job.Material);
                _stats.WallsBuilt++;
                break;
            case BlueprintKind.Floor:
                _terrain.SetFloor(job.Target, FloorKind.Built, job.Material);
                break;
            case BlueprintKind.Stair:
                _terrain.SetFloor(job.Target, FloorKind.Stair, job.Material);
                break;
            case BlueprintKind.Door:
                if (!_doors.TryGetAt(job.Target, out _)) _doorsWriter.Place(job.Target, job.Material);
                break;
            case BlueprintKind.Torch:
                _propsWriter.Place(job.Target, PropKind.Torch, job.Material);
                break;
        }
        _designations.RemoveBuild(job.Target);
        CompleteJob(job);
    }

    private void ExecuteRepair(EntityId id, Job job, float dt)
    {
        var cell = _terrain.Get(job.Target);
        bool doorTarget = _doors.TryGetAt(job.Target, out var door);
        bool damaged = doorTarget
            ? door.Hp < GameConfig.DoorHp || door.IsBroken
            : cell.HasWall && cell.WallHp < MaterialCatalog.Get(cell.WallMaterial).MaxWallHp;
        if (!damaged)
        {
            _designations.RemoveRepair(job.Target);
            CompleteJob(job);
            return;
        }

        if (job.Phase == 0)
        {
            if (job.Item.IsNone || !_items.Contains(job.Item))
            {
                var stack = FindFetchStack(_colonists.Get(id).Cell, job.Material, id);
                if (stack == default || !_itemsWriter.TryReserve(stack, id)) { ReleaseJob(job, _colonists.Get(id).Cell); return; }
                job.Item = stack;
            }
            var it = _items.Get(job.Item);
            var move = MoveToward(id, it.Cell, GoalMode.Exact, dt);
            if (move == MoveResult.Blocked) { ReleaseJob(job, _colonists.Get(id).Cell); return; }
            if (move != MoveResult.Arrived) return;
            if (it.Count > 1)
            {
                _itemsWriter.Consume(job.Item, 1);
                _itemsWriter.Release(job.Item, id);
                var carried = _itemsWriter.Spawn(ItemKind.Material, job.Material, 1, it.Cell);
                _itemsWriter.TryReserve(carried, id);
                _itemsWriter.PickUp(carried, id);
                job.Item = carried;
            }
            else
            {
                _itemsWriter.PickUp(job.Item, id);
            }
            job.Phase = 1;
            return;
        }

        if (job.Phase == 1)
        {
            var move = MoveToward(id, job.Target, GoalMode.Adjacent8, dt);
            if (move == MoveResult.Blocked) { ReleaseJob(job, _colonists.Get(id).Cell); return; }
            if (move != MoveResult.Arrived) return;
            job.Phase = 2;
            job.Progress = 0f;
            return;
        }

        _writer.SetActivity(id, ColonistActivity.Working);
        job.Progress += dt;
        if (job.Progress < GameConfig.RepairSeconds) return;

        _itemsWriter.Consume(job.Item, 1);
        job.Item = EntityId.None;
        if (doorTarget) _doorsWriter.Repair(door.Id, GameConfig.DoorHp);
        else _terrain.RepairWall(job.Target, MaterialCatalog.Get(cell.WallMaterial).MaxWallHp);
        _designations.RemoveRepair(job.Target);
        CompleteJob(job);
    }

    private void ExecuteHaul(EntityId id, Job job, float dt)
    {
        if (!_items.TryGet(job.Item, out var it)) { CancelJob(job); return; }

        if (job.Phase == 0)
        {
            if (it.ReservedBy != id && !_itemsWriter.TryReserve(job.Item, id)) { CancelJob(job); return; }
            var move = MoveToward(id, it.Cell, GoalMode.Exact, dt);
            if (move == MoveResult.Blocked) { ReleaseJob(job, _colonists.Get(id).Cell); return; }
            if (move != MoveResult.Arrived) return;
            _itemsWriter.PickUp(job.Item, id);
            if (!_stockpiles.TryFindDropCell(_items.Get(job.Item), out var drop, out var merge))
            { ReleaseJob(job, _colonists.Get(id).Cell); return; }
            job.DropCell = drop;
            job.MergeTarget = merge;
            job.Phase = 1;
            _writer.SetActivity(id, ColonistActivity.Working);
            return;
        }

        var moveDrop = MoveToward(id, job.DropCell, GoalMode.Exact, dt);
        if (moveDrop == MoveResult.Blocked) { ReleaseJob(job, _colonists.Get(id).Cell); return; }
        if (moveDrop != MoveResult.Arrived) return;

        _itemsWriter.PutDown(job.Item, job.DropCell);
        _itemsWriter.Release(job.Item, id);
        if (!job.MergeTarget.IsNone && _items.Contains(job.MergeTarget) && _items.Contains(job.Item))
        {
            var tgt = _items.Get(job.MergeTarget);
            if (tgt.IsLoose && tgt.Cell == job.DropCell)
                _itemsWriter.MergeInto(job.Item, job.MergeTarget);
        }
        CompleteJob(job);
    }

    private void ExecuteEat(EntityId id, Job job, float dt)
    {
        if (job.Item.IsNone || !_items.Contains(job.Item))
        {
            var c = _colonists.Get(id);
            EntityId best = default;
            int bestDist = int.MaxValue;
            for (int i = 0; i < _items.Count; i++)
            {
                var it = _items[i];
                if (it.Kind != ItemKind.Food || !it.IsLoose || !it.ReservedBy.IsNone || it.Count < 1) continue;
                if (!_regions.Reachable(c.Cell, it.Cell)) continue;
                int dist = CellCoord.Octile(c.Cell, it.Cell) + 100 * Math.Abs(c.Cell.Z - it.Cell.Z);
                if (dist < bestDist) { bestDist = dist; best = it.Id; }
            }
            if (best == default || !_itemsWriter.TryReserve(best, id)) { CancelJob(job); return; }
            job.Item = best;
        }

        var food = _items.Get(job.Item);
        var move = MoveToward(id, food.Cell, GoalMode.Exact, dt);
        if (move == MoveResult.Blocked) { _itemsWriter.Release(job.Item, id); CancelJob(job); return; }
        if (move != MoveResult.Arrived)
        {
            return;
        }

        _writer.SetActivity(id, ColonistActivity.Eating);
        job.Progress += dt;
        if (job.Progress < 1.5f) return; // brief, visible meal

        _itemsWriter.Release(job.Item, id);
        _itemsWriter.Consume(job.Item, 1);
        var data = _colonists.Get(id);
        _needsWriter.SetNeeds(id, GameConfig.FoodMax, data.Sleep, data.Morale);
        CompleteJob(job);
    }

    private void ExecuteSleep(EntityId id, Job job)
    {
        var c = _colonists.Get(id);
        if (c.Activity != ColonistActivity.Sleeping)
        {
            _writer.SetActivity(id, ColonistActivity.Sleeping);
            return;
        }
        if (c.Sleep >= GameConfig.SleepMax - 0.01f)
            CompleteJob(job); // wakes rested; NeedsSystem restored the bar
    }

    private void ExecuteRally(EntityId id, Job job, float dt)
    {
        var move = MoveToward(id, job.Target, GoalMode.Exact, dt);
        if (move == MoveResult.Blocked) { CancelJob(job); return; }
        if (move == MoveResult.Arrived) CompleteJob(job);
    }

    // ---- serialization ----

    public void WriteTo(BinaryWriter w)
    {
        w.Write(_nextJobId);
        w.Write(_jobOrder.Count);
        foreach (var id in _jobOrder) _jobs[id].WriteTo(w);

        w.Write(PendingRally.Count);
        foreach (var kv in PendingRally.OrderBy(k => k.Key.Value)) { w.Write(kv.Key.Value); w.Write(kv.Value); }

        // Mover paths are sim state: a reloaded world must keep following the same routes.
        var moverIds = _movers.Keys.Where(k => _movers[k].HasPath).OrderBy(k => k.Value).ToList();
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
        _jobs.Clear(); _jobOrder.Clear();
        _digJobCells.Clear(); _buildJobCells.Clear(); _repairJobCells.Clear(); _haulJobItems.Clear();
        PendingRally.Clear(); _movers.Clear();

        _nextJobId = r.ReadInt64();
        int n = r.ReadInt32();
        for (int i = 0; i < n; i++)
        {
            var job = Job.ReadFrom(r);
            _jobs[job.Id] = job;
            _jobOrder.Add(job.Id);
            switch (job.Type)
            {
                case JobType.Dig or JobType.DigStairDown: _digJobCells.Add(job.Target); break;
                case JobType.Build: _buildJobCells.Add(job.Target); break;
                case JobType.Repair: _repairJobCells.Add(job.Target); break;
                case JobType.Haul: _haulJobItems.Add(job.Item); break;
            }
        }
        n = r.ReadInt32();
        for (int i = 0; i < n; i++) PendingRally[new EntityId(r.ReadInt64())] = r.ReadInt64();
        n = r.ReadInt32();
        for (int i = 0; i < n; i++)
        {
            var id = new EntityId(r.ReadInt64());
            var m = new MoverState { Cursor = r.ReadInt32(), PlannedRevision = r.ReadUInt64(), HasPath = true };
            int cells = r.ReadInt32();
            for (int j = 0; j < cells; j++)
                m.Path.Add(new CellCoord(r.ReadInt32(), r.ReadInt32(), r.ReadInt32()));
            _movers[id] = m;
        }
        Revision.Bump();
    }
}
