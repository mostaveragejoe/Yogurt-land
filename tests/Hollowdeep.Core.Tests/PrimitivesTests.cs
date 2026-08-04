using Hollowdeep.Core;
using Hollowdeep.Core.Primitives;
using Hollowdeep.Core.Rng;
using Hollowdeep.Core.Sim;
using Xunit;

namespace Hollowdeep.Core.Tests;

public class EntityIdTests
{
    [Fact]
    public void KindPacksIntoTopByteAndCounterIsMonotonic()
    {
        var alloc = new EntityIdAllocator();
        var a = alloc.Next(EntityKind.Colonist);
        var b = alloc.Next(EntityKind.Raider);
        var c = alloc.Next(EntityKind.Item);
        Assert.Equal(EntityKind.Colonist, a.Kind);
        Assert.Equal(EntityKind.Raider, b.Kind);
        Assert.Equal(EntityKind.Item, c.Kind);
        Assert.Equal(1, a.Counter);
        Assert.Equal(2, b.Counter);
        Assert.Equal(3, c.Counter);
        Assert.Equal(3, alloc.Counter);
    }

    [Fact]
    public void AssertKindThrowsOnMismatch()
    {
        var id = EntityId.Make(EntityKind.Colonist, 7);
        id.AssertKind(EntityKind.Colonist);
        Assert.Throws<InvalidOperationException>(() => id.AssertKind(EntityKind.Raider));
    }
}

public class RngTests
{
    [Fact]
    public void SameSeedSameSequence()
    {
        var a = new RngStream(1234);
        var b = new RngStream(1234);
        for (int i = 0; i < 1000; i++)
            Assert.Equal(a.NextUInt64(), b.NextUInt64());
    }

    [Fact]
    public void SerializedStreamResumesAtArbitraryDrawCount()
    {
        var a = new RngStream(99);
        for (int i = 0; i < 137; i++) a.NextUInt64(); // arbitrary draw offset

        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
            a.WriteTo(w);
        ms.Position = 0;
        RngStream b;
        using (var r = new BinaryReader(ms))
            b = RngStream.ReadFrom(r);

        Assert.Equal(a.DrawCount, b.DrawCount);
        for (int i = 0; i < 500; i++)
            Assert.Equal(a.NextUInt64(), b.NextUInt64());
    }

    [Fact]
    public void HubStreamsAreIndependent()
    {
        var hub1 = new RngHub(42);
        var hub2 = new RngHub(42);
        hub1.Combat.NextUInt64(); // perturb one stream only
        Assert.Equal(hub1.Needs.NextUInt64(), hub2.Needs.NextUInt64());
    }

    [Fact]
    public void NextIntStaysInRangeAndIsDebiased()
    {
        var rng = new RngStream(5);
        for (int i = 0; i < 10_000; i++)
        {
            int v = rng.NextInt(7);
            Assert.InRange(v, 0, 6);
        }
    }
}

public class MutationGateTests
{
    [Fact]
    public void AssertOpenThrowsOutsideWindow()
    {
        var gate = new MutationGate();
        Assert.Throws<InvalidOperationException>(gate.AssertOpen);
        using (gate.Open())
            gate.AssertOpen(); // no throw
        Assert.Throws<InvalidOperationException>(gate.AssertOpen);
    }

    [Fact]
    public void NestedScopesFlushOnlyAtOuterClose()
    {
        var gate = new MutationGate();
        int flushes = 0;
        gate.OnOuterClose(() => flushes++);
        using (gate.Open())
        {
            using (gate.Open()) { }
            Assert.Equal(0, flushes);
        }
        Assert.Equal(1, flushes);
    }
}

public class OctileTests
{
    [Fact]
    public void OctileMatchesKnownValues()
    {
        Assert.Equal(0, CellCoord.Octile(new(0, 0, 0), new(0, 0, 0)));
        Assert.Equal(10, CellCoord.Octile(new(0, 0, 0), new(1, 0, 0)));
        Assert.Equal(14, CellCoord.Octile(new(0, 0, 0), new(1, 1, 0)));
        Assert.Equal(10 * 5 + 4 * 3, CellCoord.Octile(new(0, 0, 0), new(3, 5, 0))); // 5*10 + 3*4 = 62
    }
}
