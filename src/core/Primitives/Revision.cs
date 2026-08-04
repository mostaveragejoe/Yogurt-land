namespace Hollowdeep.Core.Primitives;

/// <summary>
/// Cheap change counter. Views poll revisions instead of subscribing to entity events;
/// systems bump on every externally observable mutation.
/// </summary>
public struct Revision
{
    private ulong _value;
    public readonly ulong Value => _value;
    public void Bump() => _value++;
    public override readonly string ToString() => $"rev{_value}";
}
