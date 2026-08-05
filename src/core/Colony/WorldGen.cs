using Hollowdeep.Core.Entities;
using Hollowdeep.Core.Primitives;
using Hollowdeep.Core.Rng;
using Hollowdeep.Core.Terrain;

namespace Hollowdeep.Core.Colony;

/// <summary>
/// The "hand-authored" mountain (§8): a deterministic generator with a fixed seed.
/// Layout (64×64×8): z7 sky; z6 surface with a dirt hillside to dig into; z5 dirt band;
/// z1–4 granite with reinforced veins and one cave pocket; z0 dense granite/reinforced.
/// A flat spawn clearing west of the hill holds the hearth, stockpile, farm, and the
/// ten colonists.
/// </summary>
public static class WorldGen
{
    public static readonly string[] ColonistNames =
    {
        "Berit", "Doran", "Eshka", "Fenn", "Grit",
        "Hulda", "Ingo", "Jasha", "Korrin", "Lom",
    };

    public static void Generate(TerrainWorld terrain, RngStream rng, ISpawnWriter spawn,
        StockpileSystem stockpiles, FarmSystem farms, ColonyStats stats)
    {
        int w = terrain.Width, h = terrain.Height;
        int surface = GameConfig.SurfaceZ;

        // Solid ground below the surface: dirt band directly under it, granite deeper.
        for (int z = 0; z < surface; z++)
        {
            var mat = z >= surface - 2 ? MaterialId.Dirt : MaterialId.Granite;
            var def = MaterialCatalog.Get(mat);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var cell = new TerrainCell
                {
                    Floor = FloorKind.Natural,
                    FloorMaterial = mat,
                    Wall = WallKind.Natural,
                    WallMaterial = mat,
                    WallHp = (ushort)def.MaxWallHp,
                };
                terrain.SetCell(new CellCoord(x, y, z), cell);
            }
        }

        // Surface: open ground everywhere...
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
            terrain.SetFloor(new CellCoord(x, y, surface), FloorKind.Natural, MaterialId.Dirt);

        // ...with a dirt hillside blob to the east: the mountain face the colony digs into.
        var hillCenter = new CellCoord(42, 32, surface);
        var dirtDef = MaterialCatalog.Get(MaterialId.Dirt);
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            double nx = (x - hillCenter.X) / 21.0;
            double ny = (y - hillCenter.Y) / 27.0;
            double noise = (rng.NextDouble() - 0.5) * 0.18;
            if (nx * nx + ny * ny + noise < 1.0)
            {
                var c = new CellCoord(x, y, surface);
                var cell = terrain.Get(c);
                cell.Wall = WallKind.Natural;
                cell.WallMaterial = MaterialId.Dirt;
                cell.WallHp = (ushort)dirtDef.MaxWallHp;
                terrain.SetCell(c, cell);
            }
        }

        // Reinforced veins: short random walks through the granite band.
        var reinDef = MaterialCatalog.Get(MaterialId.Reinforced);
        for (int vein = 0; vein < 10; vein++)
        {
            int z = rng.Range(0, 4);
            int x = rng.NextInt(w), y = rng.NextInt(h);
            int len = rng.Range(4, 10);
            for (int i = 0; i < len; i++)
            {
                var c = new CellCoord(Math.Clamp(x, 0, w - 1), Math.Clamp(y, 0, h - 1), z);
                var cell = terrain.Get(c);
                if (cell.Wall == WallKind.Natural)
                {
                    cell.WallMaterial = MaterialId.Reinforced;
                    cell.WallHp = (ushort)reinDef.MaxWallHp;
                    terrain.SetCell(c, cell);
                }
                x += rng.Range(-1, 2);
                y += rng.Range(-1, 2);
            }
        }

        // One natural cave pocket in the granite, under the hill.
        var caveCenter = new CellCoord(44, 30, 3);
        for (int dy = -3; dy <= 3; dy++)
        for (int dx = -4; dx <= 4; dx++)
        {
            if ((dx * dx) / 16.0 + (dy * dy) / 9.0 > 1.0) continue;
            var c = new CellCoord(caveCenter.X + dx, caveCenter.Y + dy, caveCenter.Z);
            if (!terrain.InBounds(c)) continue;
            terrain.DigWall(c);
        }

        // Spawn clearing west of the hill: flatten any hill spill.
        var clearing = new CellCoord(13, 32, surface);
        for (int dy = -6; dy <= 6; dy++)
        for (int dx = -6; dx <= 6; dx++)
        {
            var c = clearing.Offset(dx, dy);
            if (!terrain.InBounds(c)) continue;
            if (terrain.Get(c).HasWall) terrain.DigWall(c);
        }

        stats.HearthCell = clearing;

        // Starting stockpile zone (4×4) with food and building material.
        for (int dy = 0; dy < 4; dy++)
        for (int dx = 0; dx < 4; dx++)
            stockpiles.AddZoneCell(new CellCoord(17 + dx, 29 + dy, surface));
        int foodLeft = GameConfig.StartingFood;
        int slot = 0;
        while (foodLeft > 0)
        {
            int count = Math.Min(GameConfig.MaxStackCount, foodLeft);
            spawn.SpawnItem(ItemKind.Food, MaterialId.None, count, new CellCoord(17 + slot % 4, 29 + slot / 4, surface));
            foodLeft -= count;
            slot++;
        }
        spawn.SpawnItem(ItemKind.Material, MaterialId.Dirt, 10, new CellCoord(17 + slot % 4, 29 + slot / 4, surface));
        slot++;
        spawn.SpawnItem(ItemKind.Material, MaterialId.Granite, 5, new CellCoord(17 + slot % 4, 29 + slot / 4, surface));

        // Mushroom farm plots south of the hearth.
        for (int i = 0; i < GameConfig.FarmPlotCount; i++)
            farms.AddPlot(new CellCoord(9 + i % 3, 27 + i / 3, surface));

        // Ten colonists around the hearth; four are miners (harder melee, §5.13).
        for (int i = 0; i < GameConfig.ColonistCount; i++)
        {
            var cell = new CellCoord(11 + i % 4, 30 + i / 4, surface);
            spawn.SpawnColonist(ColonistNames[i], cell, isMiner: i % 3 == 0);
        }
    }
}
