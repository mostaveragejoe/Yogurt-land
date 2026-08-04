using System.Runtime.InteropServices;

namespace Hollowdeep.Core.Terrain;

public enum FloorKind : byte
{
    None = 0,
    Natural = 1,
    Built = 2,
    Stair = 3, // links this cell to the layer above (see Walkability)
}

public enum WallKind : byte
{
    None = 0,
    Natural = 1,
    Built = 2,
}

public enum MaterialId : byte
{
    None = 0,
    Dirt = 1,
    Granite = 2,
    Reinforced = 3,
}

/// <summary>
/// Packed 8-byte cell (ADR-0002, AoS). Architecture ONLY: floor and wall, their
/// materials, and wall HP. Never occupants, plans, zones, or combat state.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 8)]
public struct TerrainCell : IEquatable<TerrainCell>
{
    public FloorKind Floor;
    public MaterialId FloorMaterial;
    public WallKind Wall;
    public MaterialId WallMaterial;
    public ushort WallHp;
    public ushort Reserved;

    public readonly bool HasWall => Wall != WallKind.None;
    public readonly bool HasFloor => Floor != FloorKind.None;

    public readonly bool Equals(TerrainCell other) =>
        Floor == other.Floor && FloorMaterial == other.FloorMaterial &&
        Wall == other.Wall && WallMaterial == other.WallMaterial &&
        WallHp == other.WallHp && Reserved == other.Reserved;

    public override readonly bool Equals(object? obj) => obj is TerrainCell c && Equals(c);
    public override readonly int GetHashCode() =>
        HashCode.Combine(Floor, FloorMaterial, Wall, WallMaterial, WallHp, Reserved);
}

public sealed record MaterialDef(MaterialId Id, string Name, int MaxWallHp, float DigSeconds);

public static class MaterialCatalog
{
    private static readonly MaterialDef[] Defs =
    {
        new(MaterialId.None, "none", 0, 0f),
        new(MaterialId.Dirt, "dirt", GameConfig.DirtWallHp, GameConfig.DirtDigSeconds),
        new(MaterialId.Granite, "granite", GameConfig.GraniteWallHp, GameConfig.GraniteDigSeconds),
        new(MaterialId.Reinforced, "reinforced", GameConfig.ReinforcedWallHp, GameConfig.ReinforcedDigSeconds),
    };

    public static MaterialDef Get(MaterialId id) => Defs[(int)id];

    /// <summary>Stable name→id manifest written into every save (ADR-0004).</summary>
    public static IReadOnlyList<MaterialDef> All => Defs;
}
