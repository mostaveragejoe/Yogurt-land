// PROTOTYPE - NOT FOR PRODUCTION
// Question: A* over the ADR-0002 grid with ADR-0003 composite walkability — correct under
//   mid-route digs, deterministic, and allocation-free in steady state?
// Date: 2026-07-26

namespace PathfindingSpike;

public enum PathStatus { Found, NoPath, SameCell, InvalidStart, InvalidGoal }

public readonly record struct PathResult(PathStatus Status, int Cost, int NodesExpanded);

/// <summary>
/// Grid A* with generation-stamped scratch arrays: no per-query allocation, no array clearing.
/// Neighbours are 4-connected horizontally plus stair Z-linkage (ADR-0002).
/// </summary>
public sealed class Pathfinder(CompositeWalkability walk, ushort stairFloorTypeId)
{
    private readonly TerrainWorld _world = walk.World;
    private readonly int _sx = walk.World.Bounds.SizeX, _sy = walk.World.Bounds.SizeY, _sz = walk.World.Bounds.Layers;

    private int[] _g = [];
    private int[] _cameFrom = [];
    private int[] _stamp = [];
    private int[] _heapNode = [];
    private int[] _heapF = [];
    private int _heapCount;
    private int _generation;

    public ushort StairFloorTypeId => stairFloorTypeId;
    public long TerrainRevisionAtLastSearch { get; private set; }

    private void EnsureCapacity()
    {
        int n = _sx * _sy * _sz;
        if (_g.Length == n) return;
        _g = new int[n]; _cameFrom = new int[n]; _stamp = new int[n];
        _heapNode = new int[n + 1]; _heapF = new int[n + 1];
    }

    private int Index(CellCoord c) => (c.Z * _sy + c.Y) * _sx + c.X;
    private CellCoord Coord(int i)
    {
        int x = i % _sx, rest = i / _sx;
        return new CellCoord(x, rest % _sy, rest / _sy);
    }

    /// <summary>Stair rule (ADR-0002): a floor declaring Z-linkage connects Z and Z+1 (Z is DOWN).</summary>
    private bool IsStair(CellCoord c) => _world.GetCell(c).FloorTypeId == stairFloorTypeId;

    private static readonly (int dx, int dy)[] Horizontal = [(1, 0), (-1, 0), (0, 1), (0, -1)];

    /// <summary>Fills <paramref name="path"/> (reused by the caller) with start..goal inclusive.</summary>
    public PathResult FindPath(CellCoord start, CellCoord goal, TimeAuthorityMode mode,
                               EntityId mover, List<CellCoord> path)
    {
        EnsureCapacity();
        path.Clear();
        TerrainRevisionAtLastSearch = (long)_world.Revision;

        if (start == goal) { path.Add(start); return new PathResult(PathStatus.SameCell, 0, 0); }
        if (!walk.IsWalkable(start, mode, mover)) return new PathResult(PathStatus.InvalidStart, 0, 0);
        if (!walk.IsWalkable(goal, mode, mover)) return new PathResult(PathStatus.InvalidGoal, 0, 0);

        _generation++;
        _heapCount = 0;
        int startIdx = Index(start), goalIdx = Index(goal);
        _g[startIdx] = 0; _cameFrom[startIdx] = -1; _stamp[startIdx] = _generation;
        HeapPush(startIdx, Heuristic(start, goal));

        int expanded = 0;
        while (_heapCount > 0)
        {
            int current = HeapPop();
            if (current == goalIdx)
            {
                int cost = _g[current];
                for (int at = current; at != -1; at = _cameFrom[at]) path.Add(Coord(at));
                path.Reverse();
                return new PathResult(PathStatus.Found, cost, expanded);
            }
            expanded++;
            CellCoord cc = Coord(current);

            for (int d = 0; d < Horizontal.Length; d++)
                Relax(cc, new CellCoord(cc.X + Horizontal[d].dx, cc.Y + Horizontal[d].dy, cc.Z),
                      current, goal, mode, mover);

            // Stair links: descend if THIS cell is a stair; ascend if the cell above is a stair.
            if (IsStair(cc)) Relax(cc, new CellCoord(cc.X, cc.Y, cc.Z + 1), current, goal, mode, mover);
            var above = new CellCoord(cc.X, cc.Y, cc.Z - 1);
            if (_world.InBounds(above) && IsStair(above)) Relax(cc, above, current, goal, mode, mover);
        }
        return new PathResult(PathStatus.NoPath, 0, expanded);
    }

    private void Relax(CellCoord from, CellCoord to, int currentIdx, CellCoord goal,
                       TimeAuthorityMode mode, EntityId mover)
    {
        if (!_world.InBounds(to) || !walk.IsWalkable(to, mode, mover)) return;
        int ni = Index(to);
        int tentative = _g[currentIdx] + walk.StepCost(to, mode);
        if (_stamp[ni] == _generation && tentative >= _g[ni]) return;
        _stamp[ni] = _generation;
        _g[ni] = tentative;
        _cameFrom[ni] = currentIdx;
        HeapPush(ni, tentative + Heuristic(to, goal));
    }

    private static int Heuristic(CellCoord a, CellCoord b) =>
        Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) + Math.Abs(a.Z - b.Z);

    // Deterministic binary min-heap; ties broken by node index so results are reproducible.
    private void HeapPush(int node, int f)
    {
        int i = ++_heapCount;
        _heapNode[i] = node; _heapF[i] = f;
        while (i > 1)
        {
            int p = i >> 1;
            if (_heapF[p] < _heapF[i] || (_heapF[p] == _heapF[i] && _heapNode[p] <= _heapNode[i])) break;
            (_heapNode[p], _heapNode[i]) = (_heapNode[i], _heapNode[p]);
            (_heapF[p], _heapF[i]) = (_heapF[i], _heapF[p]);
            i = p;
        }
    }

    private int HeapPop()
    {
        int top = _heapNode[1];
        _heapNode[1] = _heapNode[_heapCount]; _heapF[1] = _heapF[_heapCount];
        _heapCount--;
        int i = 1;
        while (true)
        {
            int l = i << 1, r = l + 1, best = i;
            if (l <= _heapCount && (_heapF[l] < _heapF[best] || (_heapF[l] == _heapF[best] && _heapNode[l] < _heapNode[best]))) best = l;
            if (r <= _heapCount && (_heapF[r] < _heapF[best] || (_heapF[r] == _heapF[best] && _heapNode[r] < _heapNode[best]))) best = r;
            if (best == i) break;
            (_heapNode[best], _heapNode[i]) = (_heapNode[i], _heapNode[best]);
            (_heapF[best], _heapF[i]) = (_heapF[i], _heapF[best]);
            i = best;
        }
        return top;
    }
}

/// <summary>
/// Connectivity / reachability index. Pathfinding owns it (ADR-0002 firewall).
/// Rebuilt by flood fill; the spike measures whether a full rebuild per dig is affordable.
/// </summary>
public sealed class RegionIndex(CompositeWalkability walk)
{
    private readonly TerrainWorld _world = walk.World;
    private int[] _label = [];
    private int[] _queue = [];
    public int RegionCount { get; private set; }
    public long BuiltAtTerrainRevision { get; private set; } = -1;
    public int RebuildCount { get; private set; }

    public void Rebuild()
    {
        WorldBounds b = _world.Bounds;
        int n = b.SizeX * b.SizeY * b.Layers;
        if (_label.Length != n) { _label = new int[n]; _queue = new int[n]; }
        Array.Fill(_label, 0);
        RegionCount = 0;
        RebuildCount++;
        BuiltAtTerrainRevision = (long)_world.Revision;

        for (int z = 0; z < b.Layers; z++)
            for (int y = 0; y < b.SizeY; y++)
                for (int x = 0; x < b.SizeX; x++)
                {
                    var c = new CellCoord(x, y, z);
                    int idx = Index(c, b);
                    if (_label[idx] != 0 || !walk.IsWalkable(c, TimeAuthorityMode.RealTime, EntityId.None)) continue;

                    int label = ++RegionCount;
                    int head = 0, tail = 0;
                    _queue[tail++] = idx; _label[idx] = label;
                    while (head < tail)
                    {
                        CellCoord cur = Coord(_queue[head++], b);
                        Span<CellCoord> nbrs =
                        [
                            new(cur.X + 1, cur.Y, cur.Z), new(cur.X - 1, cur.Y, cur.Z),
                            new(cur.X, cur.Y + 1, cur.Z), new(cur.X, cur.Y - 1, cur.Z),
                            new(cur.X, cur.Y, cur.Z + 1), new(cur.X, cur.Y, cur.Z - 1),
                        ];
                        for (int k = 0; k < nbrs.Length; k++)
                        {
                            CellCoord nb = nbrs[k];
                            if (k >= 4)   // vertical links require a stair
                            {
                                CellCoord stairCell = k == 4 ? cur : nb;
                                if (!_world.InBounds(stairCell) ||
                                    _world.GetCell(stairCell).FloorTypeId != _stairId) continue;
                            }
                            if (!_world.InBounds(nb)) continue;
                            int ni = Index(nb, b);
                            if (_label[ni] != 0 || !walk.IsWalkable(nb, TimeAuthorityMode.RealTime, EntityId.None)) continue;
                            _label[ni] = label;
                            _queue[tail++] = ni;
                        }
                    }
                }
    }

    private ushort _stairId;
    public void SetStairId(ushort id) => _stairId = id;

    private static int Index(CellCoord c, WorldBounds b) => (c.Z * b.SizeY + c.Y) * b.SizeX + c.X;
    private static CellCoord Coord(int i, WorldBounds b)
    {
        int x = i % b.SizeX, rest = i / b.SizeX;
        return new CellCoord(x, rest % b.SizeY, rest / b.SizeY);
    }

    public int LabelOf(CellCoord c) => _label.Length == 0 ? 0 : _label[Index(c, _world.Bounds)];

    /// <summary>O(1) "is it even worth running A*?" — the point of the index.</summary>
    public bool AreConnected(CellCoord a, CellCoord b)
    {
        int la = LabelOf(a);
        return la != 0 && la == LabelOf(b);
    }

    public bool IsStale => BuiltAtTerrainRevision != (long)_world.Revision;
}

/// <summary>
/// Cached path with Revision-polling invalidation (ADR-0003's mechanism, no entity event bus).
/// The spike measures whether polling + revalidate is affordable, since ADR-0003 pre-planned a
/// narrow change-list upgrade if it is not.
/// </summary>
public sealed class PathCache(CompositeWalkability walk, Pathfinder pathfinder)
{
    public sealed class Entry
    {
        public required EntityId Owner;
        public required CellCoord Goal;
        public required List<CellCoord> Path;
        public int Cursor;
        public long TerrainRevision;
        public long DoorRevision;
    }

    private readonly List<Entry> _entries = [];
    public int RevalidationCount { get; private set; }
    public int RecomputeCount { get; private set; }
    public int InvalidatedCount { get; private set; }
    public IReadOnlyList<Entry> Entries => _entries;

    public Entry Add(EntityId owner, CellCoord from, CellCoord goal, TimeAuthorityMode mode)
    {
        var path = new List<CellCoord>(256);
        pathfinder.FindPath(from, goal, mode, owner, path);
        var e = new Entry
        {
            Owner = owner, Goal = goal, Path = path,
            TerrainRevision = (long)walk.World.Revision, DoorRevision = walk.Doors.Revision,
        };
        _entries.Add(e);
        return e;
    }

    /// <summary>Poll revisions; only re-validate entries whose world actually moved.</summary>
    public void PollAndRevalidate(TimeAuthorityMode mode)
    {
        long terrainRev = (long)walk.World.Revision, doorRev = walk.Doors.Revision;
        foreach (Entry e in _entries)
        {
            if (e.TerrainRevision == terrainRev && e.DoorRevision == doorRev) continue;
            e.TerrainRevision = terrainRev; e.DoorRevision = doorRev;
            RevalidationCount++;

            bool stillValid = true;
            for (int i = e.Cursor; i < e.Path.Count; i++)
                if (!walk.IsWalkable(e.Path[i], mode, e.Owner)) { stillValid = false; break; }
            if (stillValid) continue;

            InvalidatedCount++;
            CellCoord from = e.Path.Count > 0 ? e.Path[Math.Min(e.Cursor, e.Path.Count - 1)] : e.Goal;
            pathfinder.FindPath(from, e.Goal, mode, e.Owner, e.Path);
            e.Cursor = 0;
            RecomputeCount++;
        }
    }
}
