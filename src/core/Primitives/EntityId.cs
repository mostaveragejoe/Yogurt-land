namespace Hollowdeep.Core.Primitives;

public enum EntityKind : byte
{
    None = 0,
    Colonist = 1,
    Raider = 2,
    Item = 3,
    Door = 4,
    Prop = 5, // purely aesthetic furniture: torches, the hearth
}

/// <summary>
/// Monotonic, never-reused entity identity. The kind lives in the top byte so every
/// writer can assert it is being handed the right sort of id; the low 56 bits are the
/// world-wide monotonic counter (serialized with the save).
/// </summary>
public readonly struct EntityId : IEquatable<EntityId>, IComparable<EntityId>
{
    public readonly long Value;

    public EntityId(long value) => Value = value;

    public static readonly EntityId None = new(0);

    public EntityKind Kind => (EntityKind)(byte)((ulong)Value >> 56);
    public long Counter => Value & 0x00FF_FFFF_FFFF_FFFF;
    public bool IsNone => Value == 0;

    public static EntityId Make(EntityKind kind, long counter)
    {
        if (counter <= 0 || counter > 0x00FF_FFFF_FFFF_FFFF)
            throw new ArgumentOutOfRangeException(nameof(counter));
        return new EntityId(((long)(byte)kind << 56) | counter);
    }

    public void AssertKind(EntityKind expected)
    {
        if (Kind != expected)
            throw new InvalidOperationException($"Entity id {this} used where {expected} was required.");
    }

    public bool Equals(EntityId other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is EntityId other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public int CompareTo(EntityId other) => Value.CompareTo(other.Value);
    public static bool operator ==(EntityId a, EntityId b) => a.Value == b.Value;
    public static bool operator !=(EntityId a, EntityId b) => a.Value != b.Value;
    public override string ToString() => IsNone ? "none" : $"{Kind}#{Counter}";
}

/// <summary>Hands out ids. The counter is global across kinds and serialized; ids are never reused.</summary>
public sealed class EntityIdAllocator
{
    private long _counter;

    public long Counter => _counter;

    public EntityId Next(EntityKind kind) => EntityId.Make(kind, ++_counter);

    /// <summary>Load-time restore. Only the save loader may call this.</summary>
    public void Restore(long counter) => _counter = counter;
}
