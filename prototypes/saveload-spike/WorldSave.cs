// PROTOTYPE - NOT FOR PRODUCTION
// Question: does the whole world survive a save/load round-trip — terrain + entities + time
//   authority + id source — with derived state rebuilt and combat state structurally absent?
// Date: 2026-07-26

namespace SaveLoadSpike;

public sealed class SaveCorruptException(string message) : Exception(message);

/// <summary>
/// The cross-cutting serialization contract, executable: plain data, stable IDs only,
/// derived state never serialized, schema version, CI round-trip.
/// </summary>
public static class WorldSave
{
    public const int SchemaVersion = 1;
    private const uint Magic = 0x484C4457;      // "HLDW"

    // ---------------------------------------------------------------- write
    public static byte[] Save(GameWorld w)
    {
        // CD-9: saves happen only in RealTime. A TurnBased save is corrupt, not a supported state.
        if (w.Manager.CurrentMode != TimeAuthorityMode.RealTime)
            throw new InvalidOperationException("CD-9: refusing to save inside a battle.");

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        bw.Write(Magic);
        bw.Write(SchemaVersion);

        // --- time authority: {Mode, TurnIndex, TickSequence} ---
        TimeAuthoritySnapshot t = w.Manager.Snapshot();
        bw.Write((byte)t.Mode);
        bw.Write(t.TurnIndex);
        bw.Write(t.TickSequence);

        // --- entity id source: the ENTIRE non-reuse guarantee ---
        bw.Write(w.Ids.Counter);

        // --- terrain: bounds, chunk size, raw cells, material manifest (stable string keys) ---
        TerrainSnapshot ts = w.World.Snapshot();
        bw.Write(ts.Bounds.SizeX); bw.Write(ts.Bounds.SizeY); bw.Write(ts.Bounds.Layers);
        bw.Write(ts.ChunkSize);
        bw.Write(ts.MaterialManifest.Count);
        foreach ((string key, ushort id) in ts.MaterialManifest.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        { bw.Write(key); bw.Write(id); }
        bw.Write(ts.Chunks.Length);
        foreach (TerrainCell[] chunk in ts.Chunks)
        {
            bw.Write(chunk.Length);
            foreach (TerrainCell c in chunk)
            {
                bw.Write(c.FloorTypeId); bw.Write(c.WallTypeId); bw.Write(c.WallHp);
                bw.Write(c.StyleId); bw.Write(c.Flags);
            }
        }

        // --- colonists (ascending id, matching store order) ---
        bw.Write(w.Colonists.All.Count);
        foreach (ColonistRecord c in w.Colonists.All)
        {
            bw.Write(c.Id.Value);
            bw.Write(c.Cell.X); bw.Write(c.Cell.Y); bw.Write(c.Cell.Z);
            bw.Write(c.Hp); bw.Write(c.IsDead); bw.Write(c.BattlesSurvived);
            bw.Write(c.Name ?? string.Empty);
        }

        // --- doors ---
        bw.Write(w.Doors.All.Count);
        foreach (DoorRecord d in w.Doors.All)
        {
            bw.Write(d.Id.Value);
            bw.Write(d.Cell.X); bw.Write(d.Cell.Y); bw.Write(d.Cell.Z);
            bw.Write(d.IsOpen); bw.Write(d.Hp); bw.Write(d.IsBroken);
        }

        // --- item stacks + their reservations (Stockpile & Hauling-owned table) ---
        bw.Write(w.Items.All.Count);
        foreach (ItemRecord i in w.Items.All)
        {
            bw.Write(i.Id.Value);
            bw.Write(i.Cell.X); bw.Write(i.Cell.Y); bw.Write(i.Cell.Z);
            bw.Write(i.MaterialKey); bw.Write(i.Quantity);
        }
        var reservations = w.Reservations.AllOrdered().ToList();
        bw.Write(reservations.Count);
        foreach ((EntityId stack, EntityId holder, int qty) in reservations)
        { bw.Write(stack.Value); bw.Write(holder.Value); bw.Write(qty); }

        // NOT WRITTEN, deliberately (derived or combat-transient):
        //   UnitOccupancyIndex, EntityDirectory, region/path caches, every Revision counter,
        //   RaiderStore (encounter-scoped — a valid save has no live encounter),
        //   combat side tables, EncounterOutcomeInbox.
        // CD-9's "don't serialize battles" needs no code: there is nothing to skip.

        bw.Flush();
        return ms.ToArray();
    }

    // ---------------------------------------------------------------- read
    public static void Load(GameWorld w, byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var br = new BinaryReader(ms);

        try
        {
            if (br.ReadUInt32() != Magic) throw new SaveCorruptException("not a Hollowdeep save");
            int schema = br.ReadInt32();
            if (schema != SchemaVersion)
                throw new SaveCorruptException($"schema version {schema} != {SchemaVersion}");

            var mode = (TimeAuthorityMode)br.ReadByte();
            if (mode != TimeAuthorityMode.RealTime)
                throw new SaveCorruptException("CD-9: a save whose Mode is TurnBased is corrupt.");
            int turnIndex = br.ReadInt32();
            ulong tickSequence = br.ReadUInt64();

            long idCounter = br.ReadInt64();

            int sx = br.ReadInt32(), sy = br.ReadInt32(), layers = br.ReadInt32();
            int chunkSize = br.ReadInt32();
            int manifestCount = br.ReadInt32();
            var manifest = new Dictionary<string, ushort>(manifestCount);
            for (int i = 0; i < manifestCount; i++)
            {
                string key = br.ReadString();
                manifest[key] = br.ReadUInt16();
            }
            int chunkCount = br.ReadInt32();
            var chunks = new TerrainCell[chunkCount][];
            for (int i = 0; i < chunkCount; i++)
            {
                int len = br.ReadInt32();
                var arr = new TerrainCell[len];
                for (int j = 0; j < len; j++)
                    arr[j] = new TerrainCell
                    {
                        FloorTypeId = br.ReadUInt16(), WallTypeId = br.ReadUInt16(), WallHp = br.ReadUInt16(),
                        StyleId = br.ReadByte(), Flags = br.ReadByte(),
                    };
                chunks[i] = arr;
            }

            int colonistCount = br.ReadInt32();
            var colonists = new List<ColonistRecord>(colonistCount);
            for (int i = 0; i < colonistCount; i++)
                colonists.Add(new ColonistRecord
                {
                    Id = new EntityId(br.ReadInt64()),
                    Cell = new CellCoord(br.ReadInt32(), br.ReadInt32(), br.ReadInt32()),
                    Hp = br.ReadInt32(), IsDead = br.ReadBoolean(), BattlesSurvived = br.ReadInt32(),
                    Name = br.ReadString(),
                });

            int doorCount = br.ReadInt32();
            var doors = new List<DoorRecord>(doorCount);
            for (int i = 0; i < doorCount; i++)
                doors.Add(new DoorRecord
                {
                    Id = new EntityId(br.ReadInt64()),
                    Cell = new CellCoord(br.ReadInt32(), br.ReadInt32(), br.ReadInt32()),
                    IsOpen = br.ReadBoolean(), Hp = br.ReadInt32(), IsBroken = br.ReadBoolean(),
                });

            int itemCount = br.ReadInt32();
            var items = new List<ItemRecord>(itemCount);
            for (int i = 0; i < itemCount; i++)
                items.Add(new ItemRecord
                {
                    Id = new EntityId(br.ReadInt64()),
                    Cell = new CellCoord(br.ReadInt32(), br.ReadInt32(), br.ReadInt32()),
                    MaterialKey = br.ReadString(), Quantity = br.ReadInt32(),
                });

            int resCount = br.ReadInt32();
            var reservations = new List<(EntityId, EntityId, int)>(resCount);
            for (int i = 0; i < resCount; i++)
                reservations.Add((new EntityId(br.ReadInt64()), new EntityId(br.ReadInt64()), br.ReadInt32()));

            // ---- apply, inside the load window ----
            w.Manager.Restore(new TimeAuthoritySnapshot(mode, turnIndex, tickSequence));
            w.Ids.Counter = idCounter;
            w.World.Restore(new TerrainSnapshot
            {
                Bounds = new WorldBounds(sx, sy, layers), ChunkSize = chunkSize,
                Chunks = chunks, MaterialManifest = manifest,
            });
            w.RestoreEntities(colonists, doors, items, reservations);
        }
        catch (EndOfStreamException e)
        {
            throw new SaveCorruptException("truncated save file: " + e.Message);
        }
    }
}
