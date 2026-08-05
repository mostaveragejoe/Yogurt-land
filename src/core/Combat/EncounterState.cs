using Hollowdeep.Core.Primitives;
using Hollowdeep.Core.Sim;

namespace Hollowdeep.Core.Combat;

public enum TbPhase : byte
{
    Inactive = 0,
    ActorTurn = 1,
    BattleEnded = 2,
}

public enum CombatEventKind : byte
{
    Move = 0,
    Attack = 1,      // Value = damage dealt (0 on miss), Hit flag says which
    WallHit = 2,     // Value = damage
    WallDestroyed = 3,
    DoorHit = 4,
    DoorBroken = 5,
    Downed = 6,
    Died = 7,
    Routed = 8,
    BattleEnd = 9,
}

public struct CombatEvent
{
    public CombatEventKind Kind;
    public EntityId Actor;
    public EntityId Target;
    public CellCoord Cell;
    public int Value;
    public bool Hit;
}

/// <summary>
/// Combat-transient state (ADR-0003): initiative, AP, casualty counts. Lives in this
/// encounter-scoped side table owned by the combat system — never in stores, never in
/// colony saves; serialized only into the battle checkpoint by its owner.
/// </summary>
public sealed class EncounterState
{
    public TbPhase Phase = TbPhase.Inactive;
    public int RaidIndex;
    public string Reason = "";
    public CellCoord FocusCell;
    public readonly List<EntityId> Initiative = new();
    public readonly Dictionary<EntityId, int> Ap = new();
    public int CurrentIndex;
    public int RoundNumber;
    public int RaidersAtStart;
    public int RaiderCasualties;
    public bool RoutTriggered;
    public ulong ActivationCounter;
    public int WallsDestroyed;
    public int DoorsBroken;
    public int ColonistsDownedThisBattle;
    public int ColonistsDeadThisBattle;

    public void Clear()
    {
        Phase = TbPhase.Inactive;
        RaidIndex = 0;
        Reason = "";
        FocusCell = default;
        Initiative.Clear();
        Ap.Clear();
        CurrentIndex = 0;
        RoundNumber = 0;
        RaidersAtStart = 0;
        RaiderCasualties = 0;
        RoutTriggered = false;
        ActivationCounter = 0;
        WallsDestroyed = 0;
        DoorsBroken = 0;
        ColonistsDownedThisBattle = 0;
        ColonistsDeadThisBattle = 0;
    }

    /// <summary>Battle-checkpoint serialization (ADR-0004). Iteration orders are list-stable.</summary>
    public void WriteTo(BinaryWriter w)
    {
        w.Write((byte)Phase);
        w.Write(RaidIndex);
        w.Write(Reason);
        w.Write(FocusCell.X); w.Write(FocusCell.Y); w.Write(FocusCell.Z);
        w.Write(Initiative.Count);
        foreach (var id in Initiative) { w.Write(id.Value); w.Write(Ap.TryGetValue(id, out int ap) ? ap : 0); }
        w.Write(CurrentIndex);
        w.Write(RoundNumber);
        w.Write(RaidersAtStart);
        w.Write(RaiderCasualties);
        w.Write(RoutTriggered);
        w.Write(ActivationCounter);
        w.Write(WallsDestroyed);
        w.Write(DoorsBroken);
        w.Write(ColonistsDownedThisBattle);
        w.Write(ColonistsDeadThisBattle);
    }

    public void ReadFrom(BinaryReader r)
    {
        Clear();
        Phase = (TbPhase)r.ReadByte();
        RaidIndex = r.ReadInt32();
        Reason = r.ReadString();
        FocusCell = new CellCoord(r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
        int n = r.ReadInt32();
        for (int i = 0; i < n; i++)
        {
            var id = new EntityId(r.ReadInt64());
            Initiative.Add(id);
            Ap[id] = r.ReadInt32();
        }
        CurrentIndex = r.ReadInt32();
        RoundNumber = r.ReadInt32();
        RaidersAtStart = r.ReadInt32();
        RaiderCasualties = r.ReadInt32();
        RoutTriggered = r.ReadBoolean();
        ActivationCounter = r.ReadUInt64();
        WallsDestroyed = r.ReadInt32();
        DoorsBroken = r.ReadInt32();
        ColonistsDownedThisBattle = r.ReadInt32();
        ColonistsDeadThisBattle = r.ReadInt32();
    }
}
