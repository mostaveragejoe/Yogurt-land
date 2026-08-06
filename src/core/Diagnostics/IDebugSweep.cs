namespace Hollowdeep.Core.Diagnostics;

/// <summary>
/// A registered invariant sweep. The debug console owns and schedules the
/// debug invariant sweeps named by the foundation ADRs — ADR-0002's
/// reservation-bit check (Flags bit ≡ Job Assignment's claim-table keyset) and
/// ADR-0003's entity-layer checks (occupancy ≡ living store positions;
/// reservation consistency; composition-root grant audit). Systems register
/// their sweep when they are built; the console runs them on demand
/// (<c>sweep</c> command) and, later, on a debug-build cadence.
/// </summary>
public interface IDebugSweep
{
    /// <summary>Unique sweep name (command argument; lower-case, no spaces).</summary>
    string Name { get; }

    /// <summary>One-line description shown by the <c>sweeps</c> command.</summary>
    string Description { get; }

    /// <summary>
    /// Runs the check. Must be READ-ONLY over simulation state: a sweep
    /// observes and reports; it never repairs. (Writes from here would violate
    /// the mutation-window rule and hide the very divergence being checked.)
    /// </summary>
    SweepResult Run();
}

/// <summary>Outcome of one <see cref="IDebugSweep.Run"/>.</summary>
/// <param name="Passed">True when the invariant held.</param>
/// <param name="Detail">
/// Human-readable detail — on failure, enough to locate the divergence
/// (counts, first offending id/cell), not just "failed".
/// </param>
public readonly record struct SweepResult(bool Passed, string Detail)
{
    /// <summary>A passing result.</summary>
    public static SweepResult Pass(string detail = "ok") => new(true, detail);

    /// <summary>A failing result.</summary>
    public static SweepResult Fail(string detail) => new(false, detail);
}
