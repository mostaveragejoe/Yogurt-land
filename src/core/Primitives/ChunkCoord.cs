namespace Hollowdeep.Core.Primitives;

/// <summary>
/// Identity of a terrain chunk (ADR-0002). Chunk identity is public —
/// subscribers dirty-track by chunk — but chunk SIZE is a queryable tunable
/// (<c>TerrainWorld.ChunkSize</c>), never a caller-side constant. The only
/// sanctioned <see cref="CellCoord"/>→<see cref="ChunkCoord"/> mapping is
/// <c>TerrainWorld.ChunkOf()</c>; caller-side chunk math is a forbidden pattern.
/// </summary>
public readonly record struct ChunkCoord(int X, int Y, int Z);
