using Hollowdeep.Core.Primitives;
using Xunit;

namespace Hollowdeep.Tests.Unit.Primitives;

/// <summary>
/// Example test, and a real one: CellCoord is a shared foundation primitive
/// (ADR-0002) that every system compares, hashes and stores. Value semantics
/// and the Z-down convention are load-bearing, so they get coverage rather than
/// trust.
/// </summary>
public class CellCoordTests
{
    [Fact]
    public void test_same_components_are_equal()
    {
        var a = new CellCoord(3, 7, 2);
        var b = new CellCoord(3, 7, 2);

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void test_differing_component_is_not_equal()
    {
        var baseline = new CellCoord(3, 7, 2);

        Assert.NotEqual(baseline, new CellCoord(4, 7, 2));
        Assert.NotEqual(baseline, new CellCoord(3, 8, 2));
        Assert.NotEqual(baseline, new CellCoord(3, 7, 3));
    }

    [Fact]
    public void test_axes_are_not_interchangeable()
    {
        // Guards against a transposed-argument bug in any caller that builds a
        // coord from loop variables - the failure mode is silent and spatial.
        Assert.NotEqual(new CellCoord(1, 2, 3), new CellCoord(3, 2, 1));
        Assert.NotEqual(new CellCoord(1, 2, 3), new CellCoord(2, 1, 3));
    }

    [Fact]
    public void test_usable_as_a_dictionary_key()
    {
        // Sparse side tables across the ADRs are keyed by CellCoord.
        var table = new Dictionary<CellCoord, string>
        {
            [new CellCoord(1, 1, 0)] = "surface",
            [new CellCoord(1, 1, 5)] = "deep",
        };

        Assert.Equal("surface", table[new CellCoord(1, 1, 0)]);
        Assert.Equal("deep", table[new CellCoord(1, 1, 5)]);
        Assert.False(table.ContainsKey(new CellCoord(1, 1, 9)));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 5)]
    [InlineData(5, 16)]
    public void test_greater_z_is_deeper(int shallower, int deeper)
    {
        // Z increases DOWNWARD (ADR-0002). A reversal here would invert the
        // whole descent-is-the-frontier design language, so it is pinned.
        Assert.True(deeper > shallower);
        var above = new CellCoord(0, 0, shallower);
        var below = new CellCoord(0, 0, deeper);
        Assert.True(below.Z > above.Z);
    }

    [Fact]
    public void test_negative_coordinates_are_representable()
    {
        // Bounds checking belongs to TerrainWorld, not the primitive - the
        // struct itself must not silently clamp or throw.
        var c = new CellCoord(-1, -2, -3);
        Assert.Equal(-1, c.X);
        Assert.Equal(-2, c.Y);
        Assert.Equal(-3, c.Z);
    }
}
