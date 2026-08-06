namespace Hollowdeep.Core.Primitives;

/// <summary>
/// A cell position in the simulation grid. Shared foundation primitive
/// (ADR-0002); owned by no system — changing it is an ADR-level event.
/// </summary>
/// <param name="X">Horizontal position within a layer.</param>
/// <param name="Y">Horizontal position within a layer.</param>
/// <param name="Z">
/// Layer index. Z = 0 is the surface and Z INCREASES DOWNWARD (descent is the
/// frontier — stratum numbering matches the design language "stratum 3").
/// This is the SIM coordinate space; the view layer owns the single mapping to
/// Godot world space (data Z-down → Godot Y-up), defined once in the Terrain
/// Rendering quick-spec and never duplicated.
/// </param>
public readonly record struct CellCoord(int X, int Y, int Z);
