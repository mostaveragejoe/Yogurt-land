using Hollowdeep.Core.Colony;
using Hollowdeep.Core.Combat;
using Hollowdeep.Core.Entities;
using Hollowdeep.Core.Path;
using Hollowdeep.Core.Primitives;
using Hollowdeep.Core.Raid;
using Hollowdeep.Core.Rng;
using Hollowdeep.Core.Sim;
using Hollowdeep.Core.Terrain;

namespace Hollowdeep.Core;

/// <summary>
/// The composition root: builds every store, system, and authority, hands out writers,
/// and implements the switch hooks (placement normalization and post-encounter
/// reconcile). This is the only place writers are constructed (ADR-0003).
/// </summary>
public sealed class GameWorld : ISwitchHooks
{
    public MutationGate Gate { get; }
    public RngHub Rng { get; }
    public EntityIdAllocator Ids { get; }
    public UnitOccupancyIndex Occupancy { get; }
    public TerrainWorld Terrain { get; }
    public ColonistStore Colonists { get; }
    public RaiderStore Raiders { get; }
    public ItemStore Items { get; }
    public DoorStore Doors { get; }
    public TimeAuthorityManager Time { get; }
    public Walkability Walk { get; }
    public Pathfinder Pathfinder { get; }
    public RegionIndex Regions { get; }
    public ColonyStats Stats { get; }
    public ScarLedger Scars { get; }
    public EncounterOutcomeInbox OutcomeInbox { get; }
    public DesignationSystem Designations { get; }
    public StockpileSystem Stockpiles { get; }
    public FarmSystem Farms { get; }
    public NeedsSystem Needs { get; }
    public JobSystem Jobs { get; }
    public RaidSystem Raids { get; }
    public TurnBasedAuthority Combat { get; }

    internal StoreWriters Writers { get; }
    internal ISpawnWriter SpawnWriter => Writers;
    internal IItemsWriter ItemsWriter => Writers;
    internal IReconcileWriter ReconcileWriter => Writers;

    /// <summary>Drained outcome reports surface here for logging/UI (veterancy is out of scope).</summary>
    public event Action<EncounterOutcomeReport>? OutcomeReported;

    /// <summary>Scars of the raid that just ended, for the post-battle summary (Pillar 3).</summary>
    public readonly List<Scar> LastBattleScars = new();
    public EncounterOutcomeReport? LastOutcome { get; private set; }

    public GameWorld(ulong seed = GameConfig.WorldSeed)
    {
        Gate = new MutationGate();
        Rng = new RngHub(seed);
        Ids = new EntityIdAllocator();
        Occupancy = new UnitOccupancyIndex();
        Terrain = new TerrainWorld(GameConfig.MapWidth, GameConfig.MapHeight, GameConfig.MapDepth, Gate);
        Colonists = new ColonistStore(Gate, Occupancy);
        Raiders = new RaiderStore(Gate, Occupancy);
        Items = new ItemStore(Gate);
        Doors = new DoorStore(Gate);
        Time = new TimeAuthorityManager(Gate);
        Walk = new Walkability(Terrain, Doors, Occupancy);
        Pathfinder = new Pathfinder(Terrain, Doors, Walk);
        Regions = new RegionIndex(Terrain, Walk);
        Stats = new ColonyStats();
        Scars = new ScarLedger();
        OutcomeInbox = new EncounterOutcomeInbox();

        Writers = new StoreWriters(Gate, Time, Ids, Colonists, Raiders, Items, Doors);

        Designations = new DesignationSystem(Terrain);
        Stockpiles = new StockpileSystem(Items);
        Farms = new FarmSystem(Writers, Items);
        Needs = new NeedsSystem(Colonists, Writers);
        Jobs = new JobSystem(Colonists, Items, Doors, Terrain, Occupancy, Walk, Pathfinder, Regions,
            Designations, Stockpiles, Stats, Writers, Writers, Writers, Writers);
        Raids = new RaidSystem(Terrain, Colonists, Raiders, Doors, Items, Walk, Pathfinder, Stats,
            Stockpiles, Writers, Writers, Rng.Raids, Scars, Time);
        Combat = new TurnBasedAuthority(Gate, Time, Colonists, Raiders, Doors, Terrain, Occupancy,
            Walk, Pathfinder, Writers, Writers, Writers, Rng.Combat, Scars, OutcomeInbox);

        Time.SetHooks(this);
        // Fixed tick order — part of determinism.
        Time.RealTime.AddSystem(Needs);
        Time.RealTime.AddSystem(Farms);
        Time.RealTime.AddSystem(Jobs);
        Time.RealTime.AddSystem(Raids);
    }

    /// <summary>Generates the fixed-seed mountain and starting colony.</summary>
    public void NewGame()
    {
        using var _ = Gate.Open();
        WorldGen.Generate(Terrain, Rng.WorldGen, Writers, Stockpiles, Farms, Stats);
    }

    // ---- ISwitchHooks ----

    /// <summary>
    /// Deterministic pre-switch placement normalization (ADR-0003): decided against the
    /// DECISION SET, not live occupancy — the lowest id keeps its cell; every other
    /// co-located unit nudges to the nearest free cell, ties broken by cell index.
    /// Mid-steps snap back to the occupied cell.
    /// </summary>
    public void NormalizePlacements()
    {
        Gate.AssertOpen();
        var units = new List<(EntityId Id, CellCoord Cell)>();
        for (int i = 0; i < Colonists.Count; i++)
            if (Colonists[i].IsActive) units.Add((Colonists[i].Id, Colonists[i].Cell));
        for (int i = 0; i < Raiders.Count; i++)
            if (Raiders[i].IsAlive) units.Add((Raiders[i].Id, Raiders[i].Cell));
        units.Sort((a, b) => a.Id.CompareTo(b.Id));

        var claimed = new HashSet<CellCoord>();
        foreach (var (id, cell) in units)
        {
            var final = claimed.Contains(cell) ? FindNudgeCell(cell, claimed) : cell;
            claimed.Add(final);
            ApplyNormalizedPlacement(id, final);
        }
    }

    private CellCoord FindNudgeCell(CellCoord from, HashSet<CellCoord> claimed)
    {
        for (int radius = 1; radius <= 12; radius++)
        {
            CellCoord best = default;
            int bestIndex = int.MaxValue;
            for (int dy = -radius; dy <= radius; dy++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != radius) continue;
                var c = from.Offset(dx, dy);
                if (!Terrain.InBounds(c) || claimed.Contains(c)) continue;
                if (!Walk.PhysicallyOpen(c, MoveContext.TurnBased)) continue;
                int index = c.Y * Terrain.Width + c.X; // tie-break by cell index
                if (index < bestIndex) { bestIndex = index; best = c; }
            }
            if (bestIndex != int.MaxValue) return best;
        }
        return from; // pathological sealed pocket: keep the overlap rather than teleport
    }

    private void ApplyNormalizedPlacement(EntityId id, CellCoord cell)
    {
        if (id.Kind == EntityKind.Colonist)
        {
            Colonists.SetCell(id, cell);
            ref var d = ref Colonists.Ref(id);
            d.NextCell = cell;
            d.MoveProgress = 0f;
        }
        else
        {
            Raiders.SetCell(id, cell);
            ref var d = ref Raiders.Ref(id);
            d.NextCell = cell;
            d.MoveProgress = 0f;
        }
    }

    public void BeginEncounter(SwitchTransitionData data) => Combat.BeginEncounter(data);

    /// <summary>
    /// Runs exactly once on return to real time (ADR-0001): drains the outcome inbox,
    /// stabilizes downed colonists, reaps ALL raiders (live ones would become ghosts),
    /// and clears the encounter side tables.
    /// </summary>
    public void PostEncounterReconcile()
    {
        Gate.AssertOpen();
        var report = OutcomeInbox.Drain();
        LastOutcome = report;
        Scars.CollectRaid(report.RaidIndex, LastBattleScars);

        for (int i = 0; i < Colonists.Count; i++)
            if (Colonists[i].IsDowned && !Colonists[i].IsDead)
                ReconcileWriter.StabilizeDowned(Colonists[i].Id);

        ReconcileWriter.ReapAllRaiders();
        Combat.Encounter.Clear();
        Raids.OnReconcile();
        OutcomeReported?.Invoke(report);
    }
}
