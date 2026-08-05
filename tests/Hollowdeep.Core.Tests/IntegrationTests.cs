using Hollowdeep.Core;
using Hollowdeep.Core.Combat;
using Hollowdeep.Core.Entities;
using Hollowdeep.Core.Harness;
using Hollowdeep.Core.Primitives;
using Hollowdeep.Core.Save;
using Hollowdeep.Core.Sim;
using Hollowdeep.Core.Terrain;
using Xunit;

namespace Hollowdeep.Core.Tests;

public class DeterminismTests
{
    [Fact]
    public void SameSeedSameScriptSameWorldHash()
    {
        ulong h1 = SmokeScenario.Run(new GameWorld());
        ulong h2 = SmokeScenario.Run(new GameWorld());
        Assert.Equal(h1, h2);
    }

    [Fact]
    public void TickSequenceIsGaplessAcrossModeSwitches()
    {
        var world = new GameWorld();
        world.NewGame();

        ulong seq0 = world.Time.TickSequence;
        SmokeScenario.StepSeconds(world, 10);
        Assert.Equal(seq0 + 600, world.Time.TickSequence); // one increment per sub-step, no gaps

        // Force a battle.
        world.Raids.DebugTriggerRaid();
        int guard = 0;
        while (world.Time.Mode == SimMode.RealTime && guard++ < 240)
            SmokeScenario.StepSeconds(world, 1);
        Assert.Equal(SimMode.TurnBased, world.Time.Mode);

        ulong seqAtSwitch = world.Time.TickSequence;
        world.Combat.AutoResolve();
        ulong seqAfterBattle = world.Time.TickSequence;
        Assert.True(seqAfterBattle > seqAtSwitch); // combat orders consumed sequence steps

        SmokeScenario.StepSeconds(world, 5);
        Assert.Equal(seqAfterBattle + 300, world.Time.TickSequence); // RT resumes from the same counter
    }

    [Fact]
    public void ReloadedWorldEvolvesIdenticallyToOneThatNeverLeftMemory()
    {
        // World A: play, snapshot mid-way, keep playing to the end.
        var a = new GameWorld();
        a.NewGame();
        ScriptPhase1(a);
        byte[] snapshot;
        using (var ms = new MemoryStream())
        {
            SaveCodec.WriteFile(a, ms, SaveCodec.ColonyWriterId, 1);
            snapshot = ms.ToArray();
        }
        ScriptPhase2(a);
        ulong endA = SaveCodec.WorldHash(a);

        // World B: restore the snapshot, run the same continuation.
        var b = new GameWorld();
        using (var ms = new MemoryStream(snapshot))
            SaveCodec.ReadFile(b, ms);
        ScriptPhase2(b);
        ulong endB = SaveCodec.WorldHash(b);

        Assert.Equal(endA, endB);
    }

    private static void ScriptPhase1(GameWorld world)
    {
        int z = GameConfig.SurfaceZ;
        for (int x = 22; x <= 30; x++)
            world.Designations.DesignateDig(new CellCoord(x, 32, z));
        SmokeScenario.StepSeconds(world, 40);
    }

    private static void ScriptPhase2(GameWorld world)
    {
        int z = GameConfig.SurfaceZ;
        for (int y = 30; y <= 33; y++)
            world.Designations.DesignateDig(new CellCoord(31, y, z));
        SmokeScenario.StepSeconds(world, 45);
    }
}

public class SaveRoundTripTests
{
    [Fact]
    public void SaveLoadSaveIsByteIdentical()
    {
        var world = new GameWorld();
        world.NewGame();
        int z = GameConfig.SurfaceZ;
        for (int x = 22; x <= 26; x++)
            world.Designations.DesignateDig(new CellCoord(x, 32, z));
        SmokeScenario.StepSeconds(world, 30);

        byte[] first;
        using (var ms = new MemoryStream())
        {
            SaveCodec.WriteFile(world, ms, SaveCodec.ColonyWriterId, 7);
            first = ms.ToArray();
        }

        var reloaded = new GameWorld();
        using (var ms = new MemoryStream(first))
            SaveCodec.ReadFile(reloaded, ms);

        byte[] second;
        using (var ms = new MemoryStream())
        {
            SaveCodec.WriteFile(reloaded, ms, SaveCodec.ColonyWriterId, 7);
            second = ms.ToArray();
        }

        Assert.Equal(first, second);
    }

    [Fact]
    public void ColonySavesAreRefusedInTurnBased()
    {
        var world = DriveToBattle();
        Assert.Equal(SimMode.TurnBased, world.Time.Mode);
        using var ms = new MemoryStream();
        Assert.Throws<InvalidOperationException>(
            () => SaveCodec.WriteFile(world, ms, SaveCodec.ColonyWriterId, 1));
        // The checkpoint writer may.
        using var ms2 = new MemoryStream();
        SaveCodec.WriteFile(world, ms2, SaveCodec.CheckpointWriterId, 1);
        Assert.True(ms2.Length > 0);
    }

    internal static GameWorld DriveToBattle()
    {
        var world = new GameWorld();
        world.NewGame();
        SmokeScenario.StepSeconds(world, 5);
        world.Raids.DebugTriggerRaid();
        int guard = 0;
        while (world.Time.Mode == SimMode.RealTime && guard++ < 300)
            SmokeScenario.StepSeconds(world, 1);
        Assert.Equal(SimMode.TurnBased, world.Time.Mode);
        return world;
    }
}

public class BattleCheckpointTests
{
    [Fact]
    public void CheckpointResumesMidBattleAndEvolvesIdentically()
    {
        var world = SaveRoundTripTests.DriveToBattle();

        // Play to an activation boundary (the checkpoint beat).
        PumpActivations(world, 5);
        Assert.Equal(SimMode.TurnBased, world.Time.Mode);
        ulong hashAtBeat = SaveCodec.WorldHash(world);

        byte[] checkpoint;
        using (var ms = new MemoryStream())
        {
            SaveCodec.WriteFile(world, ms, SaveCodec.CheckpointWriterId, 42);
            checkpoint = ms.ToArray();
        }

        // Load into a fresh world: the RestoredFromCheckpoint path, not RequestSwitch.
        var resumed = new GameWorld();
        using (var ms = new MemoryStream(checkpoint))
            SaveCodec.ReadFile(resumed, ms);

        Assert.Equal(SimMode.TurnBased, resumed.Time.Mode);
        Assert.Equal(hashAtBeat, SaveCodec.WorldHash(resumed));
        Assert.Equal(world.Combat.CurrentActor, resumed.Combat.CurrentActor);
        Assert.Equal(world.Combat.Encounter.RoundNumber, resumed.Combat.Encounter.RoundNumber);

        // Both worlds finish the battle identically.
        world.Combat.AutoResolve();
        resumed.Combat.AutoResolve();
        Assert.Equal(SimMode.RealTime, world.Time.Mode);
        Assert.Equal(SimMode.RealTime, resumed.Time.Mode);
        Assert.Equal(SaveCodec.WorldHash(world), SaveCodec.WorldHash(resumed));
    }

    [Fact]
    public void CheckpointServiceWritesActivationZeroAndRollingCheckpoints()
    {
        string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hd-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var world = new GameWorld();
            var saves = new SaveService(world, dir);
            using var checkpoints = new CheckpointService(world, saves);
            world.NewGame();
            SmokeScenario.StepSeconds(world, 5);
            world.Raids.DebugTriggerRaid();
            int guard = 0;
            while (world.Time.Mode == SimMode.RealTime && guard++ < 300)
                SmokeScenario.StepSeconds(world, 1);
            Assert.Equal(SimMode.TurnBased, world.Time.Mode);

            // Activation-0 checkpoint + switch-in autosave exist immediately post-swap.
            Assert.True(File.Exists(saves.CheckpointPath));
            Assert.True(File.Exists(saves.SwitchInAutosavePath));
            using (var fs = File.OpenRead(saves.CheckpointPath))
            {
                var header = SaveCodec.ReadHeader(fs);
                Assert.Equal(SaveCodec.CheckpointWriterId, header.WriterId);
                Assert.Equal(SimMode.TurnBased, header.Mode);
            }

            PumpActivations(world, 3);
            checkpoints.DrainPending();
            Assert.True(checkpoints.CheckpointsWritten >= 2);

            // Rolling checkpoint resumes at the latest beat.
            var resumed = new GameWorld();
            var resumedSaves = new SaveService(resumed, dir);
            Assert.True(resumedSaves.TryLoadLatest(out string loaded));
            Assert.EndsWith("battle.checkpoint", loaded);
            Assert.Equal(SimMode.TurnBased, resumed.Time.Mode);
            Assert.Equal(SaveCodec.WorldHash(world), SaveCodec.WorldHash(resumed));

            // Battle end: checkpoint deleted, battle-end autosave becomes the resume point.
            world.Combat.AutoResolve();
            Assert.Equal(SimMode.RealTime, world.Time.Mode);
            Assert.False(File.Exists(saves.CheckpointPath));
            Assert.True(File.Exists(saves.BattleEndAutosavePath));
            var final = new GameWorld();
            var finalSaves = new SaveService(final, dir);
            Assert.True(finalSaves.TryLoadLatest(out string latest));
            Assert.EndsWith("auto-battleend.save", latest);
            Assert.Equal(SimMode.RealTime, final.Time.Mode);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    private static void PumpActivations(GameWorld world, ulong target)
    {
        int guard = 0;
        while (world.Time.Mode == SimMode.TurnBased &&
               world.Combat.Encounter.ActivationCounter < target && guard++ < 500)
        {
            if (world.Combat.IsPlayerTurn) world.Combat.AutoPlayColonist();
            else world.Combat.StepRaiderAi();
        }
    }
}

public class ModeSwitchTests
{
    [Fact]
    public void NormalizationIsDeterministicLowestIdKeepsCell()
    {
        var (a, keptA, nudgedA) = RunNormalization();
        var (b, keptB, nudgedB) = RunNormalization();

        // Lowest id keeps the contested cell; others nudge deterministically.
        Assert.Equal(keptA.cell, keptA.original);
        Assert.NotEqual(nudgedA.cell, nudgedA.original);
        Assert.Equal(keptA.cell, keptB.cell);
        Assert.Equal(nudgedA.cell, nudgedB.cell);

        // TB invariant: unique occupancy for every active unit.
        var seen = new HashSet<CellCoord>();
        for (int i = 0; i < a.Colonists.Count; i++)
            if (a.Colonists[i].IsActive)
                Assert.True(seen.Add(a.Colonists[i].Cell), $"overlap at {a.Colonists[i].Cell}");
    }

    private static (GameWorld world,
        (CellCoord original, CellCoord cell) kept,
        (CellCoord original, CellCoord cell) nudged) RunNormalization()
    {
        var world = new GameWorld();
        world.NewGame();
        // Stack two colonists on one cell (advisory overlap is legal in RT).
        var first = world.Colonists[0].Id;
        var second = world.Colonists[1].Id;
        var contested = world.Colonists[0].Cell;
        using (world.Gate.Open())
            world.Colonists.SetCell(second, contested);

        // Spawn a raider so the encounter is non-trivial, then switch immediately.
        using (world.Gate.Open())
            world.Raiders.Add(world.Ids, RaiderKind.Melee, new CellCoord(1, 1, GameConfig.SurfaceZ), 1);
        world.Time.RequestSwitch(new SwitchTransitionData { RaidIndex = 1, Reason = "test", FocusCell = contested });
        Assert.Equal(SimMode.TurnBased, world.Time.Mode);

        return (world,
            (contested, world.Colonists.Get(first).Cell),
            (contested, world.Colonists.Get(second).Cell));
    }

    [Fact]
    public void ReconcileReapsAllRaidersIncludingLiveOnes()
    {
        var world = SaveRoundTripTests.DriveToBattle();
        Assert.True(world.Raiders.AliveCount() > 0);
        world.Combat.AutoResolve();
        Assert.Equal(SimMode.RealTime, world.Time.Mode);
        Assert.Equal(0, world.Raiders.Count); // ALL reaped — dead, routed, and live alike
        Assert.NotNull(world.LastOutcome);
        Assert.False(world.OutcomeInbox.HasReport); // inbox drained exactly once
    }

    [Fact]
    public void WritersEnforceModeAndWindow()
    {
        var world = new GameWorld();
        world.NewGame();
        var colonist = world.Colonists[0].Id;
        INeedsWriter needs = world.Writers;
        IColonistCombatWriter combat = world.Writers;

        // Outside any window: refused.
        Assert.Throws<InvalidOperationException>(() => needs.SetNeeds(colonist, 1, 1, 1));
        // Inside a window but wrong authority: refused.
        using (world.Gate.Open())
            Assert.Throws<InvalidOperationException>(() => combat.ApplyDamage(colonist, 5));
    }

    [Fact]
    public void DownedRuleZeroHpDownsStruckWhileDownedKills()
    {
        var world = SaveRoundTripTests.DriveToBattle();
        var colonist = world.Colonists[0].Id;
        IColonistCombatWriter combat = world.Writers;
        using (world.Gate.Open())
        {
            combat.ApplyDamage(colonist, 500);
            Assert.True(world.Colonists.Get(colonist).IsDowned);
            Assert.False(world.Colonists.Get(colonist).IsDead);
            combat.ApplyDamage(colonist, 1); // struck while downed
            Assert.True(world.Colonists.Get(colonist).IsDead);
        }
    }
}

public class SmokeTests
{
    [Fact]
    public void FullSmokeScenarioRunsCleanAndHashStable()
    {
        var log1 = new List<string>();
        ulong h1 = SmokeScenario.Run(new GameWorld(), log1.Add);
        ulong h2 = SmokeScenario.Run(new GameWorld());
        Assert.Equal(h1, h2);
        Assert.Contains(log1, l => l.StartsWith("battle joined"));
        Assert.Contains(log1, l => l.StartsWith("battle over"));
    }
}
