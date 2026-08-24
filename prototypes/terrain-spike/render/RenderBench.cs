// PROTOTYPE - NOT FOR PRODUCTION
// Question: MultiMesh vs GridMap as the render backend reading ADR-0002's model —
//   draw calls, video memory, build cost, and per-dig chunk rebuild cost at MVP
//   cutaway scale. (ADR-0002 open question: "render backend".)
// Date: 2026-07-25

using System.Diagnostics;
using Godot;
using TerrainSpike;

namespace TerrainSpikeRender;

public partial class RenderBench : Node3D
{
    // MVP mountain; the cutaway shows the active layer plus two below it.
    private const int SizeX = 128, SizeY = 128, Layers = 16, ChunkSize = 32;
    private const int VisibleLayers = 3, TopLayer = 6;

    private TerrainWorld _world;
    private MutationWindow _window;
    private ushort _dirt, _granite, _reinforced, _floor;
    private string _backend = "multimesh";
    private int _octant = 8;                                  // GridMap batching granularity (default 8)
    private int _styles = 1;                                  // distinct style variants per tier
    private int _frame;
    private long _buildMs;
    private int _nodeCount;

    // --- Criterion-5 frame-rate clause (added 2026-08-24 for the target-hardware run) ---
    // The original harness read TimeFps ONCE at frame 60 and quit at 62. That cannot see a
    // hitch, cannot separate warmup from steady state, and reports a mean where the criterion
    // ("holds 60 fps") is really a tail requirement. Sustained window + percentiles instead.
    private int _warmupFrames = 300;      // discarded: shader/pipeline warmup, first rebuilds
    private int _measureFrames = 1800;    // ~30 s at 60 fps
    private int _digsPerFrame = 8;        // dig-driven chunk rebuilds, as criterion 5 requires
    private double[] _frameMs;
    private int _measured;
    private bool _sampled;
    private int _gen0Start, _gen1Start, _gen2Start;
    private long _allocStart;
    private int _digCursor;
    // Dig churn band: alternately clear and restore walls so the load is sustainable for the
    // whole window instead of exhausting the region and silently measuring an idle scene.
    private const int DigBandX = 20, DigBandY = 20, DigBandW = 40, DigBandH = 40;
    private readonly HashSet<ChunkCoord> _frameDirty = new();

    // MultiMesh backend bookkeeping: one MultiMesh per (chunk, material).
    private readonly Dictionary<(ChunkCoord chunk, ushort mat), MultiMeshInstance3D> _mmi = new();
    private GridMap _gridMap;
    private readonly Dictionary<ushort, Material> _materials = new();
    private BoxMesh _cubeMesh;

    public override void _Ready()
    {
        foreach (string a in OS.GetCmdlineUserArgs())
        {
            if (a.StartsWith("backend=")) _backend = a["backend=".Length..];
            if (a.StartsWith("octant=")) _octant = int.Parse(a["octant=".Length..]);
            if (a.StartsWith("styles=")) _styles = int.Parse(a["styles=".Length..]);
            if (a.StartsWith("warmup=")) _warmupFrames = int.Parse(a["warmup=".Length..]);
            if (a.StartsWith("frames=")) _measureFrames = int.Parse(a["frames=".Length..]);
            if (a.StartsWith("digs=")) _digsPerFrame = int.Parse(a["digs=".Length..]);
        }

        // vsync would pin every frame to the display refresh and make the fps number a
        // property of the monitor, not the renderer. Disabled here as well as in
        // project.godot so a stale editor config cannot silently invalidate the run.
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        Engine.MaxFps = 0;

        _frameMs = new double[_measureFrames];

        GD.Print($"=== RENDER BACKEND BENCH: {_backend} ===");
        // Criterion 5 is a TARGET-HARDWARE clause. The 2026-07-25 run was software Vulkan
        // (lavapipe, 3-4 fps) and therefore meaningless. Name the adapter in the log so a
        // reader can tell at a glance whether the numbers below are admissible.
        string adapter = RenderingServer.GetVideoAdapterName();
        bool software = adapter.Contains("llvmpipe", StringComparison.OrdinalIgnoreCase)
                     || adapter.Contains("lavapipe", StringComparison.OrdinalIgnoreCase)
                     || adapter.Contains("softpipe", StringComparison.OrdinalIgnoreCase)
                     || adapter.Contains("SwiftShader", StringComparison.OrdinalIgnoreCase);
        GD.Print($"RESULT video_adapter={adapter}");
        GD.Print($"RESULT software_rasterizer={software}");
        if (software)
            GD.PrintErr("FRAME-RATE CLAUSE VOID: software rasterizer detected. Draw-call and "
                      + "memory numbers remain valid (hardware-independent); every timing "
                      + "number below is NOT admissible for ADR-0002 criterion 5.");
        BuildWorld();
        BuildSceneRig();

        var sw = Stopwatch.StartNew();
        if (_backend == "gridmap") BuildGridMap();
        else if (_backend == "gridmap_two") BuildGridMapTwo();
        else BuildMultiMesh();
        sw.Stop();
        _buildMs = sw.ElapsedMilliseconds;
        GD.Print($"initial build: {_buildMs} ms, {_nodeCount} render nodes");
    }

    private void BuildWorld()
    {
        var cat = new MaterialCatalog();
        _dirt = cat.Register("dirt", 1, 30);
        _granite = cat.Register("granite", 2, 120);
        _reinforced = cat.Register("reinforced", 3, 400);
        _floor = cat.Register("rock_floor", 1, 0);
        _window = new MutationWindow();
        _world = new TerrainWorld(new WorldBounds(SizeX, SizeY, Layers), cat,
                                  new NullSink(), _window, ChunkSize);

        // Solid mountain with strata by depth, then carve a colony (deterministic seed).
        _world.PopulateForLoad(w =>
        {
            for (int z = 0; z < Layers; z++)
            {
                ushort mat = z < 4 ? _dirt : z < 10 ? _granite : _reinforced;
                for (int y = 0; y < SizeY; y++)
                    for (int x = 0; x < SizeX; x++)
                        w.LoadSetCell(new CellCoord(x, y, z),
                            new TerrainCell { FloorTypeId = _floor, WallTypeId = mat, WallHp = 30 });
            }
        });

        var rng = new Random(7);
        using (_window.Open())
            for (int z = 0; z < Layers; z++)
                for (int room = 0; room < 12; room++)
                {
                    int rx = rng.Next(SizeX - 12), ry = rng.Next(SizeY - 12), rw = rng.Next(4, 12), rh = rng.Next(4, 12);
                    for (int y = ry; y < Math.Min(ry + rh, SizeY); y++)
                        for (int x = rx; x < Math.Min(rx + rw, SizeX); x++)
                            _world.ClearWall(new CellCoord(x, y, z));
                }
    }

    private void BuildSceneRig()
    {
        _cubeMesh = new BoxMesh { Size = Vector3.One };
        foreach ((ushort id, Color c) in new[]
                 { (_dirt, new Color(0.49f, 0.36f, 0.24f)), (_granite, new Color(0.52f, 0.52f, 0.55f)),
                   (_reinforced, new Color(0.31f, 0.46f, 0.56f)), (_floor, new Color(0.25f, 0.22f, 0.19f)) })
            _materials[id] = new StandardMaterial3D { AlbedoColor = c };

        var cam = new Camera3D { Fov = 60 };
        AddChild(cam);                                        // must be in-tree BEFORE LookAt
        // Low angle at the cutaway edge: shows the layered cross-section, where a cell's
        // floor and wall are both visible — the case the single-GridMap backend cannot express.
        cam.Position = new Vector3(64, 4, 148);
        cam.LookAt(new Vector3(64, -8, 100), Vector3.Up);
        var sun = new DirectionalLight3D();
        sun.RotateX(-Mathf.Pi / 3);
        AddChild(sun);
    }

    /// <summary>Backend A: one MultiMesh per (chunk, material) — the ADR's GetChunkCells extraction path.</summary>
    private void BuildMultiMesh()
    {
        for (int z = TopLayer; z < TopLayer + VisibleLayers; z++)
            for (int cy = 0; cy < _world.ChunksY; cy++)
                for (int cx = 0; cx < _world.ChunksX; cx++)
                    RebuildChunk(new ChunkCoord(cx, cy, z));
    }

    // Pooled extraction scratch — reused across rebuilds ("multimesh_pooled").
    private readonly Dictionary<ushort, List<Transform3D>> _scratch = new();
    private readonly Dictionary<(ChunkCoord, ushort), float[]> _bufPool = new();

    private void RebuildChunk(ChunkCoord chunk)
    {
        ReadOnlySpan<TerrainCell> cells = _world.GetChunkCells(chunk);
        Dictionary<ushort, List<Transform3D>> byMat;
        if (_backend == "multimesh_pooled")
        {
            byMat = _scratch;
            foreach (List<Transform3D> l in byMat.Values) l.Clear();
        }
        else byMat = new Dictionary<ushort, List<Transform3D>>();

        for (int i = 0; i < cells.Length; i++)
        {
            int lx = i & (ChunkSize - 1), ly = i >> 5;
            float wx = chunk.X * ChunkSize + lx, wy = chunk.Y * ChunkSize + ly;
            float wz = -chunk.Z;                                  // sim Z-down -> Godot Y-up (view layer's mapping)

            if (cells[i].WallTypeId != 0)
            {
                if (!byMat.TryGetValue(cells[i].WallTypeId, out List<Transform3D> l))
                    byMat[cells[i].WallTypeId] = l = new List<Transform3D>();
                l.Add(new Transform3D(Basis.Identity, new Vector3(wx, wz, wy)));
            }
            else if (cells[i].FloorTypeId != 0)                   // floors only visible where no wall
            {
                if (!byMat.TryGetValue(_floor, out List<Transform3D> l))
                    byMat[_floor] = l = new List<Transform3D>();
                l.Add(new Transform3D(Basis.Identity.Scaled(new Vector3(1, 0.1f, 1)), new Vector3(wx, wz - 0.45f, wy)));
            }
        }

        foreach ((ushort mat, List<Transform3D> xforms) in byMat)
        {
            if (xforms.Count == 0) continue;
            var key = (chunk, mat);
            if (!_mmi.TryGetValue(key, out MultiMeshInstance3D inst))
            {
                inst = new MultiMeshInstance3D
                {
                    Multimesh = new MultiMesh
                    { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = _cubeMesh },
                    MaterialOverride = _materials[mat],
                };
                AddChild(inst);
                _mmi[key] = inst;
                _nodeCount++;
            }
            inst.Multimesh.InstanceCount = xforms.Count;

            if (_backend is "multimesh_buffer" or "multimesh_pooled")
            {
                // Bulk upload: one marshalled call for the whole chunk instead of N.
                // Pooled variant also reuses the float[] across rebuilds.
                float[] buf;
                if (_backend == "multimesh_pooled")
                {
                    if (!_bufPool.TryGetValue(key, out buf) || buf.Length != xforms.Count * 12)
                        _bufPool[key] = buf = new float[xforms.Count * 12];
                }
                else buf = new float[xforms.Count * 12];
                for (int i = 0; i < xforms.Count; i++)
                {
                    Transform3D t = xforms[i];
                    int o = i * 12;
                    buf[o + 0] = t.Basis.X.X; buf[o + 1] = t.Basis.Y.X; buf[o + 2] = t.Basis.Z.X; buf[o + 3] = t.Origin.X;
                    buf[o + 4] = t.Basis.X.Y; buf[o + 5] = t.Basis.Y.Y; buf[o + 6] = t.Basis.Z.Y; buf[o + 7] = t.Origin.Y;
                    buf[o + 8] = t.Basis.X.Z; buf[o + 9] = t.Basis.Y.Z; buf[o + 10] = t.Basis.Z.Z; buf[o + 11] = t.Origin.Z;
                }
                inst.Multimesh.Buffer = buf;
            }
            else
            {
                for (int i = 0; i < xforms.Count; i++) inst.Multimesh.SetInstanceTransform(i, xforms[i]);
            }
        }
    }

    /// <summary>
    /// Backend C: TWO stacked GridMaps — one for floors, one for walls — so a cell can
    /// carry BOTH, which the single-GridMap backend structurally cannot (one item id per
    /// cell). Floors are rendered for EVERY cell that has one, including under walls.
    /// </summary>
    private GridMap _floorMap;

    private void BuildGridMapTwo()
    {
        var wallLib = new MeshLibrary();
        foreach ((ushort id, Material m) in _materials)
        {
            wallLib.CreateItem(id);
            wallLib.SetItemMesh(id, new BoxMesh { Size = Vector3.One, Material = m });
            wallLib.SetItemName(id, $"wall{id}");
        }

        // STYLE-VARIETY TEST (godot-specialist finding): draw calls are driven by how many
        // distinct mesh/material combos co-occur IN AN OCTANT, not by MeshLibrary size.
        // Build `_styles` extra variants per tier, each with its own material, and scatter them.
        if (_styles > 1)
        {
            var tiers = new[] { _dirt, _granite, _reinforced };
            foreach (ushort tier in tiers)
                for (int s = 0; s < _styles; s++)
                {
                    int id = StyleItemId(tier, s);
                    var mat = new StandardMaterial3D
                    { AlbedoColor = ((StandardMaterial3D)_materials[tier]).AlbedoColor * (0.5f + 0.5f * s / _styles) };
                    wallLib.CreateItem(id);
                    wallLib.SetItemMesh(id, new BoxMesh { Size = Vector3.One, Material = mat });
                    wallLib.SetItemName(id, $"wall{tier}_style{s}");
                }
        }
        // Floors are thin slabs sitting at the bottom of the cell.
        var floorLib = new MeshLibrary();
        var slab = new BoxMesh { Size = new Vector3(1, 0.1f, 1), Material = _materials[_floor] };
        floorLib.CreateItem(_floor);
        floorLib.SetItemMesh(_floor, slab);
        floorLib.SetItemMeshTransform(_floor, new Transform3D(Basis.Identity, new Vector3(0, -0.45f, 0)));
        floorLib.SetItemName(_floor, "floor");

        _gridMap = new GridMap { CellSize = Vector3.One, MeshLibrary = wallLib, CellOctantSize = _octant };
        _floorMap = new GridMap { CellSize = Vector3.One, MeshLibrary = floorLib, CellOctantSize = _octant };
        AddChild(_gridMap); AddChild(_floorMap);
        _nodeCount = 2;

        for (int z = TopLayer; z < TopLayer + VisibleLayers; z++)
            for (int y = 0; y < SizeY; y++)
                for (int x = 0; x < SizeX; x++)
                {
                    TerrainCell c = _world.GetCell(new CellCoord(x, y, z));
                    var pos = new Vector3I(x, -z, y);
                    if (c.FloorTypeId != 0) _floorMap.SetCellItem(pos, _floor);   // ALWAYS, even under a wall
                    if (c.WallTypeId != 0)
                        _gridMap.SetCellItem(pos, _styles > 1
                            ? StyleItemId(c.WallTypeId, (x * 7 + y * 13 + z * 3) % _styles)   // scattered variety
                            : c.WallTypeId);
                }
    }

    private static int StyleItemId(ushort tier, int style) => 1000 + tier * 100 + style;

    /// <summary>Backend B: GridMap + runtime MeshLibrary — Godot's native cell-based tool.</summary>
    private void BuildGridMap()
    {
        var lib = new MeshLibrary();
        foreach ((ushort id, Material m) in _materials)
        {
            var mesh = new BoxMesh { Size = Vector3.One, Material = m };
            lib.CreateItem(id);
            lib.SetItemMesh(id, mesh);
            lib.SetItemName(id, $"mat{id}");
        }
        _gridMap = new GridMap { CellSize = Vector3.One, MeshLibrary = lib, CellOctantSize = _octant };
        AddChild(_gridMap);
        _nodeCount = 1;

        for (int z = TopLayer; z < TopLayer + VisibleLayers; z++)
            for (int y = 0; y < SizeY; y++)
                for (int x = 0; x < SizeX; x++)
                {
                    TerrainCell c = _world.GetCell(new CellCoord(x, y, z));
                    if (c.WallTypeId != 0) _gridMap.SetCellItem(new Vector3I(x, -z, y), c.WallTypeId);
                    else if (c.FloorTypeId != 0) _gridMap.SetCellItem(new Vector3I(x, -z, y), _floor);
                }
    }

    public override void _Process(double delta)
    {
        _frame++;

        // Warm up UNDER LOAD: the first chunk rebuilds and shader compiles are the slowest
        // frames in the run, and including them would understate steady-state performance
        // exactly where the criterion cares about it.
        if (_frame <= _warmupFrames) { DriveDigLoad(); return; }

        if (!_sampled)
        {
            Sample();                       // draw calls, memory, screenshot, model check
            _sampled = true;
            _gen0Start = GC.CollectionCount(0);
            _gen1Start = GC.CollectionCount(1);
            _gen2Start = GC.CollectionCount(2);
            _allocStart = GC.GetTotalAllocatedBytes(precise: false);
            return;                         // Sample() itself is expensive - never timed
        }

        if (_measured < _measureFrames)
        {
            DriveDigLoad();
            _frameMs[_measured++] = delta * 1000.0;
            return;
        }

        ReportFrameStats();
        DigRebuildBench();
        GetTree().Quit();
    }

    /// <summary>
    /// Sustained dig-driven churn for the measurement window. Alternates clearing and
    /// restoring walls across a fixed band so the scene never runs out of diggable cells.
    /// </summary>
    private void DriveDigLoad()
    {
        const int bandCells = DigBandW * DigBandH;
        _frameDirty.Clear();

        // ONE mutation window per frame, not one per dig. A real dispatch batches its writes,
        // and this prototype's MutationWindow.Open() returns an IDisposable CLASS, which boxes
        // 24 B per call (the production contract requires a readonly struct - see
        // technical-preferences). Opening per dig would inject digs*24 B/frame of
        // prototype-only garbage into the Gen0 number this bench exists to measure.
        using (_window.Open())
        {
            for (int i = 0; i < _digsPerFrame; i++)
            {
                int idx = _digCursor++;
                int o = idx % bandCells;
                bool restore = ((idx / bandCells) & 1) == 1;
                int x = DigBandX + o % DigBandW;
                int y = DigBandY + o / DigBandW;
                var c = new CellCoord(x, y, TopLayer);

                if (restore) _world.SetWall(c, _granite, 0);
                else _world.ClearWall(c);

                ApplyRenderForDig(x, y, TopLayer, restore, c);
            }
        }

        // MultiMesh rebuilds dirty chunks once per frame, not once per dig - otherwise the
        // backend is charged for redundant rebuilds no real implementation would perform.
        if (_backend is not ("gridmap" or "gridmap_two"))
            foreach (ChunkCoord ch in _frameDirty) RebuildChunk(ch);
    }

    private void ApplyRenderForDig(int x, int y, int z, bool restore, CellCoord c)
    {
        var pos = new Vector3I(x, -z, y);
        if (_backend == "gridmap")
            _gridMap.SetCellItem(pos, restore ? _granite : _floor);
        else if (_backend == "gridmap_two")
            _gridMap.SetCellItem(pos, restore ? _granite : -1);   // floor map untouched
        else
            _frameDirty.Add(_world.ChunkOf(c));
    }

    /// <summary>
    /// Criterion 5's frame-rate clause. Reported as percentiles because "holds 60 fps" is a
    /// tail property: a 60 fps mean with a 40 ms spike on every chunk rebuild is a visible
    /// hitch and a failure, and a single mean would hide it.
    /// </summary>
    private void ReportFrameStats()
    {
        if (_measured == 0) { GD.PrintErr("no frames measured - frames= must be > 0"); return; }

        var sorted = (double[])_frameMs.Clone();
        Array.Sort(sorted, 0, _measured);

        double sum = 0;
        for (int i = 0; i < _measured; i++) sum += _frameMs[i];
        double mean = sum / _measured;
        double P(double q) => sorted[Math.Clamp((int)(q * _measured), 0, _measured - 1)];
        double p50 = P(0.50), p95 = P(0.95), p99 = P(0.99), worst = sorted[_measured - 1];

        int gen0 = GC.CollectionCount(0) - _gen0Start;
        int gen1 = GC.CollectionCount(1) - _gen1Start;
        int gen2 = GC.CollectionCount(2) - _gen2Start;
        long alloc = GC.GetTotalAllocatedBytes(precise: false) - _allocStart;

        GD.Print($"RESULT measured_frames={_measured} warmup_frames={_warmupFrames} digs_per_frame={_digsPerFrame}");
        GD.Print($"RESULT frame_ms_mean={mean:F3} p50={p50:F3} p95={p95:F3} p99={p99:F3} worst={worst:F3}");
        GD.Print($"RESULT fps_mean={1000.0 / mean:F1} fps_at_p99={1000.0 / p99:F1}");
        GD.Print($"RESULT gen0_collections={gen0} gen1={gen1} gen2={gen2}");
        GD.Print($"RESULT bytes_per_frame={(double)alloc / _measured:F1}");
        GD.Print($"RESULT VERDICT_frame_rate_clause={(p99 <= 16.6 ? "PASS" : "FAIL")} (p99 {p99:F3} ms vs 16.6 ms budget)");
        GD.Print($"RESULT VERDICT_gen0_clause={(gen0 == 0 ? "PASS" : "REVIEW")} (criterion 5 wants no steady-state GC during dig-heavy play)");
        GD.Print("NOTE checkpoint clause (ADR-0004 Option A, per-activation cadence) is NOT measured by this bench - no implementation exists.");
    }

    private void Sample()
    {
        ulong draws = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame);
        ulong prims = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalPrimitivesInFrame);
        ulong objs = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalObjectsInFrame);
        ulong vmem = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.VideoMemUsed);
        ulong bmem = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.BufferMemUsed);
        GD.Print($"RESULT backend={_backend}");
        GD.Print($"RESULT draw_calls={draws}");
        GD.Print($"RESULT objects={objs}");
        GD.Print($"RESULT primitives={prims}");
        GD.Print($"RESULT video_mem_mb={vmem / 1024.0 / 1024.0:F2}");
        GD.Print($"RESULT buffer_mem_mb={bmem / 1024.0 / 1024.0:F2}");
        GD.Print($"RESULT render_nodes={_nodeCount}");
        GD.Print($"RESULT build_ms={_buildMs}");

        if (_backend == "gridmap_two")
        {
            // The capability under test: ONE cell carrying BOTH a floor and a wall.
            int both = 0, wallOnly = 0, floorOnly = 0;
            for (int y = 0; y < SizeY; y++)
                for (int x = 0; x < SizeX; x++)
                {
                    var p = new Vector3I(x, -TopLayer, y);
                    bool hasWall = _gridMap.GetCellItem(p) != GridMap.InvalidCellItem;
                    bool hasFloor = _floorMap.GetCellItem(p) != GridMap.InvalidCellItem;
                    if (hasWall && hasFloor) both++;
                    else if (hasWall) wallOnly++;
                    else if (hasFloor) floorOnly++;
                }
            GD.Print($"RESULT cells_with_BOTH_floor_and_wall={both}");
            GD.Print($"RESULT cells_wall_only={wallOnly}  cells_floor_only={floorOnly}");

            // And that the model agrees: same layer, counted from TerrainWorld.
            int modelBoth = 0;
            for (int y = 0; y < SizeY; y++)
                for (int x = 0; x < SizeX; x++)
                {
                    TerrainCell c = _world.GetCell(new CellCoord(x, y, TopLayer));
                    if (c.FloorTypeId != 0 && c.WallTypeId != 0) modelBoth++;
                }
            GD.Print($"RESULT model_cells_with_both={modelBoth}  render_matches_model={both == modelBoth}");
        }

        Image img = GetViewport().GetTexture().GetImage();
        img.SavePng($"user://terrain-spike-{_backend}.png");
        GD.Print($"RESULT screenshot={ProjectSettings.GlobalizePath($"user://terrain-spike-{_backend}.png")}");
    }

    /// <summary>The steady-state cost that actually matters: a colonist finishes a dig.</summary>
    private void DigRebuildBench()
    {
        const int digs = 200;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < digs; i++)
        {
            int x = 20 + i % 40, y = 20 + (i / 40) % 40, z = TopLayer;
            var c = new CellCoord(x, y, z);
            using (_window.Open()) _world.ClearWall(c);

            if (_backend == "gridmap")
                _gridMap.SetCellItem(new Vector3I(x, -z, y), _floor);      // native per-cell update
            else if (_backend == "gridmap_two")
                _gridMap.SetCellItem(new Vector3I(x, -z, y), -1);          // clear the wall; floor already there
            else
                RebuildChunk(_world.ChunkOf(c));                            // rebuild the dirty chunk
        }
        sw.Stop();
        GD.Print($"RESULT dig_rebuild_us_per_dig={sw.Elapsed.TotalMilliseconds / digs * 1000:F2}");
        ConcentratedAoeBench();
    }

    /// <summary>
    /// godot-specialist finding: the per-dig number was measured on SCATTERED single-cell
    /// changes. A combat AoE is spatially CONCENTRATED — many cells inside one or two octants
    /// in a single frame, during TurnBased where a hitch is maximally visible. Measure it
    /// rather than extrapolate.
    /// </summary>
    private void ConcentratedAoeBench()
    {
        if (_backend is not ("gridmap" or "gridmap_two")) return;

        foreach (int n in new[] { 8, 27, 64, 125 })
        {
            // A compact cube of cells, deliberately inside ONE octant (octant >= 32 here).
            int side = (int)Math.Round(Math.Pow(n, 1.0 / 3.0));
            var sw = Stopwatch.StartNew();
            int touched = 0;
            for (int dx = 0; dx < side; dx++)
                for (int dy = 0; dy < side; dy++)
                    for (int dz = 0; dz < side && dz < VisibleLayers; dz++)
                    {
                        _gridMap.SetCellItem(new Vector3I(40 + dx, -(TopLayer + dz), 40 + dy), -1);
                        touched++;
                    }
            sw.Stop();
            GD.Print($"RESULT aoe_cells={touched} same_octant_us={sw.Elapsed.TotalMilliseconds * 1000:F1} " +
                     $"us_per_cell={sw.Elapsed.TotalMilliseconds * 1000 / Math.Max(1, touched):F2}");
        }
    }

    private sealed class NullSink : ITerrainChangeSink
    {
        public void Publish(in TerrainChangeBatch batch) { }
        public void PublishWorldReloaded() { }
    }
}
