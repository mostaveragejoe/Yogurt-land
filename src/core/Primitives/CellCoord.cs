namespace Hollowdeep.Core.Primitives;

/// <summary>A cell position in the layered world grid. Z is the layer (0 = deepest).</summary>
public readonly record struct CellCoord(int X, int Y, int Z)
{
    public static readonly CellCoord Zero = new(0, 0, 0);

    public CellCoord WithZ(int z) => new(X, Y, z);
    public CellCoord Offset(int dx, int dy) => new(X + dx, Y + dy, Z);
    public CellCoord Up => new(X, Y, Z + 1);
    public CellCoord Down => new(X, Y, Z - 1);

    /// <summary>Chessboard distance on the same layer; layers add their absolute delta.</summary>
    public static int Chebyshev(CellCoord a, CellCoord b)
        => Math.Max(Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y)), Math.Abs(a.Z - b.Z));

    /// <summary>Integer octile distance (10 orthogonal / 14 diagonal) ignoring Z.</summary>
    public static int Octile(CellCoord a, CellCoord b)
    {
        int dx = Math.Abs(a.X - b.X);
        int dy = Math.Abs(a.Y - b.Y);
        return dx > dy ? 10 * dx + 4 * dy : 10 * dy + 4 * dx;
    }

    public override string ToString() => $"({X},{Y},{Z})";
}

/// <summary>Identifies one 32x32 chunk of one layer.</summary>
public readonly record struct ChunkCoord(int Cx, int Cy, int Z)
{
    public override string ToString() => $"chunk({Cx},{Cy} z{Z})";
}

/// <summary>The eight planar step directions, orthogonals first. Index order is part of determinism.</summary>
public static class Steps
{
    // dx, dy, octile cost. Orthogonals first, then diagonals; stable order matters for tie-breaks.
    public static readonly (int Dx, int Dy, int Cost)[] Planar =
    {
        (1, 0, 10), (-1, 0, 10), (0, 1, 10), (0, -1, 10),
        (1, 1, 14), (1, -1, 14), (-1, 1, 14), (-1, -1, 14),
    };
}
