namespace Hollowdeep.Core.Rng;

/// <summary>
/// xoshiro256** stream, seeded via SplitMix64. The full 256-bit state (plus a draw
/// counter for diagnostics) serializes, so a loaded stream resumes at the exact draw
/// it left off — required for deterministic reload-and-evolve.
/// </summary>
public sealed class RngStream
{
    private ulong _s0, _s1, _s2, _s3;
    private ulong _drawCount;

    public ulong DrawCount => _drawCount;

    public RngStream(ulong seed)
    {
        // SplitMix64 expansion — never leaves the state all-zero.
        ulong x = seed;
        _s0 = SplitMix(ref x);
        _s1 = SplitMix(ref x);
        _s2 = SplitMix(ref x);
        _s3 = SplitMix(ref x);
    }

    private RngStream() { }

    private static ulong SplitMix(ref ulong x)
    {
        x += 0x9E3779B97F4A7C15UL;
        ulong z = x;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    private static ulong Rotl(ulong v, int k) => (v << k) | (v >> (64 - k));

    public ulong NextUInt64()
    {
        _drawCount++;
        ulong result = Rotl(_s1 * 5, 7) * 9;
        ulong t = _s1 << 17;
        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;
        _s2 ^= t;
        _s3 = Rotl(_s3, 45);
        return result;
    }

    /// <summary>Uniform in [0, maxExclusive). Debiased via rejection.</summary>
    public int NextInt(int maxExclusive)
    {
        if (maxExclusive <= 0) throw new ArgumentOutOfRangeException(nameof(maxExclusive));
        ulong bound = (ulong)maxExclusive;
        ulong threshold = (0UL - bound) % bound;
        while (true)
        {
            ulong r = NextUInt64();
            if (r >= threshold) return (int)(r % bound);
        }
    }

    public int Range(int minInclusive, int maxExclusive) => minInclusive + NextInt(maxExclusive - minInclusive);

    public double NextDouble() => (NextUInt64() >> 11) * (1.0 / (1UL << 53));

    public bool Chance(double probability) => NextDouble() < probability;

    public void WriteTo(BinaryWriter w)
    {
        w.Write(_s0); w.Write(_s1); w.Write(_s2); w.Write(_s3); w.Write(_drawCount);
    }

    public static RngStream ReadFrom(BinaryReader r)
    {
        var s = new RngStream
        {
            _s0 = r.ReadUInt64(),
            _s1 = r.ReadUInt64(),
            _s2 = r.ReadUInt64(),
            _s3 = r.ReadUInt64(),
            _drawCount = r.ReadUInt64(),
        };
        return s;
    }

    public void CopyStateFrom(RngStream other)
    {
        _s0 = other._s0; _s1 = other._s1; _s2 = other._s2; _s3 = other._s3;
        _drawCount = other._drawCount;
    }
}

/// <summary>
/// One named stream per system so systems cannot perturb each other's sequences.
/// Streams derive from the root seed with fixed offsets; serialization order is fixed.
/// </summary>
public sealed class RngHub
{
    public RngStream WorldGen { get; private set; }
    public RngStream Needs { get; private set; }
    public RngStream Jobs { get; private set; }
    public RngStream Raids { get; private set; }
    public RngStream Combat { get; private set; }
    public RngStream Misc { get; private set; }

    public ulong RootSeed { get; }

    public RngHub(ulong rootSeed)
    {
        RootSeed = rootSeed;
        WorldGen = new RngStream(rootSeed ^ 0x01);
        Needs = new RngStream(rootSeed ^ 0x02);
        Jobs = new RngStream(rootSeed ^ 0x03);
        Raids = new RngStream(rootSeed ^ 0x04);
        Combat = new RngStream(rootSeed ^ 0x05);
        Misc = new RngStream(rootSeed ^ 0x06);
    }

    public void WriteTo(BinaryWriter w)
    {
        w.Write(RootSeed);
        WorldGen.WriteTo(w); Needs.WriteTo(w); Jobs.WriteTo(w);
        Raids.WriteTo(w); Combat.WriteTo(w); Misc.WriteTo(w);
    }

    /// <summary>Restores stream states in place (identities are stable across load).</summary>
    public void ReadFrom(BinaryReader r)
    {
        ulong seed = r.ReadUInt64();
        if (seed != RootSeed)
            throw new InvalidDataException($"RNG root seed mismatch: save {seed} vs world {RootSeed}.");
        WorldGen.CopyStateFrom(RngStream.ReadFrom(r));
        Needs.CopyStateFrom(RngStream.ReadFrom(r));
        Jobs.CopyStateFrom(RngStream.ReadFrom(r));
        Raids.CopyStateFrom(RngStream.ReadFrom(r));
        Combat.CopyStateFrom(RngStream.ReadFrom(r));
        Misc.CopyStateFrom(RngStream.ReadFrom(r));
    }
}
