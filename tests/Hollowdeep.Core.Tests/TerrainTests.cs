using Hollowdeep.Core;
using Hollowdeep.Core.Primitives;
using Hollowdeep.Core.Sim;
using Hollowdeep.Core.Terrain;
using Xunit;

namespace Hollowdeep.Core.Tests;

public class TerrainTests
{
    private static (TerrainWorld world, MutationGate gate) NewWorld()
    {
        var gate = new MutationGate();
        return (new TerrainWorld(64, 64, 8, gate), gate);
    }

    [Fact]
    public void TerrainCellIsExactlyEightBytes()
    {
        Assert.Equal(8, System.Runtime.InteropServices.Marshal.SizeOf<TerrainCell>());
    }

    [Fact]
    public void MutationOutsideWindowThrows()
    {
        var (world, _) = NewWorld();
        Assert.Throws<InvalidOperationException>(() => world.DigWall(new CellCoord(1, 1, 1)));
    }

    [Fact]
    public void ChunkOfIsTheOnlyMappingAndOctantAligned()
    {
        var (world, _) = NewWorld();
        Assert.Equal(32, TerrainWorld.ChunkSize); // locked invariant: octant size == chunk size
        Assert.Equal(new ChunkCoord(0, 0, 3), world.ChunkOf(new CellCoord(31, 31, 3)));
        Assert.Equal(new ChunkCoord(1, 0, 3), world.ChunkOf(new CellCoord(32, 31, 3)));
        Assert.Equal(new ChunkCoord(1, 1, 0), world.ChunkOf(new CellCoord(63, 63, 0)));
    }

    [Fact]
    public void DigBuildDamageLifecycle()
    {
        var (world, gate) = NewWorld();
        var c = new CellCoord(5, 5, 2);
        using (gate.Open())
        {
            world.BuildWall(c, MaterialId.Granite);
            Assert.True(world.Get(c).HasWall);
            Assert.Equal(GameConfig.GraniteWallHp, world.Get(c).WallHp);

            Assert.False(world.DamageWall(c, 150));
            Assert.Equal(GameConfig.GraniteWallHp - 150, world.Get(c).WallHp);

            world.RepairWall(c, 500);
            Assert.Equal(GameConfig.GraniteWallHp, world.Get(c).WallHp); // clamped to max

            Assert.True(world.DamageWall(c, 10_000)); // destroyed
            Assert.False(world.Get(c).HasWall);
            Assert.True(world.Get(c).HasFloor); // destruction leaves walkable floor
        }
    }

    [Fact]
    public void BatchesCapturePreviousStateAndDispatchOncePerWindow()
    {
        var (world, gate) = NewWorld();
        int dispatches = 0;
        int changeCount = 0;
        TerrainCell captured = default;
        world.Changed += batch =>
        {
            dispatches++;
            changeCount = batch.Count;
            captured = batch[0].Previous;
        };
        var c = new CellCoord(3, 4, 5);
        using (gate.Open())
        {
            world.BuildWall(c, MaterialId.Dirt);
            world.DamageWall(c, 10);
            Assert.Equal(0, dispatches); // batched until outer close
        }
        Assert.Equal(1, dispatches);
        Assert.Equal(2, changeCount);
        Assert.False(captured.HasWall); // previous state of first change was empty
    }

    [Fact]
    public void SerializationRoundTripsByteIdentical()
    {
        var (world, gate) = NewWorld();
        using (gate.Open())
        {
            world.BuildWall(new CellCoord(1, 2, 3), MaterialId.Reinforced);
            world.SetFloor(new CellCoord(9, 9, 6), FloorKind.Stair, MaterialId.Granite);
            world.DamageWall(new CellCoord(1, 2, 3), 77);
        }

        byte[] first;
        using (var ms = new MemoryStream())
        using (var w = new BinaryWriter(ms))
        {
            world.WriteTo(w);
            first = ms.ToArray();
        }

        var (world2, gate2) = NewWorld();
        using (gate2.Open())
        using (var ms = new MemoryStream(first))
        using (var r = new BinaryReader(ms))
            world2.ReadFrom(r);

        byte[] second;
        using (var ms = new MemoryStream())
        using (var w = new BinaryWriter(ms))
        {
            world2.WriteTo(w);
            second = ms.ToArray();
        }

        Assert.Equal(first, second);
    }
}
