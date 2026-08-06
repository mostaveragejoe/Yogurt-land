// PROTOTYPE - NOT FOR PRODUCTION
// Question: ADR-0003 criterion 3 + ADR-0001 criterion 2 + cross-cutting contract #2,
//   made executable. Concept doc: "does the data model survive a save/load round-trip?"
// Date: 2026-07-26

using System.Security.Cryptography;

namespace SaveLoadSpike;

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
        catch (Exception e) { Check(false, $"{name} (wrong exception {e.GetType().Name})"); }
    }

    private static string Hash(byte[] b) => Convert.ToHexString(SHA256.HashData(b))[..16];

    /// <summary>A world with enough state that a lazy serializer would lose something.</summary>
    public static GameWorld MakePopulatedWorld(int seed = 11)
    {
        var w = new GameWorld();
        var rng = new Random(seed);
        using (w.Manager.Window.Open())
        {
            for (int i = 0; i < 40; i++)
                w.World.SetWall(new CellCoord(rng.Next(64), rng.Next(64), rng.Next(8)), w.Dirt, (byte)rng.Next(4));
            for (int i = 0; i < 10; i++)
                w.World.ApplyWallDamage(new CellCoord(rng.Next(64), rng.Next(64), rng.Next(8)), rng.Next(1, 20));
            w.World.SetTerrainJobClaim(new CellCoord(5, 5, 0), true);

            for (int i = 0; i < 10; i++)
                w.Colonists.Spawn($"Colonist{i}", new CellCoord(10 + i, 10, 0), 10 - i % 3);
            w.Colonists.ApplyDamage(new EntityId(3), 4, TimeAuthorityMode.RealTime);
            w.Colonists.IncrementBattlesSurvived(new EntityId(1));
            w.Colonists.IncrementBattlesSurvived(new EntityId(1));

            for (int i = 0; i < 6; i++) w.Doors.Spawn(new CellCoord(20 + i, 20, 0));
            w.Doors.SetOpen(new CellCoord(21, 20, 0), true);

            for (int i = 0; i < 12; i++)
                w.Items.SpawnStack(i % 2 == 0 ? "dirt" : "granite", 5 + i, new CellCoord(30 + i, 30, 1));
            w.Reservations.Reserve(new EntityId(20), new EntityId(2), 3);
            w.Reservations.Reserve(new EntityId(21), new EntityId(4), 7);
        }
        w.RebuildDerived();
        return w;
    }

    public static int RunAll()
    {
        Console.WriteLine("\n=== SAVE/LOAD: the serialization contract, executable ===");
        RoundTrip();
        IdNonReuse();
        DerivedStateRebuild();
        Cd9NoCombatState();
        CatalogEvolution();
        CorruptionHandling();
        DeterminismAfterLoad();
        Console.WriteLine($"\n  {_pass} passed, {_fail} failed");
        return _fail;
    }

    private static void RoundTrip()
    {
        GameWorld a = MakePopulatedWorld();
        byte[] save1 = WorldSave.Save(a);

        var b = new GameWorld();
        WorldSave.Load(b, save1);
        byte[] save2 = WorldSave.Save(b);

        Check(save1.SequenceEqual(save2), $"save -> load -> save is BYTE-IDENTICAL ({Hash(save1)})");

        // Terrain equality, cell by cell.
        bool terrainSame = true;
        for (int z = 0; z < 8 && terrainSame; z++)
            for (int y = 0; y < 64 && terrainSame; y++)
                for (int x = 0; x < 64; x++)
                {
                    TerrainCell ca = a.World.GetCell(new CellCoord(x, y, z)), cb = b.World.GetCell(new CellCoord(x, y, z));
                    if (ca.FloorTypeId != cb.FloorTypeId || ca.WallTypeId != cb.WallTypeId ||
                        ca.WallHp != cb.WallHp || ca.StyleId != cb.StyleId || ca.Flags != cb.Flags)
                    { terrainSame = false; break; }
                }
        Check(terrainSame, "every terrain cell survives, including HP, style and the job-claim flag");

        Check(a.Colonists.All.Count == b.Colonists.All.Count &&
              a.Colonists.All.Zip(b.Colonists.All).All(p =>
                  p.First.Id == p.Second.Id && p.First.Cell == p.Second.Cell && p.First.Hp == p.Second.Hp &&
                  p.First.IsDead == p.Second.IsDead && p.First.BattlesSurvived == p.Second.BattlesSurvived &&
                  p.First.Name == p.Second.Name),
              "colonists round-trip with id, cell, hp, death flag, BattlesSurvived and name");
        Check(a.Doors.All.Count == b.Doors.All.Count &&
              a.Doors.All.Zip(b.Doors.All).All(p => p.First.Id == p.Second.Id && p.First.Cell == p.Second.Cell &&
                                                    p.First.IsOpen == p.Second.IsOpen && p.First.Hp == p.Second.Hp),
              "doors round-trip including open/broken state");
        Check(a.Items.All.Count == b.Items.All.Count &&
              a.Items.All.Zip(b.Items.All).All(p => p.First.Id == p.Second.Id && p.First.Quantity == p.Second.Quantity &&
                                                    p.First.MaterialKey == p.Second.MaterialKey),
              "item stacks round-trip by STABLE material key, not runtime id");
        Check(a.Reservations.AllOrdered().SequenceEqual(b.Reservations.AllOrdered()),
              "stack reservations round-trip (Stockpile & Hauling's table)");
        Check(a.Manager.TickSequence == b.Manager.TickSequence && a.Manager.TurnIndex == b.Manager.TurnIndex,
              "TickSequence and TurnIndex are preserved (ADR-0001 criterion 2)");
    }

    private static void IdNonReuse()
    {
        GameWorld a = MakePopulatedWorld();
        long counterBefore = a.Ids.Counter;
        byte[] save = WorldSave.Save(a);

        var b = new GameWorld();
        WorldSave.Load(b, save);
        Check(b.Ids.Counter == counterBefore, "the EntityIdSource counter is serialized — the whole non-reuse guarantee");

        EntityId fresh;
        using (b.Manager.Window.Open()) fresh = b.Colonists.Spawn("PostLoad", new CellCoord(1, 1, 0), 10);
        bool collides = a.Colonists.All.Any(c => c.Id == fresh) || a.Doors.All.Any(d => d.Id == fresh) ||
                        a.Items.All.Any(i => i.Id == fresh);
        Check(!collides && fresh.Value == counterBefore,
              "an entity spawned AFTER load gets a fresh id that never collides with a pre-save one");

        // The classic bug this prevents: a dangling reference must resolve to nothing, not to a stranger.
        Check(b.Directory.KindOf(new EntityId(99_999)) == EntityKind.None,
              "an unknown/dangling id resolves to nothing — callers handle 'gone'");
    }

    private static void DerivedStateRebuild()
    {
        GameWorld a = MakePopulatedWorld();
        byte[] save = WorldSave.Save(a);
        var b = new GameWorld();

        int occBefore = b.OccupancyRebuildCount, dirBefore = b.DirectoryRebuildCount;
        WorldSave.Load(b, save);
        Check(b.OccupancyRebuildCount == occBefore + 1 && b.DirectoryRebuildCount == dirBefore + 1,
              "occupancy index and entity directory are REBUILT on load");

        Check(b.Occupancy.Verify(b.Colonists, b.Raiders, out string detail), $"rebuilt occupancy matches positions ({detail})");
        Check(b.Directory.Count == b.Colonists.All.Count + b.Doors.All.Count + b.Items.All.Count,
              "directory covers every entity kind after rebuild");
        Check(b.Directory.KindOf(b.Colonists.All[0].Id) == EntityKind.Colonist &&
              b.Directory.KindOf(b.Doors.All[0].Id) == EntityKind.Door &&
              b.Directory.KindOf(b.Items.All[0].Id) == EntityKind.Item,
              "directory resolves each id to the right kind");

        // Derived state must contribute ZERO bytes: corrupting it cannot change the save.
        GameWorld c = MakePopulatedWorld();
        byte[] before = WorldSave.Save(c);
        c.Occupancy.Clear();
        c.Directory.Clear();
        byte[] after = WorldSave.Save(c);
        Check(before.SequenceEqual(after),
              "wiping ALL derived state does not change a single byte of the save (never serialized)");
    }

    private static void Cd9NoCombatState()
    {
        GameWorld a = MakePopulatedWorld();
        byte[] clean = WorldSave.Save(a);

        // Fabricate combat-transient state, then prove the save is unchanged.
        using (a.Manager.Window.Open())
        {
            a.Raiders.Spawn(new CellCoord(2, 2, 0), 8);
            a.Raiders.Spawn(new CellCoord(3, 3, 0), 8);
        }
        a.Inbox.Deposit(new EncounterOutcomeReport(1, [], [], true));
        byte[] withCombat = WorldSave.Save(a);

        // The save must gain NO records. It does legitimately differ in exactly one field: the
        // EntityIdSource counter, which the raider spawns advanced — and which MUST advance,
        // because ids are never reused even by entities that existed only inside an encounter.
        const int counterOffset = 4 + 4 + 1 + 4 + 8;      // magic, schema, mode, turnIndex, tickSequence
        bool sameLength = clean.Length == withCombat.Length;
        bool onlyCounterDiffers = sameLength;
        for (int i = 0; i < clean.Length && onlyCounterDiffers; i++)
            if (clean[i] != withCombat[i] && (i < counterOffset || i >= counterOffset + 8))
                onlyCounterDiffers = false;
        Check(sameLength && onlyCounterDiffers,
              "raiders and the outcome inbox add ZERO records — CD-9 needs no code, there is nothing to skip");
        Check(BitConverter.ToInt64(withCombat, counterOffset) > BitConverter.ToInt64(clean, counterOffset),
              "...but the id counter DID advance: non-reuse holds across the encounter boundary too");

        // And saving inside a battle is refused outright.
        GameWorld b = MakePopulatedWorld();
        b.Manager.RequestSwitch(TimeAuthorityMode.TurnBased,
            new SwitchTransitionData(1, SwitchTriggerReason.Breach, [], []));
        Throws<InvalidOperationException>(() => WorldSave.Save(b), "CD-9: saving inside a battle is refused");
    }

    private static void CatalogEvolution()
    {
        // The retrofit this contract exists to prevent: a patch inserts a material and every
        // saved wall silently becomes a different rock.
        GameWorld a = MakePopulatedWorld();
        byte[] save = WorldSave.Save(a);
        int dirtWallsBefore = CountWalls(a, "dirt");

        var b = new GameWorld();
        // Simulate a later build whose catalog registers an extra material FIRST, shifting ids.
        b.Catalog.Register("marble", 2, 200);
        b.Catalog.Register("granite", 2, 120);
        WorldSave.Load(b, save);
        Check(CountWalls(b, "dirt") == dirtWallsBefore && dirtWallsBefore > 0,
              "ids REMAP through the material manifest — walls stay the same rock after a catalog change");
    }

    private static int CountWalls(GameWorld w, string key)
    {
        if (!TryFindId(w, key, out ushort id)) return -1;
        int n = 0;
        for (int z = 0; z < 8; z++)
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                    if (w.World.GetCell(new CellCoord(x, y, z)).WallTypeId == id) n++;
        return n;
    }

    private static bool TryFindId(GameWorld w, string key, out ushort id) => w.Catalog.TryId(key, out id);

    private static void CorruptionHandling()
    {
        GameWorld a = MakePopulatedWorld();
        byte[] save = WorldSave.Save(a);

        byte[] truncated = save[..(save.Length / 2)];
        Throws<SaveCorruptException>(() => WorldSave.Load(new GameWorld(), truncated),
            "a truncated save fails loudly, not silently");

        byte[] badMagic = (byte[])save.Clone();
        badMagic[0] ^= 0xFF;
        Throws<SaveCorruptException>(() => WorldSave.Load(new GameWorld(), badMagic),
            "a foreign/garbage file is rejected by magic number");

        byte[] badSchema = (byte[])save.Clone();
        badSchema[4] = 99;
        Throws<SaveCorruptException>(() => WorldSave.Load(new GameWorld(), badSchema),
            "a future schema version is rejected rather than misread");

        // An unknown material key must fail loudly (ADR-0002's rule), not silently re-materialize.
        // Save from a build that has an EXTRA material; load into one that has never heard of it
        // — the modded-save / downgraded-build case.
        var modded = new GameWorld();
        ushort exotic = modded.Catalog.Register("exotic_crystal", 3, 500);
        using (modded.Manager.Window.Open()) modded.World.SetWall(new CellCoord(4, 4, 0), exotic, 0);
        byte[] moddedSave = WorldSave.Save(modded);
        Throws<InvalidOperationException>(() => WorldSave.Load(new GameWorld(), moddedSave),
            "a save naming a material this build does not know fails LOUDLY, not silently");
    }

    private static void DeterminismAfterLoad()
    {
        // The strongest test: a world that has been saved and reloaded must evolve IDENTICALLY
        // to one that never left memory. If anything load-bearing is missing from the save,
        // the two diverge.
        GameWorld control = MakePopulatedWorld();
        GameWorld saved = MakePopulatedWorld();
        byte[] save = WorldSave.Save(saved);
        var reloaded = new GameWorld();
        WorldSave.Load(reloaded, save);

        static string Advance(GameWorld w)
        {
            var rng = new Random(4242);                 // identical input sequence for both
            using (w.Manager.Window.Open())
                for (int i = 0; i < 200; i++)
                {
                    var c = new CellCoord(rng.Next(64), rng.Next(64), rng.Next(8));
                    switch (i % 3)
                    {
                        case 0: w.World.SetWall(c, w.Dirt, (byte)rng.Next(4)); break;
                        case 1: w.World.ApplyWallDamage(c, rng.Next(1, 40)); break;
                        case 2: w.Colonists.SetCell(new EntityId(1 + i % 10), c); break;
                    }
                }
            return Convert.ToHexString(SHA256.HashData(WorldSave.Save(w)))[..16];
        }

        string a = Advance(control);
        string b = Advance(reloaded);
        Check(a == b, $"a reloaded world evolves IDENTICALLY to one that never left memory ({a} == {b})");
    }
}
