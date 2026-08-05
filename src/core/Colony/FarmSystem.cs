using Hollowdeep.Core.Entities;
using Hollowdeep.Core.Primitives;
using Hollowdeep.Core.Sim;
using Hollowdeep.Core.Terrain;

namespace Hollowdeep.Core.Colony;

/// <summary>
/// The renewable food source (§5.6): mushroom plots regrow on a timer and drop a food
/// item on their cell; hauling brings it to the stockpile.
/// </summary>
public sealed class FarmSystem : ITickable
{
    public struct Plot
    {
        public CellCoord Cell;
        public float Timer;
        public bool HasFruit; // a food item is sitting on the cell, waiting for regrow after pickup
    }

    private readonly List<Plot> _plots = new();
    private readonly IItemsWriter _items;
    private readonly ItemStore _itemStore;

    public Revision Revision;

    public FarmSystem(IItemsWriter items, ItemStore itemStore)
    {
        _items = items;
        _itemStore = itemStore;
    }

    public IReadOnlyList<Plot> Plots => _plots;

    public void AddPlot(CellCoord c)
    {
        _plots.Add(new Plot { Cell = c, Timer = GameConfig.FarmRegrowSeconds });
        Revision.Bump();
    }

    public void Tick(in TimeContext ctx)
    {
        if (ctx.Mode != SimMode.RealTime) return;
        var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_plots);
        for (int i = 0; i < span.Length; i++)
        {
            ref var plot = ref span[i];
            if (plot.HasFruit)
            {
                // Regrow clock restarts once the fruit has been hauled/eaten off the cell.
                if (!AnyFoodAt(plot.Cell)) { plot.HasFruit = false; plot.Timer = GameConfig.FarmRegrowSeconds; }
                continue;
            }
            plot.Timer -= ctx.Dt;
            if (plot.Timer <= 0f)
            {
                _items.Spawn(ItemKind.Food, MaterialId.None, 1, plot.Cell);
                plot.HasFruit = true;
                Revision.Bump();
            }
        }
    }

    private bool AnyFoodAt(CellCoord c)
    {
        for (int i = 0; i < _itemStore.Count; i++)
        {
            var it = _itemStore[i];
            if (it.Kind == ItemKind.Food && it.IsLoose && it.Cell == c) return true;
        }
        return false;
    }

    public void WriteTo(BinaryWriter w)
    {
        w.Write(_plots.Count);
        foreach (var p in _plots)
        {
            w.Write(p.Cell.X); w.Write(p.Cell.Y); w.Write(p.Cell.Z);
            w.Write(p.Timer); w.Write(p.HasFruit);
        }
    }

    public void ReadFrom(BinaryReader r)
    {
        _plots.Clear();
        int n = r.ReadInt32();
        for (int i = 0; i < n; i++)
            _plots.Add(new Plot
            {
                Cell = new CellCoord(r.ReadInt32(), r.ReadInt32(), r.ReadInt32()),
                Timer = r.ReadSingle(),
                HasFruit = r.ReadBoolean(),
            });
        Revision.Bump();
    }
}
