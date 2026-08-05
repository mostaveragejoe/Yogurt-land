using Hollowdeep.Core;
using Hollowdeep.Core.Colony;
using Hollowdeep.Core.Entities;
using Hollowdeep.Core.Harness;
using Hollowdeep.Core.Primitives;
using Hollowdeep.Core.Sim;
using Hollowdeep.Core.Terrain;
using Xunit;
using Xunit.Abstractions;

namespace Hollowdeep.Core.Tests;

/// <summary>
/// Long-horizon proof of the §6 loop: multiple sim-days, escalating raids, food
/// economy, and the repair cycle — the closest an automated run gets to a playtest.
/// </summary>
public class EnduranceTests
{
    private readonly ITestOutputHelper _out;
    public EnduranceTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void TwoRaidsOverTwoSimDaysColonySurvivesAndSecondRaidIsBigger()
    {
        var world = new GameWorld();
        world.NewGame();

        var raidSizes = new List<int>();
        var outcomes = new List<EncounterOutcomeReport>();
        world.Time.SwitchedToTurnBased += _ => raidSizes.Add(world.Raiders.AliveCount());
        world.OutcomeReported += outcomes.Add;

        // Opening play: corridor + room + defenses, like a player's first half hour.
        int z = GameConfig.SurfaceZ;
        for (int y = 31; y <= 33; y++)
            for (int x = 20; x <= 27; x++)
                world.Designations.DesignateDig(new CellCoord(x, y, z));
        for (int y = 29; y <= 35; y++)
            for (int x = 28; x <= 32; x++)
                world.Designations.DesignateDig(new CellCoord(x, y, z));
        // Two sim-days; battles auto-resolve as they come; scars trigger repairs.
        // Fortifications get painted once the corridor mouth is dug open, like a player.
        bool fortified = false;
        int totalSeconds = (int)(2 * GameConfig.DayLengthSeconds); // 960 s
        for (int t = 0; t < totalSeconds; t++)
        {
            SmokeScenario.StepSeconds(world, 1);
            if (!fortified && t >= 150)
            {
                fortified = true;
                Assert.True(world.Designations.DesignateBuild(new CellCoord(22, 32, z), BlueprintKind.Door, MaterialId.Dirt));
                Assert.True(world.Designations.DesignateBuild(new CellCoord(22, 31, z), BlueprintKind.Wall, MaterialId.Dirt));
                Assert.True(world.Designations.DesignateBuild(new CellCoord(22, 33, z), BlueprintKind.Wall, MaterialId.Dirt));
                Assert.True(world.Designations.DesignateBuild(new CellCoord(24, 32, z), BlueprintKind.Torch, MaterialId.Dirt));
            }
            if (world.Time.Mode == SimMode.TurnBased)
            {
                world.Combat.AutoResolve();
                Assert.Equal(SimMode.RealTime, world.Time.Mode);
                Assert.Equal(0, world.Raiders.Count); // reap completeness every battle
                foreach (var scar in world.LastBattleScars)
                    if (scar.Kind is Hollowdeep.Core.Combat.ScarKind.WallDamaged or Hollowdeep.Core.Combat.ScarKind.DoorDamaged
                        or Hollowdeep.Core.Combat.ScarKind.DoorBroken)
                        world.Designations.DesignateRepair(scar.Cell);
            }
        }

        _out.WriteLine($"days simulated: {world.Stats.WorldSeconds / GameConfig.DayLengthSeconds:0.0}");
        _out.WriteLine($"raids fought: {outcomes.Count} (sizes: {string.Join(", ", raidSizes)})");
        foreach (var o in outcomes)
            _out.WriteLine($"  raid {o.RaidIndex}: {o.Result}, killed {o.RaidersKilled}, downed {o.ColonistsDowned}, dead {o.ColonistsDead}");
        int foodStacks = 0, alive = 0;
        for (int i = 0; i < world.Items.Count; i++)
            if (world.Items[i].Kind == ItemKind.Food) foodStacks += world.Items[i].Count;
        for (int i = 0; i < world.Colonists.Count; i++)
            if (!world.Colonists[i].IsDead) alive++;
        _out.WriteLine($"colonists alive: {alive}/10, food units: {foodStacks}, dug {world.Stats.CellsDug}, built {world.Stats.WallsBuilt}");

        // The loop holds: at least two raids, escalation, survival, food economy.
        Assert.True(outcomes.Count >= 2, $"expected ≥2 raids, got {outcomes.Count}");
        Assert.True(raidSizes[^1] > raidSizes[0], $"raid sizes did not escalate: {string.Join(",", raidSizes)}");
        Assert.True(alive >= 6, $"colony collapsed: only {alive} colonists alive");
        Assert.True(foodStacks > 0, "food economy collapsed (mushroom farm + hauling failed)");
        Assert.True(world.Stats.CellsDug > 20, "colonists stopped digging");
        Assert.True(world.Stats.WallsBuilt >= 2, $"construction pipeline stalled: built {world.Stats.WallsBuilt}");
        Assert.True(world.Doors.Count >= 1, "the door was never built");
        Assert.True(world.Props.Count >= 2, "the torch was never built"); // hearth + torch
    }

    [Fact]
    public void SteadyStateSimAllocationIsNearZero()
    {
        var world = new GameWorld();
        world.NewGame();
        // Warm up: caches, pools, pathfinder arrays, first jobs.
        SmokeScenario.StepSeconds(world, 30);

        long before = GC.GetAllocatedBytesForCurrentThread();
        SmokeScenario.StepSeconds(world, 10); // 600 sub-steps of ordinary colony life
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        double perStep = allocated / 600.0;
        _out.WriteLine($"steady-state allocation: {allocated} B over 600 sub-steps = {perStep:0.0} B/sub-step");
        // §4f: zero steady-state allocation is the goal; the farm/haul cycle may allocate
        // a handful of job/stack objects ON CHANGE, so the bound is a loose ceiling —
        // regressions (per-tick garbage) blow straight past it.
        Assert.True(perStep < 200, $"steady-state allocation too high: {perStep:0.0} B/sub-step");
    }
}

public class RepairAndPropTests
{
    [Fact]
    public void TorchBlueprintBuildsAnAestheticProp()
    {
        var world = new GameWorld();
        world.NewGame();
        var cell = new CellCoord(14, 34, GameConfig.SurfaceZ);
        Assert.True(world.Designations.DesignateBuild(cell, BlueprintKind.Torch, MaterialId.Dirt));
        SmokeScenario.StepSeconds(world, 60);
        Assert.True(world.Props.HasAt(cell), "torch prop was not built");
        // Aesthetic only: the cell stays walkable and grants no cover.
        Assert.True(world.Walk.TerrainWalkable(cell));
    }

    [Fact]
    public void DamagedDoorIsRepairableViaDesignation()
    {
        var world = new GameWorld();
        world.NewGame();
        var cell = new CellCoord(15, 35, GameConfig.SurfaceZ);
        EntityId doorId;
        using (world.Gate.Open())
        {
            doorId = world.Doors.Add(world.Ids, cell, MaterialId.Dirt);
            ref var d = ref world.Doors.Ref(doorId);
            d.Hp = 10; // battered but standing
        }
        Assert.True(world.Designations.DesignateRepair(cell), "damaged door refused repair designation");
        SmokeScenario.StepSeconds(world, 90);
        Assert.True(world.Doors.TryGet(doorId, out var door));
        Assert.Equal(GameConfig.DoorHp, door.Hp);
        Assert.False(door.IsBroken);
    }

    [Fact]
    public void BrokenDoorIsRepairableViaDesignation()
    {
        var world = new GameWorld();
        world.NewGame();
        var cell = new CellCoord(16, 35, GameConfig.SurfaceZ);
        EntityId doorId;
        using (world.Gate.Open())
        {
            doorId = world.Doors.Add(world.Ids, cell, MaterialId.Dirt);
            ref var d = ref world.Doors.Ref(doorId);
            d.Hp = 0;
            d.IsBroken = true;
        }
        Assert.True(world.Designations.DesignateRepair(cell), "broken door refused repair designation");
        SmokeScenario.StepSeconds(world, 90);
        Assert.True(world.Doors.TryGet(doorId, out var door));
        Assert.False(door.IsBroken, "door was not restored");
        Assert.Equal(GameConfig.DoorHp, door.Hp);
    }

    [Fact]
    public void WoundedColonistHealsWhileFed()
    {
        var world = new GameWorld();
        world.NewGame();
        var id = world.Colonists[0].Id;
        using (world.Gate.Open())
        {
            ref var d = ref world.Colonists.Ref(id);
            d.Hp = 40;
        }
        SmokeScenario.StepSeconds(world, 60);
        Assert.True(world.Colonists.Get(id).Hp > 40, "wounded colonist did not heal");
    }
}
