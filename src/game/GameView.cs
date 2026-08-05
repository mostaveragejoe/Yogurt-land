using Godot;
using Hollowdeep.Core.Primitives;

namespace Hollowdeep.Game;

/// <summary>Shared view conventions: cell (X,Y,Z-layer) ↔ Godot world (X, up=Z-layer, Y).</summary>
public static class GameView
{
    public const float CellSize = 1f;
    public const float LayerHeight = 1f;

    public static Vector3 CellToWorld(CellCoord c)
        => new(c.X + 0.5f, c.Z * LayerHeight, c.Y + 0.5f);

    public static Vector3 CellToWorldCenter(CellCoord c)
        => new(c.X + 0.5f, c.Z * LayerHeight + 0.5f, c.Y + 0.5f);

    public static CellCoord WorldToCell(Vector3 p, int z)
        => new(Mathf.FloorToInt(p.X), Mathf.FloorToInt(p.Z), z);

    // "Warm Hearth, Cold Dark" greybox palette (§2).
    public static readonly Color DirtColor = new(0.45f, 0.33f, 0.22f);
    public static readonly Color GraniteColor = new(0.52f, 0.52f, 0.55f);
    public static readonly Color ReinforcedColor = new(0.42f, 0.47f, 0.58f);
    public static readonly Color FloorTint = new(0.8f, 0.76f, 0.7f);
    public static readonly Color StairColor = new(0.75f, 0.65f, 0.45f);
    public static readonly Color ColonistColor = new(0.95f, 0.85f, 0.6f);
    public static readonly Color MinerColor = new(0.95f, 0.72f, 0.4f);
    public static readonly Color DownedColor = new(0.6f, 0.25f, 0.2f);
    public static readonly Color RaiderColor = new(0.75f, 0.2f, 0.18f);
    public static readonly Color RangedRaiderColor = new(0.85f, 0.35f, 0.55f);
    public static readonly Color DoorColor = new(0.55f, 0.4f, 0.2f);
    public static readonly Color BrokenDoorColor = new(0.3f, 0.22f, 0.12f);
    public static readonly Color FoodColor = new(0.55f, 0.75f, 0.35f);
    public static readonly Color DigOverlay = new(1f, 0.85f, 0.3f, 0.45f);
    public static readonly Color BuildOverlay = new(0.4f, 0.7f, 1f, 0.45f);
    public static readonly Color RepairOverlay = new(0.4f, 1f, 0.6f, 0.45f);
    public static readonly Color StockpileOverlay = new(0.8f, 0.6f, 1f, 0.35f);
    public static readonly Color MoveRangeOverlay = new(0.35f, 0.8f, 1f, 0.4f);
    public static readonly Color SelectionColor = new(1f, 1f, 1f, 0.9f);

    public static StandardMaterial3D FlatMaterial(Color color, bool transparent = false)
    {
        var mat = new StandardMaterial3D
        {
            AlbedoColor = color,
            Roughness = 1f,
            MetallicSpecular = 0f,
        };
        if (transparent)
            mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        return mat;
    }
}
