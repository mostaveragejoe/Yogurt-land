// PROTOTYPE - NOT FOR PRODUCTION
// Question: Does ADR-0002's terrain data model (chunked AoS of 8-byte cells behind
//   a single TerrainWorld facade) hold up on memory, allocation, sweep cost, and
//   chunk-rebuild extraction — and is 32x32 the right chunk size?
// Date: 2026-07-25

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SaveLoadSpike;

public readonly record struct CellCoord(int X, int Y, int Z);
public readonly record struct ChunkCoord(int X, int Y, int Z);

/// <summary>ADR-0002 packed cell. Layout is contract: Snapshot byte-identity depends on it.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct TerrainCell
{
    public ushort FloorTypeId;   // 0 = no floor (void/pit)
    public ushort WallTypeId;    // 0 = no wall (open cell)
    public ushort WallHp;        // meaningful only when WallTypeId != 0
    public byte StyleId;         // 0 = natural
    public byte Flags;           // bit 0: TerrainJobClaim; bits 1-7 spare
}

public enum TerrainChangeKind : byte
{ WallPlaced, WallRemoved, WallDamaged, WallRepaired, FloorPlaced, FloorRemoved }

public readonly record struct TerrainChange(CellCoord Cell, TerrainChangeKind Kind, TerrainCell Previous);

/// <summary>ref struct: retention past Publish is a compile error (ADR-0002 rule 1).</summary>
public readonly ref struct TerrainChangeBatch
{
    public ReadOnlySpan<TerrainChange> Changes { get; }
    public TerrainChangeBatch(ReadOnlySpan<TerrainChange> changes) { Changes = changes; }
}

public interface ITerrainChangeSink
{
    void Publish(in TerrainChangeBatch batch);
    void PublishWorldReloaded();
}

public enum TerrainMutationKind : byte
{ SetWall, ClearWall, SetFloor, ClearFloor, DamageWall, RepairWall }

public readonly record struct TerrainMutation(
    CellCoord Cell, TerrainMutationKind Kind, ushort TypeId, byte StyleId, int Amount);

public enum DamageOutcome : byte { Damaged, Destroyed, NoWall }
public enum RepairOutcome : byte { Repaired, AlreadyAtMax, NoWall }
public readonly record struct DamageResult(DamageOutcome Outcome, int AppliedAmount, ushort RemainingHp);
public readonly record struct RepairResult(RepairOutcome Outcome, int AppliedAmount, ushort RemainingHp);
public readonly record struct BulkResult(bool Accepted, int FirstInvalidIndex, string? Reason)
{
    public static readonly BulkResult Ok = new(true, -1, null);
}

public readonly record struct WorldBounds(int SizeX, int SizeY, int Layers);

/// <summary>Material catalog stand-in: tier + max HP by runtime id, stable string keys for saves.</summary>
public sealed class MaterialCatalog
{
    private readonly List<string> _keys = ["<none>"];
    private readonly List<byte> _tiers = [0];
    private readonly List<ushort> _maxHp = [0];

    public ushort Register(string key, byte tier, ushort maxHp)
    {
        _keys.Add(key); _tiers.Add(tier); _maxHp.Add(maxHp);
        return (ushort)(_keys.Count - 1);
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ushort MaxHp(ushort id) => _maxHp[id];
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte Tier(ushort id) => _tiers[id];
    public string Key(ushort id) => _keys[id];
    public int Count => _keys.Count;
    public bool TryId(string key, out ushort id)
    {
        int i = _keys.IndexOf(key);
        id = (ushort)(i < 0 ? 0 : i);
        return i >= 0;
    }
}

/// <summary>ADR-0001's mutation window, modelled just enough to assert against.</summary>
public sealed class MutationWindow
{
    private int _depth;
    public bool IsOpen => _depth > 0;
    public bool PublishOnStack { get; internal set; }

    /// <summary>
    /// Returns a STRUCT scope: `using` on a known struct type does not box, so opening the
    /// window allocates nothing. (A class scope cost 24 B per dispatch — measured, and a
    /// violation of the zero-steady-state-allocation standard. Depth-counted so nesting is safe.)
    /// </summary>
    public Scope Open() { _depth++; return new Scope(this); }

    public readonly struct Scope(MutationWindow w) : IDisposable
    {
        public void Dispose() => w._depth--;
    }
}

public sealed class TerrainSnapshot
{
    public const int CurrentSchemaVersion = 1;
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public WorldBounds Bounds { get; init; }
    public int ChunkSize { get; init; }
    public required TerrainCell[][] Chunks { get; init; }
    public required Dictionary<string, ushort> MaterialManifest { get; init; }
}

/// <summary>
/// ADR-0002's chunked dense grid behind the single write facade.
/// Chunks are per-layer tiles of ChunkSize x ChunkSize x 1.
/// </summary>
public sealed class TerrainWorld
{
    private readonly TerrainCell[][] _chunks;      // [chunkIndex][cellInChunk]
    private readonly int _chunkSize, _chunkShift, _chunkMask;
    private readonly int _chunksX, _chunksY;
    private readonly WorldBounds _bounds;
    private readonly MaterialCatalog _catalog;
    private readonly ITerrainChangeSink _sink;
    private readonly MutationWindow _window;
    private TerrainChange[] _batchBuffer = new TerrainChange[256];   // pooled, reused (rule 1)

    public TerrainWorld(WorldBounds bounds, MaterialCatalog catalog, ITerrainChangeSink sink,
                        MutationWindow window, int chunkSize = 32)
    {
        if ((chunkSize & (chunkSize - 1)) != 0) throw new ArgumentException("chunk size must be a power of two");
        _bounds = bounds; _catalog = catalog; _sink = sink; _window = window;
        _chunkSize = chunkSize;
        _chunkShift = System.Numerics.BitOperations.TrailingZeroCount((uint)chunkSize);
        _chunkMask = chunkSize - 1;
        _chunksX = (bounds.SizeX + chunkSize - 1) / chunkSize;
        _chunksY = (bounds.SizeY + chunkSize - 1) / chunkSize;
        _chunks = new TerrainCell[_chunksX * _chunksY * bounds.Layers][];
        for (int i = 0; i < _chunks.Length; i++) _chunks[i] = new TerrainCell[chunkSize * chunkSize];
    }

    public int ChunkSize => _chunkSize;
    public WorldBounds Bounds => _bounds;
    public ulong Revision { get; private set; }
    public int ChunkCount => _chunks.Length;
    public long CellDataBytes => (long)_chunks.Length * _chunkSize * _chunkSize * Unsafe.SizeOf<TerrainCell>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ChunkCoord ChunkOf(CellCoord c) => new(c.X >> _chunkShift, c.Y >> _chunkShift, c.Z);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool InBounds(CellCoord c) =>
        (uint)c.X < (uint)_bounds.SizeX && (uint)c.Y < (uint)_bounds.SizeY && (uint)c.Z < (uint)_bounds.Layers;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int ChunkIndex(int cx, int cy, int z) => (z * _chunksY + cy) * _chunksX + cx;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ref TerrainCell CellRef(CellCoord c)
    {
        int ci = ChunkIndex(c.X >> _chunkShift, c.Y >> _chunkShift, c.Z);
        int oi = ((c.Y & _chunkMask) << _chunkShift) | (c.X & _chunkMask);
        return ref _chunks[ci][oi];
    }

    // ---- reads: copies or read-only spans only (rule 7) ----
    public TerrainCell GetCell(CellCoord c) => CellRef(c);
    public ReadOnlySpan<TerrainCell> GetChunkCells(ChunkCoord chunk) =>
        _chunks[ChunkIndex(chunk.X, chunk.Y, chunk.Z)];
    public int ChunksX => _chunksX;
    public int ChunksY => _chunksY;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsPassableTerrain(CellCoord c)
    { ref TerrainCell cell = ref CellRef(c); return cell.FloorTypeId != 0 && cell.WallTypeId == 0; }

    public bool IsTerrainJobClaimed(CellCoord c) => (CellRef(c).Flags & 1) != 0;

    // ---- load-window population: no per-cell events, then WorldReloaded (rule 6) ----
    public void PopulateForLoad(Action<TerrainWorld> fill)
    {
        fill(this);
        Revision++;
        _sink.PublishWorldReloaded();
    }
    /// <summary>Load-path direct write — legal only inside PopulateForLoad / Restore.</summary>
    public void LoadSetCell(CellCoord c, TerrainCell value) => CellRef(c) = value;

    // ---- writes: the only mutation path ----
    private void AssertWindow()
    {
        if (!_window.IsOpen)
            throw new InvalidOperationException("Terrain write outside the mutation window (ADR-0002 rule 5).");
        if (_window.PublishOnStack)
            throw new InvalidOperationException("Terrain write from inside Publish (bus handlers are bookkeeping only).");
    }

    private void PublishOne(CellCoord c, TerrainChangeKind kind, in TerrainCell previous)
    {
        _batchBuffer[0] = new TerrainChange(c, kind, previous);
        _window.PublishOnStack = true;
        try { _sink.Publish(new TerrainChangeBatch(_batchBuffer.AsSpan(0, 1))); }
        finally { _window.PublishOnStack = false; }
    }

    public void SetWall(CellCoord c, ushort wallTypeId, byte styleId)
    {
        AssertWindow();
        if (!InBounds(c)) throw new ArgumentOutOfRangeException(nameof(c));
        ref TerrainCell cell = ref CellRef(c);
        TerrainCell prev = cell;
        if (cell.WallTypeId == wallTypeId && cell.StyleId == styleId) return;   // no-op publishes nothing
        cell.WallTypeId = wallTypeId; cell.StyleId = styleId; cell.WallHp = _catalog.MaxHp(wallTypeId);
        Revision++;
        PublishOne(c, TerrainChangeKind.WallPlaced, prev);
    }

    public void ClearWall(CellCoord c)
    {
        AssertWindow();
        if (!InBounds(c)) throw new ArgumentOutOfRangeException(nameof(c));
        ref TerrainCell cell = ref CellRef(c);
        if (cell.WallTypeId == 0) return;
        TerrainCell prev = cell;
        cell.WallTypeId = 0; cell.WallHp = 0;
        Revision++;
        PublishOne(c, TerrainChangeKind.WallRemoved, prev);
    }

    public void SetFloor(CellCoord c, ushort floorTypeId, byte styleId)
    {
        AssertWindow();
        if (!InBounds(c)) throw new ArgumentOutOfRangeException(nameof(c));
        ref TerrainCell cell = ref CellRef(c);
        if (cell.FloorTypeId == floorTypeId) return;
        TerrainCell prev = cell;
        cell.FloorTypeId = floorTypeId; cell.StyleId = styleId;
        Revision++;
        PublishOne(c, TerrainChangeKind.FloorPlaced, prev);
    }

    public void ClearFloor(CellCoord c)
    {
        AssertWindow();
        if (!InBounds(c)) throw new ArgumentOutOfRangeException(nameof(c));
        ref TerrainCell cell = ref CellRef(c);
        if (cell.FloorTypeId == 0) return;
        TerrainCell prev = cell;
        cell.FloorTypeId = 0;
        Revision++;
        PublishOne(c, TerrainChangeKind.FloorRemoved, prev);
    }

    public DamageResult ApplyWallDamage(CellCoord c, int amount)
    {
        AssertWindow();
        ref TerrainCell cell = ref CellRef(c);
        if (cell.WallTypeId == 0) return new DamageResult(DamageOutcome.NoWall, 0, 0);
        TerrainCell prev = cell;
        int applied = Math.Min(amount, cell.WallHp);
        cell.WallHp = (ushort)(cell.WallHp - applied);
        Revision++;
        if (cell.WallHp == 0)
        {
            cell.WallTypeId = 0;
            PublishOne(c, TerrainChangeKind.WallRemoved, prev);   // destroy reports WallRemoved
            return new DamageResult(DamageOutcome.Destroyed, applied, 0);
        }
        PublishOne(c, TerrainChangeKind.WallDamaged, prev);
        return new DamageResult(DamageOutcome.Damaged, applied, cell.WallHp);
    }

    public RepairResult ApplyWallRepair(CellCoord c, int amount)   // CD-7 path
    {
        AssertWindow();
        ref TerrainCell cell = ref CellRef(c);
        if (cell.WallTypeId == 0) return new RepairResult(RepairOutcome.NoWall, 0, 0);
        ushort max = _catalog.MaxHp(cell.WallTypeId);
        if (cell.WallHp >= max) return new RepairResult(RepairOutcome.AlreadyAtMax, 0, cell.WallHp);
        TerrainCell prev = cell;
        int applied = Math.Min(amount, max - cell.WallHp);
        cell.WallHp = (ushort)(cell.WallHp + applied);
        Revision++;
        PublishOne(c, TerrainChangeKind.WallRepaired, prev);
        return new RepairResult(RepairOutcome.Repaired, applied, cell.WallHp);
    }

    /// <summary>Job bookkeeping — publishes nothing (rule 2).</summary>
    public void SetTerrainJobClaim(CellCoord c, bool value)
    {
        AssertWindow();
        ref TerrainCell cell = ref CellRef(c);
        cell.Flags = (byte)(value ? cell.Flags | 1 : cell.Flags & ~1);
        Revision++;
    }

    /// <summary>Bulk: validate-all-then-apply, order preserved, one batch (rule 3).</summary>
    public BulkResult Apply(ReadOnlySpan<TerrainMutation> mutations)
    {
        AssertWindow();
        // pass 1: validate everything, reject the whole batch on the first offender
        for (int i = 0; i < mutations.Length; i++)
        {
            var m = mutations[i];
            if (!InBounds(m.Cell)) return new BulkResult(false, i, "out of bounds");
            for (int j = 0; j < i; j++)
                if (mutations[j].Cell == m.Cell) return new BulkResult(false, i, "duplicate cell in batch");
        }
        // pass 2: apply, collecting non-no-op changes in caller order
        if (_batchBuffer.Length < mutations.Length) _batchBuffer = new TerrainChange[mutations.Length];
        int n = 0;
        for (int i = 0; i < mutations.Length; i++)
        {
            var m = mutations[i];
            ref TerrainCell cell = ref CellRef(m.Cell);
            TerrainCell prev = cell;
            switch (m.Kind)
            {
                case TerrainMutationKind.SetWall:
                    if (cell.WallTypeId == m.TypeId && cell.StyleId == m.StyleId) continue;
                    cell.WallTypeId = m.TypeId; cell.StyleId = m.StyleId; cell.WallHp = _catalog.MaxHp(m.TypeId);
                    _batchBuffer[n++] = new TerrainChange(m.Cell, TerrainChangeKind.WallPlaced, prev); break;
                case TerrainMutationKind.ClearWall:
                    if (cell.WallTypeId == 0) continue;
                    cell.WallTypeId = 0; cell.WallHp = 0;
                    _batchBuffer[n++] = new TerrainChange(m.Cell, TerrainChangeKind.WallRemoved, prev); break;
                case TerrainMutationKind.SetFloor:
                    if (cell.FloorTypeId == m.TypeId) continue;
                    cell.FloorTypeId = m.TypeId; cell.StyleId = m.StyleId;
                    _batchBuffer[n++] = new TerrainChange(m.Cell, TerrainChangeKind.FloorPlaced, prev); break;
                case TerrainMutationKind.ClearFloor:
                    if (cell.FloorTypeId == 0) continue;
                    cell.FloorTypeId = 0;
                    _batchBuffer[n++] = new TerrainChange(m.Cell, TerrainChangeKind.FloorRemoved, prev); break;
                case TerrainMutationKind.DamageWall:
                {
                    if (cell.WallTypeId == 0) continue;
                    int applied = Math.Min(m.Amount, cell.WallHp);
                    cell.WallHp = (ushort)(cell.WallHp - applied);
                    if (cell.WallHp == 0)
                    { cell.WallTypeId = 0; _batchBuffer[n++] = new TerrainChange(m.Cell, TerrainChangeKind.WallRemoved, prev); }
                    else _batchBuffer[n++] = new TerrainChange(m.Cell, TerrainChangeKind.WallDamaged, prev);
                    break;
                }
                case TerrainMutationKind.RepairWall:
                {
                    if (cell.WallTypeId == 0) continue;
                    ushort max = _catalog.MaxHp(cell.WallTypeId);
                    if (cell.WallHp >= max) continue;
                    cell.WallHp = (ushort)(cell.WallHp + Math.Min(m.Amount, max - cell.WallHp));
                    _batchBuffer[n++] = new TerrainChange(m.Cell, TerrainChangeKind.WallRepaired, prev); break;
                }
            }
        }
        Revision++;
        if (n > 0)
        {
            _window.PublishOnStack = true;
            try { _sink.Publish(new TerrainChangeBatch(_batchBuffer.AsSpan(0, n))); }
            finally { _window.PublishOnStack = false; }
        }
        return BulkResult.Ok;
    }

    // ---- serialization: stable ids at the boundary ----
    public TerrainSnapshot Snapshot()
    {
        var chunks = new TerrainCell[_chunks.Length][];
        for (int i = 0; i < _chunks.Length; i++) chunks[i] = (TerrainCell[])_chunks[i].Clone();
        var manifest = new Dictionary<string, ushort>(_catalog.Count);
        for (ushort id = 1; id < _catalog.Count; id++) manifest[_catalog.Key(id)] = id;
        return new TerrainSnapshot
        { Bounds = _bounds, ChunkSize = _chunkSize, Chunks = chunks, MaterialManifest = manifest };
    }

    /// <summary>Remaps ids through the manifest against the current catalog; unknown keys fail loudly.</summary>
    public void Restore(TerrainSnapshot snapshot)
    {
        if (snapshot.SchemaVersion != TerrainSnapshot.CurrentSchemaVersion)
            throw new InvalidOperationException("schema version mismatch");
        if (snapshot.ChunkSize != _chunkSize || snapshot.Chunks.Length != _chunks.Length)
            throw new InvalidOperationException("layout mismatch");

        var remap = new ushort[snapshot.MaterialManifest.Count + 1];
        foreach ((string key, ushort oldId) in snapshot.MaterialManifest)
        {
            if (!_catalog.TryId(key, out ushort newId))
                throw new InvalidOperationException($"unknown material key in save: '{key}'");
            if (oldId >= remap.Length) Array.Resize(ref remap, oldId + 1);
            remap[oldId] = newId;
        }
        for (int i = 0; i < _chunks.Length; i++)
        {
            TerrainCell[] src = snapshot.Chunks[i], dst = _chunks[i];
            for (int j = 0; j < dst.Length; j++)
            {
                TerrainCell cell = src[j];
                if (cell.FloorTypeId != 0) cell.FloorTypeId = remap[cell.FloorTypeId];
                if (cell.WallTypeId != 0) cell.WallTypeId = remap[cell.WallTypeId];
                dst[j] = cell;
            }
        }
        Revision++;
        _sink.PublishWorldReloaded();
    }
}
