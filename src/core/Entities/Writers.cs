using Hollowdeep.Core.Primitives;
using Hollowdeep.Core.Sim;
using Hollowdeep.Core.Terrain;

namespace Hollowdeep.Core.Entities;

// Per-(system × field group) writer interfaces (ADR-0003). The composition root hands
// each system exactly the writer it may hold. Every write asserts the mutation window,
// the active mode where the field group is authority-split, and the id kind.
// Health is writer-per-authority: Needs writes it in RealTime, Combat in TurnBased.

public interface ISpawnWriter
{
    EntityId SpawnColonist(string name, CellCoord cell, bool isMiner);
    EntityId SpawnItem(ItemKind kind, MaterialId material, int count, CellCoord cell);
    EntityId SpawnDoor(CellCoord cell, MaterialId material);
    EntityId SpawnProp(CellCoord cell, PropKind kind, MaterialId material);
}

public interface INeedsWriter // NeedsSystem, RealTime only
{
    void SetNeeds(EntityId id, float food, float sleep, float morale);
    void SetActivity(EntityId id, ColonistActivity activity);
    void TickDownedRecovery(EntityId id, float delta01);
    /// <summary>Passive wound healing for standing colonists; Needs owns HP in RealTime.</summary>
    void RegenHp(EntityId id, float hpDelta);
}

public interface IPropsWriter // construction, RealTime only; props are aesthetic-only
{
    EntityId Place(CellCoord cell, PropKind kind, MaterialId material);
}

public interface IJobsWriter // JobSystem, RealTime only
{
    void SetActivity(EntityId id, ColonistActivity activity);
    void SetJob(EntityId id, long jobId);
    void BeginStep(EntityId id, CellCoord nextCell);
    void SetMoveProgress(EntityId id, float progress);
    void CompleteStep(EntityId id); // lands on NextCell; occupancy moves atomically
}

public interface IColonistCombatWriter // Combat, TurnBased only
{
    void SetCell(EntityId id, CellCoord cell);
    /// <summary>Applies §8 downed rules: 0 HP downs; a hit while downed kills.</summary>
    void ApplyDamage(EntityId id, int damage);
    void SetActivity(EntityId id, ColonistActivity activity);
}

public interface IReconcileWriter // PostEncounterReconcile only (RealTime window)
{
    void StabilizeDowned(EntityId id);
    void ReapAllRaiders();
}

public interface IRaiderRtWriter // RaidSystem, RealTime only
{
    EntityId Spawn(RaiderKind kind, CellCoord cell, int raidIndex);
    void BeginStep(EntityId id, CellCoord nextCell);
    void SetMoveProgress(EntityId id, float progress);
    void CompleteStep(EntityId id);
    void SetState(EntityId id, RaiderState state);
    void SetBreachTarget(EntityId id, CellCoord cell);
    void SetActionTimer(EntityId id, float seconds);
}

public interface IRaiderCombatWriter // Combat, TurnBased only
{
    void SetCell(EntityId id, CellCoord cell);
    void ApplyDamage(EntityId id, int damage);
    void SetState(EntityId id, RaiderState state);
}

public interface IItemsWriter // JobSystem/StockpileSystem/Farm, RealTime only
{
    EntityId Spawn(ItemKind kind, MaterialId material, int count, CellCoord cell);
    bool TryReserve(EntityId item, EntityId by);
    void Release(EntityId item, EntityId by);
    void PickUp(EntityId item, EntityId by);
    void PutDown(EntityId item, CellCoord cell);
    /// <summary>Consumes count units; removes the stack when it empties.</summary>
    void Consume(EntityId item, int count);
    void MergeInto(EntityId source, EntityId target);
}

public interface IDoorsWriter // construction places; both authorities damage
{
    EntityId Place(CellCoord cell, MaterialId material);
    /// <summary>Returns true when this hit breaks the door (unblocks move + LOS).</summary>
    bool Damage(EntityId id, int damage);
    void Repair(EntityId id, int amount);
}

/// <summary>Single implementation behind all writer interfaces; constructed once by GameWorld.</summary>
internal sealed class StoreWriters :
    ISpawnWriter, INeedsWriter, IJobsWriter, IColonistCombatWriter, IReconcileWriter,
    IRaiderRtWriter, IRaiderCombatWriter, IItemsWriter, IDoorsWriter, IPropsWriter
{
    private readonly MutationGate _gate;
    private readonly TimeAuthorityManager _time;
    private readonly EntityIdAllocator _ids;
    private readonly ColonistStore _colonists;
    private readonly RaiderStore _raiders;
    private readonly ItemStore _items;
    private readonly DoorStore _doors;
    private readonly PropStore _props;

    public StoreWriters(MutationGate gate, TimeAuthorityManager time, EntityIdAllocator ids,
        ColonistStore colonists, RaiderStore raiders, ItemStore items, DoorStore doors, PropStore props)
    {
        _gate = gate; _time = time; _ids = ids;
        _colonists = colonists; _raiders = raiders; _items = items; _doors = doors; _props = props;
    }

    private void AssertRt()
    {
        _gate.AssertOpen();
        if (_time.Mode != SimMode.RealTime)
            throw new InvalidOperationException("RealTime-only writer used while TurnBased authority is active.");
    }

    private void AssertTb()
    {
        _gate.AssertOpen();
        if (_time.Mode != SimMode.TurnBased)
            throw new InvalidOperationException("TurnBased-only writer used while RealTime authority is active.");
    }

    // ---- ISpawnWriter (world gen / load / debug; any mode, window asserted by stores) ----
    EntityId ISpawnWriter.SpawnColonist(string name, CellCoord cell, bool isMiner) => _colonists.Add(_ids, name, cell, isMiner);
    EntityId ISpawnWriter.SpawnItem(ItemKind kind, MaterialId material, int count, CellCoord cell) => _items.Add(_ids, kind, material, count, cell);
    EntityId ISpawnWriter.SpawnDoor(CellCoord cell, MaterialId material) => _doors.Add(_ids, cell, material);
    EntityId ISpawnWriter.SpawnProp(CellCoord cell, PropKind kind, MaterialId material) => _props.Add(_ids, cell, kind, material);

    // ---- INeedsWriter ----
    void INeedsWriter.SetNeeds(EntityId id, float food, float sleep, float morale)
    {
        AssertRt();
        ref var d = ref _colonists.Ref(id);
        d.Food = food; d.Sleep = sleep; d.Morale = morale;
        // Needs values change every tick; views read need bars through the store revision.
        _colonists.Bump();
    }

    void INeedsWriter.SetActivity(EntityId id, ColonistActivity activity)
    {
        AssertRt();
        ref var d = ref _colonists.Ref(id);
        d.Activity = activity;
        _colonists.Bump();
    }

    void INeedsWriter.TickDownedRecovery(EntityId id, float delta01)
    {
        AssertRt();
        ref var d = ref _colonists.Ref(id);
        if (!d.IsDowned || d.IsDead) return;
        d.DownedRecovery += delta01;
        if (d.DownedRecovery >= 1f)
        {
            d.DownedRecovery = 0f;
            d.Hp = d.MaxHp / 2; // §8: recover at 50% over one day — Needs owns HP in RealTime
            _colonists.SetIncapacitated(id, downed: false, dead: false);
            d = ref _colonists.Ref(id);
            d.Activity = ColonistActivity.Idle;
        }
        _colonists.Bump();
    }

    void INeedsWriter.RegenHp(EntityId id, float hpDelta)
    {
        AssertRt();
        ref var d = ref _colonists.Ref(id);
        if (d.IsDead || d.IsDowned || d.Hp >= d.MaxHp) return;
        d.HealProgress += hpDelta;
        if (d.HealProgress >= 1f)
        {
            int whole = (int)d.HealProgress;
            d.HealProgress -= whole;
            d.Hp = Math.Min(d.MaxHp, d.Hp + whole);
        }
        _colonists.Bump();
    }

    // ---- IPropsWriter ----
    EntityId IPropsWriter.Place(CellCoord cell, PropKind kind, MaterialId material)
    {
        AssertRt();
        return _props.Add(_ids, cell, kind, material);
    }

    // ---- IJobsWriter ----
    void IJobsWriter.SetActivity(EntityId id, ColonistActivity activity)
    {
        AssertRt();
        ref var d = ref _colonists.Ref(id);
        d.Activity = activity;
        _colonists.Bump();
    }

    void IJobsWriter.SetJob(EntityId id, long jobId)
    {
        AssertRt();
        ref var d = ref _colonists.Ref(id);
        d.CurrentJobId = jobId;
        _colonists.Bump();
    }

    void IJobsWriter.BeginStep(EntityId id, CellCoord nextCell)
    {
        AssertRt();
        ref var d = ref _colonists.Ref(id);
        d.NextCell = nextCell;
        d.MoveProgress = 0f;
        _colonists.Bump();
    }

    void IJobsWriter.SetMoveProgress(EntityId id, float progress)
    {
        AssertRt();
        ref var d = ref _colonists.Ref(id);
        d.MoveProgress = progress;
        _colonists.Bump();
    }

    void IJobsWriter.CompleteStep(EntityId id)
    {
        AssertRt();
        ref var d = ref _colonists.Ref(id);
        var next = d.NextCell;
        d.MoveProgress = 0f;
        _colonists.SetCell(id, next);
        ref var d2 = ref _colonists.Ref(id);
        d2.NextCell = next;
    }

    // ---- IColonistCombatWriter ----
    void IColonistCombatWriter.SetCell(EntityId id, CellCoord cell)
    {
        AssertTb();
        ref var d = ref _colonists.Ref(id);
        d.NextCell = cell;
        d.MoveProgress = 0f;
        _colonists.SetCell(id, cell);
    }

    void IColonistCombatWriter.ApplyDamage(EntityId id, int damage)
    {
        AssertTb();
        ref var d = ref _colonists.Ref(id);
        if (d.IsDead) return;
        if (d.IsDowned)
        {
            // §8: struck while downed = dead.
            _colonists.SetIncapacitated(id, downed: false, dead: true);
            return;
        }
        d.Hp -= damage;
        if (d.Hp <= 0)
        {
            d.Hp = 0;
            d.DownedRecovery = 0f;
            _colonists.SetIncapacitated(id, downed: true, dead: false);
        }
        _colonists.Bump();
    }

    void IColonistCombatWriter.SetActivity(EntityId id, ColonistActivity activity)
    {
        AssertTb();
        ref var d = ref _colonists.Ref(id);
        d.Activity = activity;
        _colonists.Bump();
    }

    // ---- IReconcileWriter (runs in the RealTime reconcile window) ----
    void IReconcileWriter.StabilizeDowned(EntityId id)
    {
        AssertRt();
        ref var d = ref _colonists.Ref(id);
        if (d.IsDead || !d.IsDowned) return;
        d.DownedRecovery = 0f; // stabilized: recovery clock starts now
        d.Activity = ColonistActivity.Downed;
        _colonists.Bump();
    }

    void IReconcileWriter.ReapAllRaiders() { AssertRt(); _raiders.ReapAll(); }

    // ---- IRaiderRtWriter ----
    EntityId IRaiderRtWriter.Spawn(RaiderKind kind, CellCoord cell, int raidIndex)
    {
        AssertRt();
        return _raiders.Add(_ids, kind, cell, raidIndex);
    }

    void IRaiderRtWriter.BeginStep(EntityId id, CellCoord nextCell)
    {
        AssertRt();
        ref var d = ref _raiders.Ref(id);
        d.NextCell = nextCell;
        d.MoveProgress = 0f;
        _raiders.Bump();
    }

    void IRaiderRtWriter.SetMoveProgress(EntityId id, float progress)
    {
        AssertRt();
        ref var d = ref _raiders.Ref(id);
        d.MoveProgress = progress;
        _raiders.Bump();
    }

    void IRaiderRtWriter.CompleteStep(EntityId id)
    {
        AssertRt();
        ref var d = ref _raiders.Ref(id);
        var next = d.NextCell;
        d.MoveProgress = 0f;
        _raiders.SetCell(id, next);
        ref var d2 = ref _raiders.Ref(id);
        d2.NextCell = next;
    }

    void IRaiderRtWriter.SetState(EntityId id, RaiderState state) { AssertRt(); _raiders.SetState(id, state); }

    void IRaiderRtWriter.SetBreachTarget(EntityId id, CellCoord cell)
    {
        AssertRt();
        ref var d = ref _raiders.Ref(id);
        d.BreachTarget = cell;
        _raiders.Bump();
    }

    void IRaiderRtWriter.SetActionTimer(EntityId id, float seconds)
    {
        AssertRt();
        ref var d = ref _raiders.Ref(id);
        d.ActionTimer = seconds;
        _raiders.Bump();
    }

    // ---- IRaiderCombatWriter ----
    void IRaiderCombatWriter.SetCell(EntityId id, CellCoord cell)
    {
        AssertTb();
        ref var d = ref _raiders.Ref(id);
        d.NextCell = cell;
        d.MoveProgress = 0f;
        _raiders.SetCell(id, cell);
    }

    void IRaiderCombatWriter.ApplyDamage(EntityId id, int damage)
    {
        AssertTb();
        ref var d = ref _raiders.Ref(id);
        if (!d.IsAlive) return;
        d.Hp -= damage;
        if (d.Hp <= 0)
        {
            d.Hp = 0;
            _raiders.SetState(id, RaiderState.Dead);
            return;
        }
        _raiders.Bump();
    }

    void IRaiderCombatWriter.SetState(EntityId id, RaiderState state) { AssertTb(); _raiders.SetState(id, state); }

    // ---- IItemsWriter ----
    EntityId IItemsWriter.Spawn(ItemKind kind, MaterialId material, int count, CellCoord cell)
    {
        AssertRt();
        return _items.Add(_ids, kind, material, count, cell);
    }

    bool IItemsWriter.TryReserve(EntityId item, EntityId by)
    {
        AssertRt();
        ref var d = ref _items.Ref(item);
        if (!d.ReservedBy.IsNone && d.ReservedBy != by) return false;
        d.ReservedBy = by;
        _items.Bump();
        return true;
    }

    void IItemsWriter.Release(EntityId item, EntityId by)
    {
        AssertRt();
        if (!_items.Contains(item)) return;
        ref var d = ref _items.Ref(item);
        if (d.ReservedBy == by) { d.ReservedBy = EntityId.None; _items.Bump(); }
    }

    void IItemsWriter.PickUp(EntityId item, EntityId by)
    {
        AssertRt();
        ref var d = ref _items.Ref(item);
        d.CarriedBy = by;
        _items.Bump();
    }

    void IItemsWriter.PutDown(EntityId item, CellCoord cell)
    {
        AssertRt();
        ref var d = ref _items.Ref(item);
        d.CarriedBy = EntityId.None;
        d.Cell = cell;
        _items.Bump();
    }

    void IItemsWriter.Consume(EntityId item, int count)
    {
        AssertRt();
        ref var d = ref _items.Ref(item);
        d.Count -= count;
        if (d.Count <= 0) _items.RemoveAt(item);
        else _items.Bump();
    }

    void IItemsWriter.MergeInto(EntityId source, EntityId target)
    {
        AssertRt();
        ref var t = ref _items.Ref(target);
        ref var s = ref _items.Ref(source);
        int space = GameConfig.MaxStackCount - t.Count;
        int moved = Math.Min(space, s.Count);
        if (moved <= 0) return;
        t.Count += moved;
        s.Count -= moved;
        if (s.Count <= 0) _items.RemoveAt(source);
        else _items.Bump();
    }

    // ---- IDoorsWriter ----
    EntityId IDoorsWriter.Place(CellCoord cell, MaterialId material)
    {
        AssertRt();
        return _doors.Add(_ids, cell, material);
    }

    bool IDoorsWriter.Damage(EntityId id, int damage)
    {
        _gate.AssertOpen(); // doors take damage under both authorities
        ref var d = ref _doors.Ref(id);
        if (d.IsBroken) return false;
        d.Hp -= damage;
        if (d.Hp <= 0)
        {
            d.Hp = 0;
            d.IsBroken = true; // unblocks movement and LOS immediately
            _doors.Bump();
            return true;
        }
        _doors.Bump();
        return false;
    }

    void IDoorsWriter.Repair(EntityId id, int amount)
    {
        AssertRt();
        ref var d = ref _doors.Ref(id);
        d.Hp = Math.Min(GameConfig.DoorHp, d.Hp + amount);
        if (d.Hp > 0) d.IsBroken = false;
        _doors.Bump();
    }
}
