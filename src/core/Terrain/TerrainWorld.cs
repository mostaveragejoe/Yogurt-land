using Hollowdeep.Core.Primitives;
using Hollowdeep.Core.Sim;

namespace Hollowdeep.Core.Terrain;

public struct TerrainChange
{
    public CellCoord Cell;
    public TerrainCell Previous;
    public TerrainCell Current;
}

/// <summary>
/// Pooled batch of terrain changes with previous-state capture. Handlers copy
/// primitives out synchronously and never retain the batch (ADR-0002).
/// </summary>
public sealed class TerrainChangeBatch
{
    internal readonly List<TerrainChange> Changes = new(256);
    public int Count => Changes.Count;
    public TerrainChange this[int i] => Changes[i];
    internal void Clear() => Changes.Clear();
}

/// <summary>One 32x32 slab of one layer; 1024 packed cells = 8 KB, safely off the LOH.</summary>
public sealed class TerrainChunk
{
    public const int Size = 32;
    internal readonly TerrainCell[] Cells = new TerrainCell[Size * Size];
    public Revision Revision;
}

/// <summary>
/// The single write facade over the chunked dense grid (ADR-0002). All mutations run
/// inside the authority-driven mutation window; changes batch per window and dispatch
/// once when the outermost scope closes. Handlers are idempotent bookkeeping only.
/// </summary>
public sealed class TerrainWorld
{
    public const int ChunkSize = TerrainChunk.Size;

    public int Width { get; }
    public int Height { get; }
    public int Depth { get; }
    public int ChunksX { get; }
    public int ChunksY { get; }

    private readonly TerrainChunk[] _chunks;
    private readonly MutationGate _gate;
    private readonly Stack<TerrainChangeBatch> _batchPool = new();
    private TerrainChangeBatch? _pending;

    /// <summary>World-wide revision; bumps once per dispatched batch. Path revalidation polls this.</summary>
    public Revision Revision;

    /// <summary>Synchronous batched change dispatch. Subscribers do dirty-marking only.</summary>
    public event Action<TerrainChangeBatch>? Changed;

    public TerrainWorld(int width, int height, int depth, MutationGate gate)
    {
        if (width % ChunkSize != 0 || height % ChunkSize != 0)
            throw new ArgumentException("Map dimensions must be chunk-aligned.");
        Width = width; Height = height; Depth = depth;
        _gate = gate;
        ChunksX = width / ChunkSize;
        ChunksY = height / ChunkSize;
        _chunks = new TerrainChunk[ChunksX * ChunksY * depth];
        for (int i = 0; i < _chunks.Length; i++) _chunks[i] = new TerrainChunk();
        gate.OnOuterClose(FlushBatch);
    }

    public bool InBounds(CellCoord c) =>
        (uint)c.X < (uint)Width && (uint)c.Y < (uint)Height && (uint)c.Z < (uint)Depth;

    /// <summary>The ONLY CellCoord→chunk mapping in the codebase (ADR-0002).</summary>
    public ChunkCoord ChunkOf(CellCoord c) => new(c.X / ChunkSize, c.Y / ChunkSize, c.Z);

    public TerrainChunk GetChunk(ChunkCoord cc) => _chunks[(cc.Z * ChunksY + cc.Cy) * ChunksX + cc.Cx];

    public TerrainCell Get(CellCoord c)
    {
        var chunk = _chunks[(c.Z * ChunksY + c.Y / ChunkSize) * ChunksX + c.X / ChunkSize];
        return chunk.Cells[(c.Y % ChunkSize) * ChunkSize + (c.X % ChunkSize)];
    }

    // ---- mutations (assert the window, capture previous state) ----

    public void SetCell(CellCoord c, TerrainCell cell)
    {
        _gate.AssertOpen();
        if (!InBounds(c)) throw new ArgumentOutOfRangeException(nameof(c), $"{c} out of bounds");
        var chunk = _chunks[(c.Z * ChunksY + c.Y / ChunkSize) * ChunksX + c.X / ChunkSize];
        int idx = (c.Y % ChunkSize) * ChunkSize + (c.X % ChunkSize);
        TerrainCell prev = chunk.Cells[idx];
        if (prev.Equals(cell)) return;
        chunk.Cells[idx] = cell;
        chunk.Revision.Bump();
        _pending ??= RentBatch();
        _pending.Changes.Add(new TerrainChange { Cell = c, Previous = prev, Current = cell });
    }

    /// <summary>Dig: remove the wall; the emptied cell gains a natural floor if it had none.</summary>
    public void DigWall(CellCoord c)
    {
        _gate.AssertOpen();
        var cell = Get(c);
        if (!cell.HasWall) return;
        cell.Wall = WallKind.None;
        cell.WallMaterial = MaterialId.None;
        cell.WallHp = 0;
        if (!cell.HasFloor) { cell.Floor = FloorKind.Natural; cell.FloorMaterial = MaterialId.Dirt; }
        SetCell(c, cell);
    }

    public void BuildWall(CellCoord c, MaterialId material)
    {
        var cell = Get(c);
        cell.Wall = WallKind.Built;
        cell.WallMaterial = material;
        cell.WallHp = (ushort)MaterialCatalog.Get(material).MaxWallHp;
        SetCell(c, cell);
    }

    public void SetFloor(CellCoord c, FloorKind kind, MaterialId material)
    {
        var cell = Get(c);
        cell.Floor = kind;
        cell.FloorMaterial = material;
        SetCell(c, cell);
    }

    /// <summary>Applies damage to a wall; returns true when the wall is destroyed by this hit.</summary>
    public bool DamageWall(CellCoord c, int damage)
    {
        _gate.AssertOpen();
        var cell = Get(c);
        if (!cell.HasWall) return false;
        int hp = cell.WallHp - damage;
        if (hp <= 0)
        {
            DigWall(c); // destruction leaves a diggable-open cell with natural floor
            return true;
        }
        cell.WallHp = (ushort)hp;
        SetCell(c, cell);
        return false;
    }

    public void RepairWall(CellCoord c, int amount)
    {
        _gate.AssertOpen();
        var cell = Get(c);
        if (!cell.HasWall) return;
        int max = MaterialCatalog.Get(cell.WallMaterial).MaxWallHp;
        cell.WallHp = (ushort)Math.Min(max, cell.WallHp + amount);
        SetCell(c, cell);
    }

    // ---- batching ----

    private TerrainChangeBatch RentBatch() => _batchPool.Count > 0 ? _batchPool.Pop() : new TerrainChangeBatch();

    private void FlushBatch()
    {
        var batch = _pending;
        if (batch == null || batch.Count == 0) return;
        _pending = null;
        Revision.Bump();
        try
        {
            Changed?.Invoke(batch);
        }
        finally
        {
            batch.Clear();
            _batchPool.Push(batch);
        }
    }

    // ---- serialization (raw cell bytes, chunk order = index order: deterministic) ----

    public void WriteTo(BinaryWriter w)
    {
        w.Write(Width); w.Write(Height); w.Write(Depth);
        Span<byte> buf = stackalloc byte[8];
        foreach (var chunk in _chunks)
        {
            var cells = chunk.Cells;
            for (int i = 0; i < cells.Length; i++)
            {
                var c = cells[i];
                buf[0] = (byte)c.Floor; buf[1] = (byte)c.FloorMaterial;
                buf[2] = (byte)c.Wall; buf[3] = (byte)c.WallMaterial;
                buf[4] = (byte)c.WallHp; buf[5] = (byte)(c.WallHp >> 8);
                buf[6] = (byte)c.Reserved; buf[7] = (byte)(c.Reserved >> 8);
                w.Write(buf);
            }
        }
    }

    /// <summary>Load-time restore. Caller must hold the mutation window open; no change events fire.</summary>
    public void ReadFrom(BinaryReader r)
    {
        _gate.AssertOpen();
        int wdt = r.ReadInt32(); int hgt = r.ReadInt32(); int dpt = r.ReadInt32();
        if (wdt != Width || hgt != Height || dpt != Depth)
            throw new InvalidDataException($"Terrain size mismatch: save {wdt}x{hgt}x{dpt} vs world {Width}x{Height}x{Depth}.");
        Span<byte> buf = stackalloc byte[8];
        foreach (var chunk in _chunks)
        {
            var cells = chunk.Cells;
            for (int i = 0; i < cells.Length; i++)
            {
                r.BaseStream.ReadExactly(buf);
                cells[i] = new TerrainCell
                {
                    Floor = (FloorKind)buf[0],
                    FloorMaterial = (MaterialId)buf[1],
                    Wall = (WallKind)buf[2],
                    WallMaterial = (MaterialId)buf[3],
                    WallHp = (ushort)(buf[4] | (buf[5] << 8)),
                    Reserved = (ushort)(buf[6] | (buf[7] << 8)),
                };
            }
            chunk.Revision.Bump();
        }
        // Discard any batched load-window changes: load rebuilds derived state wholesale.
        _pending?.Clear();
        Revision.Bump();
    }
}
