using Hollowdeep.Core.Colony;
using Hollowdeep.Core.Combat;
using Hollowdeep.Core.Primitives;
using Hollowdeep.Core.Save;
using Hollowdeep.Core.Sim;
using Hollowdeep.Core.Terrain;

namespace Hollowdeep.Core.Harness;

/// <summary>
/// The scripted headless smoke (§7.2): dig orders → build orders → raid → auto-resolved
/// battle → reconcile → repairs. Pure core, fixed seed, substep-exact scripting — the
/// final world hash must be stable across runs. Also reachable from the debug console.
/// </summary>
public static class SmokeScenario
{
    public static ulong Run(GameWorld world, Action<string>? log = null)
    {
        void Log(string s) => log?.Invoke(s);

        world.NewGame();
        Log($"world generated; hash {SaveCodec.WorldHash(world):x16}");

        // Carve an entry corridor and a room into the hillside east of the clearing —
        // a drag-rectangle over the ragged hill face, exactly as a player would paint it
        // (non-wall cells inside the rect are rejected by designation validation).
        int z = GameConfig.SurfaceZ;
        for (int y = 31; y <= 33; y++)
        for (int x = 20; x <= 27; x++)
            world.Designations.DesignateDig(new CellCoord(x, y, z));
        for (int y = 29; y <= 35; y++)
        for (int x = 28; x <= 32; x++)
            world.Designations.DesignateDig(new CellCoord(x, y, z));

        StepSeconds(world, 90);
        Log($"after digging phase: dug {world.Stats.CellsDug} cells, {world.Items.Count} item stacks");
        if (world.Stats.CellsDug < 10)
            throw new InvalidOperationException($"Smoke: digging stalled at {world.Stats.CellsDug} cells.");

        // Fortify: door at the corridor mouth, walls narrowing the approach, a torch inside.
        world.Designations.DesignateBuild(new CellCoord(22, 32, z), BlueprintKind.Door, MaterialId.Dirt);
        world.Designations.DesignateBuild(new CellCoord(22, 31, z), BlueprintKind.Wall, MaterialId.Dirt);
        world.Designations.DesignateBuild(new CellCoord(22, 33, z), BlueprintKind.Wall, MaterialId.Dirt);
        world.Designations.DesignateBuild(new CellCoord(24, 32, z), BlueprintKind.Torch, MaterialId.Dirt);
        StepSeconds(world, 45);
        Log($"after build phase: built {world.Stats.WallsBuilt} walls, doors {world.Doors.Count}");

        // Raid: trigger, wait through the warning, let raiders close until the switch.
        world.Raids.DebugTriggerRaid();
        int guard = 0;
        while (world.Time.Mode == SimMode.RealTime && guard++ < 240)
            StepSeconds(world, 1);
        if (world.Time.Mode != SimMode.TurnBased)
            throw new InvalidOperationException("Smoke: raid never triggered the mode switch.");
        Log($"battle joined: {world.Raiders.AliveCount()} raiders, reason '{world.Combat.Encounter.Reason}'");

        world.Combat.AutoResolve();
        if (world.Time.Mode != SimMode.RealTime)
            throw new InvalidOperationException("Smoke: battle did not resolve back to real time.");
        var outcome = world.LastOutcome ?? throw new InvalidOperationException("Smoke: no outcome report.");
        Log($"battle over: {outcome.Result}, raiders killed {outcome.RaidersKilled}, " +
            $"colonists downed {outcome.ColonistsDowned}, walls destroyed {outcome.WallsDestroyed}");
        if (world.Raiders.Count != 0)
            throw new InvalidOperationException("Smoke: reconcile left raiders behind.");

        // Scars → repairs (the loop-closer, Pillar 3).
        foreach (var scar in world.LastBattleScars)
            if (scar.Kind == ScarKind.WallDamaged)
                world.Designations.DesignateRepair(scar.Cell);
        StepSeconds(world, 45);

        ulong hash = SaveCodec.WorldHash(world);
        Log($"final hash {hash:x16} at tick {world.Time.TickSequence}");
        return hash;
    }

    public static void StepSeconds(GameWorld world, int seconds)
        => world.Time.RealTime.Step(seconds * 60);
}
