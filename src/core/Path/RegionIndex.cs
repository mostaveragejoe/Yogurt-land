using Hollowdeep.Core.Primitives;
using Hollowdeep.Core.Terrain;

namespace Hollowdeep.Core.Path;

/// <summary>
/// Amortized per-layer region connectivity for reachability queries (§4d). Never runs a
/// full-region flood per dig: terrain changes only mark layers dirty; dirty layers
/// rebuild lazily on the next query, and stair links union regions across layers.
/// Uses the SAME connectivity rules as A* (colonist-RT physical openness + the diagonal
/// rule); occupancy is advisory in RT and never consulted here.
/// </summary>
public sealed class RegionIndex
{
    private const int MaxRegionsPerLayer = 4096;

    private readonly TerrainWorld _terrain;
    private readonly Walkability _walk;
    private readonly int _w, _h, _d, _layerSize;

    private readonly int[] _labels;      // per cell; -1 = not walkable
    private readonly bool[] _layerDirty;
    private bool _linksDirty;
    private readonly int[] _dsu;         // parent per label id (layer z owns [z*Max, (z+1)*Max))
    private readonly Queue<int> _floodQueue = new();

    public RegionIndex(TerrainWorld terrain, Walkability walk)
    {
        _terrain = terrain;
        _walk = walk;
        _w = terrain.Width; _h = terrain.Height; _d = terrain.Depth;
        _layerSize = _w * _h;
        _labels = new int[_layerSize * _d];
        _layerDirty = new bool[_d];
        _dsu = new int[MaxRegionsPerLayer * _d];
        for (int z = 0; z < _d; z++) _layerDirty[z] = true;
        _linksDirty = true;
        terrain.Changed += OnTerrainChanged;
    }

    /// <summary>Bus handler: idempotent dirty-marking only (ADR-0002).</summary>
    private void OnTerrainChanged(TerrainChangeBatch batch)
    {
        for (int i = 0; i < batch.Count; i++)
        {
            _layerDirty[batch[i].Cell.Z] = true;
            // A wall change beside a layer boundary can invalidate a stair link either way.
            _linksDirty = true;
        }
    }

    public void MarkAllDirty()
    {
        for (int z = 0; z < _d; z++) _layerDirty[z] = true;
        _linksDirty = true;
    }

    /// <summary>Colonist-RT reachability between two cells.</summary>
    public bool Reachable(CellCoord a, CellCoord b)
    {
        EnsureClean();
        if (!_terrain.InBounds(a) || !_terrain.InBounds(b)) return false;
        int la = _labels[Index(a)], lb = _labels[Index(b)];
        if (la < 0 || lb < 0) return false;
        return Find(la) == Find(lb);
    }

    /// <summary>Can a colonist standing at <paramref name="from"/> reach any same-layer 8-neighbor of <paramref name="target"/>?</summary>
    public bool ReachableAdjacent(CellCoord from, CellCoord target)
    {
        EnsureClean();
        if (!_terrain.InBounds(from)) return false;
        int lf = _labels[Index(from)];
        if (lf < 0) return false;
        int rootFrom = Find(lf);
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            if (dx == 0 && dy == 0) continue;
            var n = target.Offset(dx, dy);
            if (!_terrain.InBounds(n)) continue;
            int ln = _labels[Index(n)];
            if (ln >= 0 && Find(ln) == rootFrom) return true;
        }
        return false;
    }

    private int Index(CellCoord c) => c.Z * _layerSize + c.Y * _w + c.X;

    private void EnsureClean()
    {
        bool rebuilt = false;
        for (int z = 0; z < _d; z++)
        {
            if (!_layerDirty[z]) continue;
            RebuildLayer(z);
            _layerDirty[z] = false;
            rebuilt = true;
        }
        if (rebuilt || _linksDirty)
        {
            RebuildLinks();
            _linksDirty = false;
        }
    }

    private void RebuildLayer(int z)
    {
        int baseIdx = z * _layerSize;
        int nextLocal = 0;
        for (int i = 0; i < _layerSize; i++) _labels[baseIdx + i] = -1;

        for (int y = 0; y < _h; y++)
        for (int x = 0; x < _w; x++)
        {
            int idx = baseIdx + y * _w + x;
            if (_labels[idx] != -1) continue;
            var seed = new CellCoord(x, y, z);
            if (!_walk.PhysicallyOpen(seed, MoveContext.ColonistRealTime)) continue;
            if (nextLocal >= MaxRegionsPerLayer)
                throw new InvalidOperationException($"Region overflow on layer {z}.");
            int label = z * MaxRegionsPerLayer + nextLocal++;
            Flood(seed, label);
        }
    }

    private void Flood(CellCoord seed, int label)
    {
        _floodQueue.Clear();
        _labels[Index(seed)] = label;
        _floodQueue.Enqueue(Index(seed));
        var steps = Steps.Planar;
        while (_floodQueue.Count > 0)
        {
            int idx = _floodQueue.Dequeue();
            int rem = idx - seed.Z * _layerSize;
            var cur = new CellCoord(rem % _w, rem / _w, seed.Z);
            for (int s = 0; s < steps.Length; s++)
            {
                var (dx, dy, _) = steps[s];
                var next = cur.Offset(dx, dy);
                if (!_terrain.InBounds(next)) continue;
                int nIdx = Index(next);
                if (_labels[nIdx] != -1) continue;
                if (!_walk.PhysicallyOpen(next, MoveContext.ColonistRealTime)) continue;
                if (dx != 0 && dy != 0 && !_walk.DiagonalLegal(cur, dx, dy, MoveContext.ColonistRealTime)) continue;
                _labels[nIdx] = label;
                _floodQueue.Enqueue(nIdx);
            }
        }
    }

    private void RebuildLinks()
    {
        for (int i = 0; i < _dsu.Length; i++) _dsu[i] = i;
        // Stair cells are the lower endpoints of Z links; scan is cheap and amortized.
        for (int z = 0; z < _d - 1; z++)
        {
            int baseIdx = z * _layerSize;
            for (int i = 0; i < _layerSize; i++)
            {
                int lower = _labels[baseIdx + i];
                if (lower < 0) continue;
                var c = new CellCoord(i % _w, i / _w, z);
                if (!_walk.HasStairLink(c)) continue;
                int upper = _labels[Index(c.Up)];
                if (upper >= 0) Union(lower, upper);
            }
        }
    }

    private int Find(int x)
    {
        while (_dsu[x] != x)
        {
            _dsu[x] = _dsu[_dsu[x]];
            x = _dsu[x];
        }
        return x;
    }

    private void Union(int a, int b)
    {
        int ra = Find(a), rb = Find(b);
        if (ra != rb) _dsu[Math.Max(ra, rb)] = Math.Min(ra, rb);
    }
}
