// PROTOTYPE - NOT FOR PRODUCTION
// Question: the ADR-0002 numbers the spike must report — chunk size, memory,
//   Gen0 during dig-heavy play, full-map walkability sweep (the AoS falsification
//   test), extraction cost, snapshot strategy.
// Date: 2026-07-25

using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace TerrainSpike;

/// <summary>Sink that does what a real subscriber does: copy primitives out, dirty a chunk set.</summary>
public sealed class DirtyTrackingSink : ITerrainChangeSink
{
    private readonly HashSet<ChunkCoord> _dirty = [];
    private readonly TerrainWorld? _world;
    public DirtyTrackingSink(TerrainWorld? world = null) => _world = world;
    public TerrainWorld? World { get; set; }
    public int DirtyCount => _dirty.Count;
    public void Publish(in TerrainChangeBatch batch)
    {
        TerrainWorld? w = World ?? _world;
        if (w is null) return;
        foreach (TerrainChange c in batch.Changes) _dirty.Add(w.ChunkOf(c.Cell));
    }
    public void PublishWorldReloaded() => _dirty.Clear();
    public void Clear() => _dirty.Clear();
}

public static class Benchmarks
{
    private const int MvpX = 128, MvpY = 128, MvpLayers = 16;      // "generous MVP mountain" (ADR-0002)
    private const int BigX = 256, BigY = 256, BigLayers = 32;      // full-vision ceiling

    private static (TerrainWorld world, DirtyTrackingSink sink, MutationWindow win, MaterialCatalog cat,
                    ushort dirt, ushort granite, ushort reinforced, ushort floor)
        MakeWorld(int sx, int sy, int layers, int chunkSize)
    {
        var cat = new MaterialCatalog();
        ushort dirt = cat.Register("dirt", 1, 30);
        ushort granite = cat.Register("granite", 2, 120);
        ushort reinforced = cat.Register("reinforced", 3, 400);
        ushort floor = cat.Register("rock_floor", 1, 0);
        var sink = new DirtyTrackingSink();
        var win = new MutationWindow();
        var world = new TerrainWorld(new WorldBounds(sx, sy, layers), cat, sink, win, chunkSize);
        sink.World = world;
        // Solid mountain: every cell is rock (a mountain is mostly SOLID, per ADR-0002 Alt-D rejection).
        world.PopulateForLoad(w =>
        {
            for (int z = 0; z < layers; z++)
                for (int y = 0; y < sy; y++)
                    for (int x = 0; x < sx; x++)
                        w.LoadSetCell(new CellCoord(x, y, z),
                            new TerrainCell { FloorTypeId = floor, WallTypeId = dirt, WallHp = 30 });
        });
        return (world, sink, win, cat, dirt, granite, reinforced, floor);
    }

    /// <summary>Carve a plausible colony: rooms and corridors, so sweeps see mixed data.</summary>
    private static void CarveColony(TerrainWorld w, MutationWindow win, int sx, int sy, int layers, int seed = 7)
    {
        var rng = new Random(seed);
        using (win.Open())
        {
            for (int z = 0; z < layers; z++)
                for (int room = 0; room < 12; room++)
                {
                    int rx = rng.Next(sx - 12), ry = rng.Next(sy - 12), rw = rng.Next(4, 12), rh = rng.Next(4, 12);
                    for (int y = ry; y < Math.Min(ry + rh, sy); y++)
                        for (int x = rx; x < Math.Min(rx + rw, sx); x++)
                            w.ClearWall(new CellCoord(x, y, z));
                }
        }
    }

    // ---------------------------------------------------------------- memory
    public static void Memory()
    {
        Console.WriteLine("\n=== MEMORY (cell data) ===");
        Console.WriteLine($"  sizeof(TerrainCell)                = {Unsafe.SizeOf<TerrainCell>()} bytes (contract: 8)");
        foreach ((string label, int sx, int sy, int lz) in new[]
                 { ("MVP  128x128x16", MvpX, MvpY, MvpLayers), ("Full 256x256x32", BigX, BigY, BigLayers) })
        {
            foreach (int cs in new[] { 16, 32, 64 })
            {
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                long before = GC.GetTotalMemory(true);
                var (world, _, _, _, _, _, _, _) = MakeWorld(sx, sy, lz, cs);
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                long after = GC.GetTotalMemory(true);
                long chunkBytes = (long)cs * cs * Unsafe.SizeOf<TerrainCell>();
                Console.WriteLine($"  {label} chunk={cs,2}: cells={world.CellDataBytes / 1024.0 / 1024.0,6:F2} MB  " +
                                  $"heap={(after - before) / 1024.0 / 1024.0,6:F2} MB  chunks={world.ChunkCount,5}  " +
                                  $"chunk={chunkBytes / 1024.0,5:F1} KB {(chunkBytes >= 85000 ? "** LOH **" : "(off-LOH)")}");
                GC.KeepAlive(world);
            }
        }
    }

    // ------------------------------------------------- walkability sweep (AoS vs SoA)
    /// <summary>The designated AoS falsification test: full-map walkability sweep.</summary>
    public static void WalkabilitySweep()
    {
        Console.WriteLine("\n=== FULL-MAP WALKABILITY SWEEP (AoS falsification test) ===");
        Console.WriteLine("  Sweep = read floor+wall for every cell in the world (pathfinding's hottest bulk read).");
        foreach (int cs in new[] { 16, 32, 64 })
        {
            var (world, _, win, _, _, _, _, _) = MakeWorld(MvpX, MvpY, MvpLayers, cs);
            CarveColony(world, win, MvpX, MvpY, MvpLayers);

            // AoS sweep via the facade's bulk read (the sanctioned path)
            int passable = 0;
            for (int warm = 0; warm < 5; warm++)          // JIT + cache warmup: never measure the first pass
                for (int z = 0; z < MvpLayers; z++)
                    for (int cy = 0; cy < world.ChunksY; cy++)
                        for (int cx = 0; cx < world.ChunksX; cx++)
                        {
                            ReadOnlySpan<TerrainCell> wc = world.GetChunkCells(new ChunkCoord(cx, cy, z));
                            for (int i = 0; i < wc.Length; i++) if (wc[i].WallTypeId == 0) passable++;
                        }
            var sw = Stopwatch.StartNew();
            const int reps = 20;
            for (int r = 0; r < reps; r++)
            {
                passable = 0;
                for (int z = 0; z < MvpLayers; z++)
                    for (int cy = 0; cy < world.ChunksY; cy++)
                        for (int cx = 0; cx < world.ChunksX; cx++)
                        {
                            ReadOnlySpan<TerrainCell> cells = world.GetChunkCells(new ChunkCoord(cx, cy, z));
                            for (int i = 0; i < cells.Length; i++)
                                if (cells[i].FloorTypeId != 0 && cells[i].WallTypeId == 0) passable++;
                        }
            }
            sw.Stop();
            double aosMs = sw.Elapsed.TotalMilliseconds / reps;

            // SoA comparison: hot fields only (floor+wall as two ushort planes = 4 B/cell instead of 8)
            int cellCount = MvpX * MvpY * MvpLayers;
            var floors = new ushort[cellCount];
            var walls = new ushort[cellCount];
            int idx = 0;
            for (int z = 0; z < MvpLayers; z++)
                for (int y = 0; y < MvpY; y++)
                    for (int x = 0; x < MvpX; x++)
                    { TerrainCell c = world.GetCell(new CellCoord(x, y, z)); floors[idx] = c.FloorTypeId; walls[idx] = c.WallTypeId; idx++; }

            int passable2 = 0;
            for (int warm = 0; warm < 5; warm++)
                for (int i = 0; i < cellCount; i++) if (floors[i] != 0 && walls[i] == 0) passable2++;
            sw.Restart();
            for (int r = 0; r < reps; r++)
            {
                passable2 = 0;
                for (int i = 0; i < cellCount; i++) if (floors[i] != 0 && walls[i] == 0) passable2++;
            }
            sw.Stop();
            double soaMs = sw.Elapsed.TotalMilliseconds / reps;

            Console.WriteLine($"  chunk={cs,2}: AoS {aosMs,7:F3} ms   SoA(hot) {soaMs,7:F3} ms   " +
                              $"AoS costs {(aosMs / soaMs - 1) * 100,5:F0}% more   " +
                              $"[{aosMs / 16.6 * 100,5:F1}% of a 16.6 ms frame]   passable={passable}/{passable2}");
        }
        Console.WriteLine("  NOTE: a full-map sweep is a worst case — real pathfinding sweeps a region, not the world.");
    }

    // ------------------------------------------------- dig-heavy allocation
    public static void DigHeavyAllocation()
    {
        Console.WriteLine("\n=== STEADY-STATE ALLOCATION (dig-heavy play) ===");
        var (world, sink, win, _, _, _, _, _) = MakeWorld(MvpX, MvpY, MvpLayers, 32);
        CarveColony(world, win, MvpX, MvpY, MvpLayers);

        // Warm up so JIT/first-touch costs are not counted as steady state.
        using (win.Open())
            for (int i = 0; i < 1000; i++) world.ClearWall(new CellCoord(i % MvpX, (i / MvpX) % MvpY, 0));

        const int digs = 20_000;
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        int gen0Before = GC.CollectionCount(0);
        long allocBefore = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        using (win.Open())
        {
            for (int i = 0; i < digs; i++)
            {
                var c = new CellCoord((i * 7) % MvpX, (i * 13) % MvpY, (i * 3) % MvpLayers);
                world.SetWall(c, 1, 0);
                world.ApplyWallDamage(c, 10);
                world.ApplyWallDamage(c, 25);      // destroys (dirt = 30 hp): the dig completes
            }
        }
        sw.Stop();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
        int gen0 = GC.CollectionCount(0) - gen0Before;
        int mutations = digs * 3;
        Console.WriteLine($"  {mutations:N0} mutations (+{mutations:N0} publishes) in {sw.Elapsed.TotalMilliseconds:F1} ms");
        Console.WriteLine($"  per mutation+publish: {sw.Elapsed.TotalMilliseconds * 1000.0 / mutations:F3} us");
        Console.WriteLine($"  ALLOCATED: {allocated:N0} bytes total = {(double)allocated / mutations:F2} B/mutation");
        Console.WriteLine($"  Gen0 collections: {gen0}   (dirty chunks tracked: {sink.DirtyCount})");
        Console.WriteLine($"  VERDICT: {(allocated / (double)mutations < 1.0 ? "ZERO steady-state allocation CONFIRMED" : "ALLOCATION PRESENT — investigate")}");

        // A colonist digs ~1 cell/sec; a frame is 16.6 ms. What does a realistic frame cost?
        double perFrame = sw.Elapsed.TotalMilliseconds / digs * 10;   // 10 colonists digging simultaneously
        Console.WriteLine($"  realistic frame (10 colonists, 1 dig each): {perFrame:F4} ms = {perFrame / 16.6 * 100:F2}% of budget");
    }

    // ------------------------------------------------- bulk apply
    public static void BulkApply()
    {
        Console.WriteLine("\n=== BULK APPLY (stair dig / AoE / designation-cancel) ===");
        var (world, _, win, _, _, granite, _, floor) = MakeWorld(MvpX, MvpY, MvpLayers, 32);
        foreach (int n in new[] { 2, 16, 64, 256 })
        {
            var muts = new TerrainMutation[n];
            for (int i = 0; i < n; i++)
                muts[i] = new TerrainMutation(new CellCoord(i % MvpX, (i / MvpX) % MvpY, 2), TerrainMutationKind.SetWall, granite, 0, 0);
            const int reps = 2000;
            GC.Collect();
            long allocBefore = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();
            using (win.Open())
                for (int r = 0; r < reps; r++)
                {
                    for (int i = 0; i < n; i++)   // alternate material so it is never a no-op
                        muts[i] = muts[i] with { TypeId = (ushort)(r % 2 == 0 ? granite : 1) };
                    world.Apply(muts);
                }
            sw.Stop();
            long alloc = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
            Console.WriteLine($"  batch of {n,3}: {sw.Elapsed.TotalMilliseconds / reps * 1000,7:F2} us/batch   " +
                              $"{alloc / (double)reps,6:F1} B/batch   (validation is O(n^2) dup-check)");
        }
    }

    // ------------------------------------------------- render extraction
    /// <summary>
    /// What a render backend does when a chunk is dirty: walk the chunk and emit one
    /// instance per exposed wall face. Counts instances (draw-call relevant) and time.
    /// </summary>
    public static void RenderExtraction()
    {
        Console.WriteLine("\n=== RENDER EXTRACTION (dirty-chunk rebuild, per ADR-0002 GetChunkCells path) ===");
        foreach (int cs in new[] { 16, 32, 64 })
        {
            var (world, _, win, _, _, _, _, _) = MakeWorld(MvpX, MvpY, MvpLayers, cs);
            CarveColony(world, win, MvpX, MvpY, MvpLayers);

            long instances = 0;
            const int reps = 200;
            for (int warm = 0; warm < 50; warm++)          // JIT + cache warmup
            {
                ReadOnlySpan<TerrainCell> wc = world.GetChunkCells(new ChunkCoord(1, 1, 2));
                for (int i = 0; i < wc.Length; i++) if (wc[i].WallTypeId != 0) instances++;
            }
            var sw = Stopwatch.StartNew();
            for (int r = 0; r < reps; r++)
            {
                instances = 0;
                // one chunk rebuild (the common case: a dig dirties exactly one chunk)
                ReadOnlySpan<TerrainCell> cells = world.GetChunkCells(new ChunkCoord(1, 1, 2));
                for (int i = 0; i < cells.Length; i++)
                {
                    if (cells[i].WallTypeId != 0) instances++;         // wall mesh instance
                    if (cells[i].FloorTypeId != 0) instances++;        // floor mesh instance
                }
            }
            sw.Stop();
            double oneChunkUs = sw.Elapsed.TotalMilliseconds / reps * 1000;

            // full cutaway slice rebuild (worst case: player changes Z level, all chunks in a layer rebuild)
            sw.Restart();
            long sliceInstances = 0;
            const int sliceReps = 50;
            for (int r = 0; r < sliceReps; r++)
            {
                sliceInstances = 0;
                for (int cy = 0; cy < world.ChunksY; cy++)
                    for (int cx = 0; cx < world.ChunksX; cx++)
                    {
                        ReadOnlySpan<TerrainCell> cells = world.GetChunkCells(new ChunkCoord(cx, cy, 2));
                        for (int i = 0; i < cells.Length; i++)
                        {
                            if (cells[i].WallTypeId != 0) sliceInstances++;
                            if (cells[i].FloorTypeId != 0) sliceInstances++;
                        }
                    }
            }
            sw.Stop();
            double sliceMs = sw.Elapsed.TotalMilliseconds / sliceReps;
            Console.WriteLine($"  chunk={cs,2}: 1-chunk rebuild {oneChunkUs,7:F2} us ({instances,5} inst)   " +
                              $"full-layer rebuild {sliceMs,6:F3} ms ({sliceInstances,6} inst) = {sliceMs / 16.6 * 100,4:F1}% frame");
        }
        Console.WriteLine("  NOTE: instance counts feed the renderer; draw calls depend on the backend (see Godot half).");
    }

    // ------------------------------------------------- snapshot
    public static void SnapshotCost()
    {
        Console.WriteLine("\n=== SNAPSHOT / RESTORE (CD-9 autosave at mode-switch) ===");
        foreach ((string label, int sx, int sy, int lz) in new[]
                 { ("MVP  128x128x16", MvpX, MvpY, MvpLayers), ("Full 256x256x32", BigX, BigY, BigLayers) })
        {
            var (world, _, win, _, _, _, _, _) = MakeWorld(sx, sy, lz, 32);
            CarveColony(world, win, sx, sy, lz);

            GC.Collect();
            long allocBefore = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();
            TerrainSnapshot snap = world.Snapshot();
            sw.Stop();
            long alloc = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
            double snapMs = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            world.Restore(snap);
            sw.Stop();
            Console.WriteLine($"  {label}: Snapshot {snapMs,6:F2} ms ({alloc / 1024.0 / 1024.0,5:F2} MB allocated)   " +
                              $"Restore {sw.Elapsed.TotalMilliseconds,6:F2} ms (incl. id remap)");
        }
        Console.WriteLine("  Snapshot happens at the mode-switch autosave — a non-gameplay moment (CD-9).");
    }
}
