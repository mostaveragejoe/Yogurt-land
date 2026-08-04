using Hollowdeep.Core.Primitives;

namespace Hollowdeep.Core.Entities;

public enum EncounterResult : byte
{
    Victory = 0,        // all raiders dead
    RaidersRouted = 1,  // ≥60% casualties
    ColonyOverrun = 2,  // every colonist downed/dead
    Aborted = 3,
}

/// <summary>
/// The Combat↔Veterancy cycle breaker (ADR-0003): combat drops one report into this
/// one-slot inbox at battle end; PostEncounterReconcile drains it. MVP consumers log it.
/// </summary>
public sealed class EncounterOutcomeReport
{
    public int RaidIndex;
    public EncounterResult Result;
    public int RaidersKilled;
    public int RaidersRouted;
    public int ColonistsDowned;
    public int ColonistsDead;
    public int WallsDestroyed;
    public int DoorsBroken;
    public int RoundsFought;
}

public sealed class EncounterOutcomeInbox
{
    private EncounterOutcomeReport? _slot;

    public bool HasReport => _slot != null;

    public void Post(EncounterOutcomeReport report)
    {
        if (_slot != null)
            throw new InvalidOperationException("Encounter outcome inbox already holds an undrained report.");
        _slot = report;
    }

    public EncounterOutcomeReport Drain()
    {
        var r = _slot ?? throw new InvalidOperationException("Encounter outcome inbox is empty.");
        _slot = null;
        return r;
    }

    public void Clear() => _slot = null;
}
