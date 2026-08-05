using Hollowdeep.Core.Entities;
using Hollowdeep.Core.Primitives;
using Hollowdeep.Core.Terrain;

namespace Hollowdeep.Core.Path;

public enum GoalMode : byte
{
    Exact = 0,
    /// <summary>Stop on any same-layer 8-neighbor of the goal (dig/attack-wall approach).</summary>
    Adjacent8 = 1,
}

/// <summary>
/// Allocation-free deterministic A* (§4d): 8-connected, integer octile costs 10/14,
/// octile heuristic, index-tiebroken open heap, corner-cutting per the diagonal rule,
/// stairs linking Z↔Z+1. The breach variant lets raiders plan through walls/doors at a
/// surcharge proportional to hits-to-break — used to pick which wall to attack.
/// </summary>
public sealed class Pathfinder
{
    private readonly TerrainWorld _terrain;
    private readonly DoorStore _doors;
    private readonly Walkability _walk;

    private readonly int _w, _h, _d, _layerSize;
    private readonly int[] _g;
    private readonly int[] _parent;
    private readonly int[] _openEpoch;
    private readonly int[] _closedEpoch;
    private int _epoch;

    // Binary min-heap of (key = cost<<24 | nodeIndex) — equal costs pop lowest index first.
    private long[] _heap;
    private int _heapCount;

    public Pathfinder(TerrainWorld terrain, DoorStore doors, Walkability walk)
    {
        _terrain = terrain;
        _doors = doors;
        _walk = walk;
        _w = terrain.Width; _h = terrain.Height; _d = terrain.Depth;
        _layerSize = _w * _h;
        int n = _layerSize * _d;
        _g = new int[n];
        _parent = new int[n];
        _openEpoch = new int[n];
        _closedEpoch = new int[n];
        _heap = new long[1024];
    }

    private int Index(CellCoord c) => c.Z * _layerSize + c.Y * _w + c.X;
    private CellCoord Cell(int idx)
    {
        int z = idx / _layerSize;
        int rem = idx - z * _layerSize;
        return new CellCoord(rem % _w, rem / _w, z);
    }

    private int Heuristic(CellCoord a, CellCoord b)
        => CellCoord.Octile(a, b) + 10 * Math.Abs(a.Z - b.Z);

    /// <summary>Cost to enter a cell in breach planning; -1 = truly impassable.</summary>
    private int BreachEnterCost(CellCoord c, int baseCost)
    {
        if (!_terrain.InBounds(c)) return -1;
        var cell = _terrain.Get(c);
        if (cell.HasWall)
        {
            int hits = (cell.WallHp + GameConfig.RaiderWallDamagePerHit - 1) / GameConfig.RaiderWallDamagePerHit;
            return baseCost + hits * 30; // one hit ≈ three steps of walking time
        }
        if (!cell.HasFloor) return -1;
        if (_doors.TryGetAt(c, out var door) && door.Blocks)
        {
            int hits = (door.Hp + GameConfig.RaiderWallDamagePerHit - 1) / GameConfig.RaiderWallDamagePerHit;
            return baseCost + hits * 30;
        }
        return baseCost;
    }

    private bool BreachOpen(CellCoord c)
        => _terrain.InBounds(c) && (_terrain.Get(c).HasWall || _terrain.Get(c).HasFloor);

    /// <summary>
    /// Finds a path (excluding start, including destination) into outPath. Returns false —
    /// with outPath empty, never stale — when the goal is sealed or beyond maxCost.
    /// </summary>
    public bool FindPath(CellCoord start, CellCoord goal, MoveContext ctx, EntityId mover,
        GoalMode goalMode, List<CellCoord> outPath, bool breach = false, int maxCost = int.MaxValue)
    {
        outPath.Clear();
        if (!_terrain.InBounds(start) || !_terrain.InBounds(goal)) return false;
        if (GoalReached(start, goal, goalMode)) return true; // already there: empty path

        _epoch++;
        _heapCount = 0;
        int startIdx = Index(start);
        _g[startIdx] = 0;
        _parent[startIdx] = -1;
        _openEpoch[startIdx] = _epoch;
        Push(Heuristic(start, goal), startIdx);

        while (_heapCount > 0)
        {
            long key = Pop();
            int idx = (int)(key & 0xFFFFFF);
            if (_closedEpoch[idx] == _epoch) continue;
            _closedEpoch[idx] = _epoch;
            var cur = Cell(idx);
            int g = _g[idx];

            if (GoalReached(cur, goal, goalMode))
            {
                Reconstruct(idx, startIdx, outPath);
                return true;
            }

            // Planar steps, orthogonals first (order is part of determinism).
            var steps = Steps.Planar;
            for (int s = 0; s < steps.Length; s++)
            {
                var (dx, dy, cost) = steps[s];
                var next = cur.Offset(dx, dy);
                if (!_terrain.InBounds(next)) continue;
                bool diagonal = dx != 0 && dy != 0;
                if (breach)
                {
                    if (diagonal && !(BreachOpen(cur.Offset(dx, 0)) || BreachOpen(cur.Offset(0, dy)))) continue;
                    int enter = BreachEnterCost(next, cost);
                    if (enter < 0) continue;
                    Relax(idx, next, g + enter, goal, maxCost);
                }
                else
                {
                    if (!_walk.CanEnter(next, ctx, mover)) continue;
                    if (diagonal && !_walk.DiagonalLegal(cur, dx, dy, ctx)) continue;
                    Relax(idx, next, g + cost + _walk.EnterSurcharge(next, ctx), goal, maxCost);
                }
            }

            // Stair links: this cell is the lower endpoint, or the cell below is.
            if (_walk.HasStairLink(cur))
            {
                var up = cur.Up;
                if (_terrain.InBounds(up) && (breach ? BreachEnterCost(up, 10) >= 0 : _walk.CanEnter(up, ctx, mover)))
                    Relax(idx, up, g + 10 + (breach ? 0 : _walk.EnterSurcharge(up, ctx)), goal, maxCost);
            }
            var down = cur.Down;
            if (_terrain.InBounds(down) && _walk.HasStairLink(down))
            {
                if (breach ? BreachEnterCost(down, 10) >= 0 : _walk.CanEnter(down, ctx, mover))
                    Relax(idx, down, g + 10 + (breach ? 0 : _walk.EnterSurcharge(down, ctx)), goal, maxCost);
            }
        }
        return false;
    }

    private bool GoalReached(CellCoord c, CellCoord goal, GoalMode mode) => mode switch
    {
        GoalMode.Exact => c == goal,
        GoalMode.Adjacent8 => c.Z == goal.Z && c != goal &&
                              Math.Abs(c.X - goal.X) <= 1 && Math.Abs(c.Y - goal.Y) <= 1,
        _ => false,
    };

    private void Relax(int fromIdx, CellCoord next, int tentativeG, CellCoord goal, int maxCost)
    {
        if (tentativeG > maxCost) return;
        int nIdx = Index(next);
        if (_closedEpoch[nIdx] == _epoch) return;
        if (_openEpoch[nIdx] == _epoch && _g[nIdx] <= tentativeG) return;
        _g[nIdx] = tentativeG;
        _parent[nIdx] = fromIdx;
        _openEpoch[nIdx] = _epoch;
        Push(tentativeG + Heuristic(next, goal), nIdx);
    }

    private void Reconstruct(int goalIdx, int startIdx, List<CellCoord> outPath)
    {
        int idx = goalIdx;
        while (idx != startIdx && idx >= 0)
        {
            outPath.Add(Cell(idx));
            idx = _parent[idx];
        }
        outPath.Reverse();
    }

    /// <summary>
    /// Dijkstra flood of every cell reachable within maxCost (TB move range display and
    /// AI). Fills outCells with (cell, cost) pairs in deterministic pop order.
    /// </summary>
    public void FloodCosts(CellCoord start, MoveContext ctx, EntityId mover, int maxCost,
        List<(CellCoord Cell, int Cost)> outCells)
    {
        outCells.Clear();
        if (!_terrain.InBounds(start)) return;
        _epoch++;
        _heapCount = 0;
        int startIdx = Index(start);
        _g[startIdx] = 0;
        _openEpoch[startIdx] = _epoch;
        Push(0, startIdx);

        while (_heapCount > 0)
        {
            long key = Pop();
            int idx = (int)(key & 0xFFFFFF);
            if (_closedEpoch[idx] == _epoch) continue;
            _closedEpoch[idx] = _epoch;
            var cur = Cell(idx);
            int g = _g[idx];
            if (idx != startIdx) outCells.Add((cur, g));

            var steps = Steps.Planar;
            for (int s = 0; s < steps.Length; s++)
            {
                var (dx, dy, cost) = steps[s];
                var next = cur.Offset(dx, dy);
                if (!_terrain.InBounds(next)) continue;
                if (!_walk.CanEnter(next, ctx, mover)) continue;
                if (dx != 0 && dy != 0 && !_walk.DiagonalLegal(cur, dx, dy, ctx)) continue;
                FloodRelax(idx, next, g + cost + _walk.EnterSurcharge(next, ctx), maxCost);
            }
            if (_walk.HasStairLink(cur) && _walk.CanEnter(cur.Up, ctx, mover))
                FloodRelax(idx, cur.Up, g + 10 + _walk.EnterSurcharge(cur.Up, ctx), maxCost);
            var down = cur.Down;
            if (_terrain.InBounds(down) && _walk.HasStairLink(down) && _walk.CanEnter(down, ctx, mover))
                FloodRelax(idx, down, g + 10 + _walk.EnterSurcharge(down, ctx), maxCost);
        }
    }

    private void FloodRelax(int fromIdx, CellCoord next, int tentativeG, int maxCost)
    {
        if (tentativeG > maxCost) return;
        int nIdx = Index(next);
        if (_closedEpoch[nIdx] == _epoch) return;
        if (_openEpoch[nIdx] == _epoch && _g[nIdx] <= tentativeG) return;
        _g[nIdx] = tentativeG;
        _parent[nIdx] = fromIdx;
        _openEpoch[nIdx] = _epoch;
        Push(tentativeG, nIdx);
    }

    // ---- heap ----

    private void Push(int cost, int nodeIdx)
    {
        if (_heapCount == _heap.Length)
            Array.Resize(ref _heap, _heap.Length * 2); // one-time growth, steady-state allocation-free
        long key = ((long)cost << 24) | (uint)nodeIdx;
        int i = _heapCount++;
        _heap[i] = key;
        while (i > 0)
        {
            int p = (i - 1) >> 1;
            if (_heap[p] <= _heap[i]) break;
            (_heap[p], _heap[i]) = (_heap[i], _heap[p]);
            i = p;
        }
    }

    private long Pop()
    {
        long top = _heap[0];
        _heap[0] = _heap[--_heapCount];
        int i = 0;
        while (true)
        {
            int l = 2 * i + 1, r = l + 1, m = i;
            if (l < _heapCount && _heap[l] < _heap[m]) m = l;
            if (r < _heapCount && _heap[r] < _heap[m]) m = r;
            if (m == i) break;
            (_heap[m], _heap[i]) = (_heap[i], _heap[m]);
            i = m;
        }
        return top;
    }
}
