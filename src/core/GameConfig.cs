namespace Hollowdeep.Core;

/// <summary>
/// Every gameplay tuning value lives here (spec §3: data-driven, no scattered magic
/// numbers). Values marked §8 are pre-made spec decisions — do not retune casually.
/// </summary>
public static class GameConfig
{
    // ---- Time (§8) ----
    public const float FixedDt = 1f / 60f;
    public static readonly int[] SubStepsPerSecond = { 0, 60, 120, 180 }; // speeds 0..3

    // ---- Map ----
    public const int MapWidth = 64;
    public const int MapHeight = 64;
    public const int MapDepth = 8;
    public const ulong WorldSeed = 0x48011053UL; // fixed hand-authored seed (DECISIONS #3)
    public const int SurfaceZ = 6;               // walkable surface layer; 7 is sky

    // ---- Materials (§5): HP and dig seconds at 1x ----
    public const int DirtWallHp = 50;
    public const int GraniteWallHp = 200;
    public const int ReinforcedWallHp = 600;
    public const float DirtDigSeconds = 1f;
    public const float GraniteDigSeconds = 3f;
    public const float ReinforcedDigSeconds = 8f;

    // ---- Construction ----
    public const float BuildSeconds = 2f;          // build any wall/floor/door/stair
    public const int BuildMaterialCost = 1;        // stacks consumed per build
    public const float RepairSeconds = 1.5f;       // per repair action
    public const int RepairAmountPerAction = 50;   // HP restored per action, consumes 1 material at most every action

    // ---- Colonists ----
    public const int ColonistCount = 10;
    public const int ColonistMaxHp = 100;          // §8
    public const float ColonistMoveCellsPerSecond = 4f;
    public const int ColonistMeleeDamage = 20;     // §8
    public const int MinerMeleeDamage = 25;        // miners hit harder (§5.13)

    // ---- Needs (§8: eat ~2x/day, sleep 1x/day, day ≈ 8 min at 1x) ----
    public const float DayLengthSeconds = 480f;
    public const float FoodMax = 100f;
    public const float FoodDecayPerSecond = FoodMax / (DayLengthSeconds / 2f); // empty twice a day
    public const float FoodSeekThreshold = 35f;
    public const float SleepMax = 100f;
    public const float SleepDecayPerSecond = SleepMax / DayLengthSeconds;
    public const float SleepSeekThreshold = 25f;
    public const float SleepRestoreSeconds = 45f;  // full night's sleep at 1x
    public const float MoraleMax = 100f;
    public const float MoraleIdleDecayPerSecond = 0.5f;
    public const float MoraleWorkRegenPerSecond = 1.0f;
    public const float HealHpPerSecond = 0.5f;   // standing-wound regen (full heal ≈ half a day)
    public const float HealMinFood = 30f;        // too hungry to mend

    // ---- Items / stockpiles ----
    public const int StartingFood = 30;
    public const int MaxStackCount = 10;
    public const float FarmRegrowSeconds = 90f;
    public const int FarmPlotCount = 6;

    // ---- Raids (§8) ----
    public const float FirstRaidAtSeconds = 480f;      // ~8 min
    public const float RaidIntervalMinSeconds = 360f;  // 6 min
    public const float RaidIntervalMaxSeconds = 600f;  // 10 min
    public const int RaidSizeBase = 2;
    public const int RaidSizeCap = 8;
    public const int WealthRaidThreshold = 150;        // DECISIONS #10
    public const float RaidWarningSeconds = 30f;       // squad-prep window at 1x
    public const int RaiderWallDamagePerHit = 25;      // §8
    public const float RaiderWallHitSeconds = 1f;
    public const float RaiderMoveCellsPerSecond = 3f;
    public const int BreachTriggerRadius = 10;         // DECISIONS #8

    // ---- Combat (§8) ----
    public const int RaiderMaxHp = 80;
    public const int RaiderMeleeDamage = 20;
    public const int RaiderRangedDamage = 15;
    public const int RangedAttackRange = 8;
    public const int BaseHitPercent = 75;
    public const int CoverHitPenaltyPercent = 25;
    public const int ActionPointsPerTurn = 2;
    public const int MoveBudgetPerAp = 50;             // 5 orthogonal steps in octile units
    public const int DoorTraversalSurcharge = 10;      // §8
    public const int DoorHp = 100;                     // §8, any tier
    public const float RoutCasualtyFraction = 0.6f;    // §8
    public const float DownedRecoveryDays = 1f;        // §8: recover at 50% over one day

    // ---- Presentation budgets (§4f) ----
    public const int TerrainDrawCallBudget = 150;
    public const int FrameDrawCallBudget = 500;
}
