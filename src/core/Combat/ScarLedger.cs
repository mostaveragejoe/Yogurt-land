using Hollowdeep.Core.Primitives;

namespace Hollowdeep.Core.Combat;

public enum ScarKind : byte
{
    WallDamaged = 0,
    WallDestroyed = 1,
    DoorDamaged = 2,
    DoorBroken = 3,
    ColonistDowned = 4,
    ColonistDead = 5,
    BreachEntry = 6, // where raiders first broke into the colony
}

public struct Scar
{
    public ScarKind Kind;
    public CellCoord Cell;
    public EntityId Actor;
    public int RaidIndex;
    public int Value; // damage amount where meaningful

    public void WriteTo(BinaryWriter w)
    {
        w.Write((byte)Kind);
        w.Write(Cell.X); w.Write(Cell.Y); w.Write(Cell.Z);
        w.Write(Actor.Value); w.Write(RaidIndex); w.Write(Value);
    }

    public static Scar ReadFrom(BinaryReader r) => new()
    {
        Kind = (ScarKind)r.ReadByte(),
        Cell = new CellCoord(r.ReadInt32(), r.ReadInt32(), r.ReadInt32()),
        Actor = new EntityId(r.ReadInt64()),
        RaidIndex = r.ReadInt32(),
        Value = r.ReadInt32(),
    };
}

/// <summary>
/// Pillar 3: every loss leaves a legible lesson. Raid systems log damage as it happens
/// (RT breach hits and TB destruction alike); the post-battle summary lists what was
/// damaged/destroyed and by which breach path.
/// </summary>
public sealed class ScarLedger
{
    private const int Cap = 500;
    private readonly List<Scar> _scars = new();

    public Revision Revision;

    public IReadOnlyList<Scar> Scars => _scars;

    public void Record(ScarKind kind, CellCoord cell, EntityId actor, int raidIndex, int value = 0)
    {
        if (_scars.Count >= Cap) _scars.RemoveAt(0);
        _scars.Add(new Scar { Kind = kind, Cell = cell, Actor = actor, RaidIndex = raidIndex, Value = value });
        Revision.Bump();
    }

    /// <summary>All scars from one raid, for the post-battle summary.</summary>
    public void CollectRaid(int raidIndex, List<Scar> into)
    {
        into.Clear();
        foreach (var s in _scars)
            if (s.RaidIndex == raidIndex)
                into.Add(s);
    }

    public void WriteTo(BinaryWriter w)
    {
        w.Write(_scars.Count);
        foreach (var s in _scars) s.WriteTo(w);
    }

    public void ReadFrom(BinaryReader r)
    {
        _scars.Clear();
        int n = r.ReadInt32();
        for (int i = 0; i < n; i++) _scars.Add(Scar.ReadFrom(r));
        Revision.Bump();
    }
}
