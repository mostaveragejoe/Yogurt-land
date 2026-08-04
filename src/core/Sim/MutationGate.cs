namespace Hollowdeep.Core.Sim;

/// <summary>
/// The mutation window. All store and terrain writes assert this is open; the time
/// authorities (and the save loader) are the only code that opens it. Scopes are
/// readonly structs so opening a window never allocates.
/// </summary>
public sealed class MutationGate
{
    private int _depth;
    private readonly List<Action> _outerClosed = new();

    public bool IsOpen => _depth > 0;

    /// <summary>Registered callbacks run when the outermost scope closes (used to flush change batches).</summary>
    public void OnOuterClose(Action callback) => _outerClosed.Add(callback);

    public MutationScope Open()
    {
        _depth++;
        return new MutationScope(this);
    }

    internal void Close()
    {
        if (_depth <= 0)
            throw new InvalidOperationException("Mutation window closed more times than opened.");
        if (_depth == 1)
        {
            // Flush while still open so handlers may not re-mutate but batches can read.
            for (int i = 0; i < _outerClosed.Count; i++) _outerClosed[i]();
        }
        _depth--;
    }

    public void AssertOpen()
    {
        if (!IsOpen)
            throw new InvalidOperationException(
                "Illegal write: simulation state mutated outside the authority-driven mutation window.");
    }
}

/// <summary>Zero-allocation scope over an open mutation window. Dispose closes it.</summary>
public readonly struct MutationScope : IDisposable
{
    private readonly MutationGate _gate;
    internal MutationScope(MutationGate gate) => _gate = gate;
    public void Dispose() => _gate.Close();
}
