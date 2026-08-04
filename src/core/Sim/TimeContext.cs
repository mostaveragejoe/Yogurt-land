namespace Hollowdeep.Core.Sim;

public enum SimMode : byte
{
    RealTime = 0,
    TurnBased = 1,
}

/// <summary>Context handed to every Tick. Dt is always the fixed sub-step (1/60 s).</summary>
public readonly struct TimeContext
{
    public readonly SimMode Mode;
    public readonly float Dt;
    public readonly ulong TickSequence;
    public readonly int TurnIndex;

    public TimeContext(SimMode mode, float dt, ulong tickSequence, int turnIndex)
    {
        Mode = mode;
        Dt = dt;
        TickSequence = tickSequence;
        TurnIndex = turnIndex;
    }
}

/// <summary>The ONLY path that advances simulation state. Presentation code never implements this.</summary>
public interface ITickable
{
    void Tick(in TimeContext ctx);
}
