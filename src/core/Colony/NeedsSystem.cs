using Hollowdeep.Core.Entities;
using Hollowdeep.Core.Sim;

namespace Hollowdeep.Core.Colony;

/// <summary>
/// Need decay and downed recovery (RealTime only). The Needs system owns colonist HP in
/// RealTime (writer-per-authority, ADR-0003). Interruption thresholds are read by the
/// JobSystem, which owns job switching.
/// </summary>
public sealed class NeedsSystem : ITickable
{
    private readonly ColonistStore _colonists;
    private readonly INeedsWriter _writer;

    public NeedsSystem(ColonistStore colonists, INeedsWriter writer)
    {
        _colonists = colonists;
        _writer = writer;
    }

    public void Tick(in TimeContext ctx)
    {
        if (ctx.Mode != SimMode.RealTime) return;
        float dt = ctx.Dt;
        float downedDelta = dt / (GameConfig.DownedRecoveryDays * GameConfig.DayLengthSeconds);

        for (int i = 0; i < _colonists.Count; i++)
        {
            var c = _colonists[i];
            if (c.IsDead) continue;
            if (c.IsDowned)
            {
                _writer.TickDownedRecovery(c.Id, downedDelta);
                continue;
            }

            float food = c.Food;
            float sleep = c.Sleep;
            float morale = c.Morale;

            if (c.Activity != ColonistActivity.Eating) // eating pauses decay; JobSystem applies the refill
                food = Math.Max(0f, food - GameConfig.FoodDecayPerSecond * dt);

            if (c.Activity == ColonistActivity.Sleeping)
                sleep = Math.Min(GameConfig.SleepMax, sleep + GameConfig.SleepMax / GameConfig.SleepRestoreSeconds * dt);
            else
                sleep = Math.Max(0f, sleep - GameConfig.SleepDecayPerSecond * dt);

            // Morale-lite (§8): decays while idle, recovers while working. Cosmetic in MVP.
            if (c.Activity == ColonistActivity.Idle)
                morale = Math.Max(0f, morale - GameConfig.MoraleIdleDecayPerSecond * dt);
            else if (c.Activity == ColonistActivity.Working)
                morale = Math.Min(GameConfig.MoraleMax, morale + GameConfig.MoraleWorkRegenPerSecond * dt);

            if (food != c.Food || sleep != c.Sleep || morale != c.Morale)
                _writer.SetNeeds(c.Id, food, sleep, morale);
        }
    }
}
