using System.IO.Compression;
using Hollowdeep.Core.Combat;
using Hollowdeep.Core.Sim;
using Hollowdeep.Core.Terrain;

namespace Hollowdeep.Core.Save;

public readonly record struct SaveHeader(int Version, byte WriterId, long SaveOrder, SimMode Mode);

/// <summary>
/// One binary codec for colony saves and battle checkpoints (ADR-0004): header +
/// gzip payload, schema version, material manifest for stable ids, EntityId counter
/// serialized, deterministic iteration orders throughout. Derived state (occupancy,
/// regions) contributes zero bytes and rebuilds on load. .NET's GZipStream writes no
/// timestamp, so identical payloads yield identical files.
/// </summary>
public static class SaveCodec
{
    public const uint Magic = 0x48445356; // "HDSV"
    public const int Version = 1;
    public const byte ColonyWriterId = 0;
    public const byte CheckpointWriterId = 1;

    // ---- payload ----

    public static void WritePayload(GameWorld world, Stream stream)
    {
        using var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);

        // Material manifest: stable name→id mapping guards against enum drift.
        w.Write(MaterialCatalog.All.Count);
        foreach (var m in MaterialCatalog.All) { w.Write((byte)m.Id); w.Write(m.Name); }

        world.Rng.WriteTo(w);
        w.Write(world.Ids.Counter);

        w.Write((byte)world.Time.Mode);
        w.Write(world.Time.TurnIndex);
        w.Write(world.Time.TickSequence);
        w.Write(world.Time.Speed);
        w.Write(world.Terrain.Revision.Value);

        world.Terrain.WriteTo(w);
        world.Colonists.WriteTo(w);
        world.Raiders.WriteTo(w);
        world.Items.WriteTo(w);
        world.Doors.WriteTo(w);

        world.Stats.WriteTo(w);
        world.Designations.WriteTo(w);
        world.Stockpiles.WriteTo(w);
        world.Farms.WriteTo(w);
        world.Jobs.WriteTo(w);
        world.Raids.WriteTo(w);
        world.Scars.WriteTo(w);

        // Combat side tables ride ONLY in TurnBased saves — i.e. battle checkpoints.
        if (world.Time.Mode == SimMode.TurnBased)
            world.Combat.Encounter.WriteTo(w);
    }

    /// <summary>Restores a payload inside the load window; rebuilds derived state after.</summary>
    public static void ReadPayload(GameWorld world, Stream stream)
    {
        world.Gate.AssertOpen();
        using var r = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);

        int mats = r.ReadInt32();
        for (int i = 0; i < mats; i++)
        {
            var id = (MaterialId)r.ReadByte();
            string name = r.ReadString();
            if (i >= MaterialCatalog.All.Count || MaterialCatalog.All[i].Id != id || MaterialCatalog.All[i].Name != name)
                throw new InvalidDataException($"Material manifest mismatch at index {i}: save has {name}({(byte)id}).");
        }

        world.Rng.ReadFrom(r);
        world.Ids.Restore(r.ReadInt64());

        var mode = (SimMode)r.ReadByte();
        int turnIndex = r.ReadInt32();
        ulong tickSequence = r.ReadUInt64();
        int speed = r.ReadInt32();
        ulong terrainRevision = r.ReadUInt64();

        world.Time.RestoreState(mode, turnIndex, tickSequence);
        world.Time.Speed = speed;

        world.Terrain.ReadFrom(r);
        world.Terrain.Revision.Restore(terrainRevision);

        world.Occupancy.Clear();
        world.Colonists.ReadFrom(r);
        world.Raiders.ReadFrom(r);
        world.Items.ReadFrom(r);
        world.Doors.ReadFrom(r);

        world.Stats.ReadFrom(r);
        world.Designations.ReadFrom(r);
        world.Stockpiles.ReadFrom(r);
        world.Farms.ReadFrom(r);
        world.Jobs.ReadFrom(r);
        world.Raids.ReadFrom(r);
        world.Scars.ReadFrom(r);

        if (mode == SimMode.TurnBased)
            world.Combat.Encounter.ReadFrom(r);
        else
            world.Combat.Encounter.Clear();

        // Derived state rebuilds; occupancy filters dead/downed units (ADR-0004).
        world.Colonists.RebuildOccupancy();
        world.Raiders.RebuildOccupancy();
        world.Regions.MarkAllDirty();
    }

    // ---- file format ----

    public static void WriteFile(GameWorld world, Stream stream, byte writerId, long saveOrder)
    {
        if (world.Time.Mode == SimMode.TurnBased && writerId != CheckpointWriterId)
            throw new InvalidOperationException(
                "Refused: only the checkpoint writer may produce TurnBased saves (ADR-0004).");
        using var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        w.Write(Magic);
        w.Write(Version);
        w.Write(writerId);
        w.Write(saveOrder);
        w.Write((byte)world.Time.Mode);
        using var gzip = new GZipStream(stream, CompressionLevel.Fastest, leaveOpen: true);
        WritePayload(world, gzip);
    }

    public static SaveHeader ReadHeader(Stream stream)
    {
        using var r = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        if (r.ReadUInt32() != Magic) throw new InvalidDataException("Not a Hollowdeep save.");
        int version = r.ReadInt32();
        if (version != Version) throw new InvalidDataException($"Unsupported save version {version}.");
        byte writerId = r.ReadByte();
        long order = r.ReadInt64();
        var mode = (SimMode)r.ReadByte();
        return new SaveHeader(version, writerId, order, mode);
    }

    /// <summary>Full load. TurnBased files re-enter the battle via RestoredFromCheckpoint — never RequestSwitch.</summary>
    public static SaveHeader ReadFile(GameWorld world, Stream stream)
    {
        var header = ReadHeader(stream);
        if (header.Mode == SimMode.TurnBased && header.WriterId != CheckpointWriterId)
            throw new InvalidDataException("TurnBased save not produced by the checkpoint writer.");
        using (var _ = world.Gate.Open())
        using (var gzip = new GZipStream(stream, CompressionMode.Decompress, leaveOpen: true))
            ReadPayload(world, gzip);
        return header;
    }

    // ---- hashing ----

    /// <summary>FNV-1a 64 over the uncompressed payload: the world hash used by determinism tests.</summary>
    public static ulong WorldHash(GameWorld world)
    {
        using var ms = new MemoryStream(1 << 20);
        WritePayload(world, ms);
        ulong hash = 14695981039346656037UL;
        var buf = ms.GetBuffer();
        int len = (int)ms.Length;
        for (int i = 0; i < len; i++)
        {
            hash ^= buf[i];
            hash *= 1099511628211UL;
        }
        return hash;
    }
}
