using Hollowdeep.Core.Primitives;

namespace Hollowdeep.Core.Colony;

/// <summary>
/// Colony-wide counters written by systems inside Tick (never by bus handlers).
/// Wealth drives raid sizing (§5.11): cells dug + 2×walls built + stockpile items.
/// </summary>
public sealed class ColonyStats
{
    public int CellsDug;
    public int WallsBuilt;
    public CellCoord HearthCell; // the colony core: raider fallback target, breach anchor
    public double WorldSeconds;  // accumulated RT sim time

    public int Wealth(int itemCount) => CellsDug + 2 * WallsBuilt + itemCount;

    public void WriteTo(BinaryWriter w)
    {
        w.Write(CellsDug); w.Write(WallsBuilt);
        w.Write(HearthCell.X); w.Write(HearthCell.Y); w.Write(HearthCell.Z);
        w.Write(WorldSeconds);
    }

    public void ReadFrom(BinaryReader r)
    {
        CellsDug = r.ReadInt32(); WallsBuilt = r.ReadInt32();
        HearthCell = new CellCoord(r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
        WorldSeconds = r.ReadDouble();
    }
}
