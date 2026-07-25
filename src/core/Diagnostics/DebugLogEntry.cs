namespace Hollowdeep.Core.Diagnostics;

/// <summary>Severity of a <see cref="DebugLogEntry"/>.</summary>
public enum DebugLogSeverity
{
    /// <summary>Informational output (command echo, sweep passes).</summary>
    Info = 0,

    /// <summary>
    /// Something a developer should see — e.g. ADR-0001's dispatch-loop purge
    /// of an invalid tickable, which must surface here rather than be silently
    /// absorbed.
    /// </summary>
    Warning = 1,

    /// <summary>A failed sweep, failed command, or invariant violation.</summary>
    Error = 2,
}

/// <summary>One line in the debug console's log buffer.</summary>
/// <param name="Sequence">Monotonic per console instance; never reused.</param>
/// <param name="Severity">Display severity.</param>
/// <param name="Message">The text. Single line by convention.</param>
public readonly record struct DebugLogEntry(long Sequence, DebugLogSeverity Severity, string Message);
