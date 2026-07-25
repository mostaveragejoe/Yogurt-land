namespace Hollowdeep.Core.Primitives;

/// <summary>
/// Stable identity of a simulation entity across all entity kinds (ADR-0003) —
/// the cross-object reference mandated by the serialization contract.
/// One id space across ALL kinds: a participant list, reservation table, or
/// notification can reference any entity without kind-tagged unions.
/// </summary>
/// <param name="Value">
/// Declared <see cref="long"/> (not <c>ulong</c>) so ids cross the Godot C#
/// Variant boundary without conversion. Allocated by <c>EntityIdSource</c>:
/// monotonic per world starting at 1, NEVER reused (the counter itself is
/// serialized, which is the entire non-reuse guarantee). A despawned id
/// resolves to nothing forever after — dangling references are legal and
/// callers handle "gone".
/// </param>
public readonly record struct EntityId(long Value)
{
    /// <summary>The null entity reference (Value 0).</summary>
    public static readonly EntityId None = new(0);

    /// <summary>True when this is <see cref="None"/>.</summary>
    public bool IsNone => Value == 0;
}
