namespace Hollowdeep.Core.Diagnostics;

/// <summary>
/// The Tier 0 debug console core (systems-index #29): a command registry, an
/// invariant-sweep registry, and a bounded log buffer. Plain C# — the Godot
/// overlay in <c>src/tools/DebugConsole/</c> is a view that binds to this and
/// polls <see cref="Revision"/>, the house change-notification pattern.
///
/// Rules for command authors (ADR-0002/0003): commands mutate simulation state
/// ONLY through the same facades gameplay uses (<c>TerrainWorld</c>, granted
/// writer interfaces) — never by poking storage; the mutation-window assertion
/// fires on any attempt. The console also grows the entity-inspection surface
/// (reading stores) that replaces Godot's Remote Inspector for non-Node
/// entities.
/// </summary>
public sealed class DebugConsole
{
    private const int DefaultLogCapacity = 512;

    private readonly int _logCapacity;
    private readonly List<DebugLogEntry> _entries = [];
    private readonly SortedDictionary<string, DebugCommand> _commands = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, IDebugSweep> _sweeps = new(StringComparer.Ordinal);
    private long _nextSequence = 1;

    private readonly record struct DebugCommand(
        string Help,
        Func<IReadOnlyList<string>, string> Handler);

    /// <summary>Creates a console with the given log-buffer capacity.</summary>
    /// <param name="logCapacity">Max retained entries; oldest are dropped.</param>
    public DebugConsole(int logCapacity = DefaultLogCapacity)
    {
        if (logCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(logCapacity));
        _logCapacity = logCapacity;
        RegisterBuiltins();
    }

    /// <summary>
    /// Monotonic change counter (+1 per log append, clear, or registration).
    /// Views poll this per frame and rebuild only when it moved — no events,
    /// matching the entity-layer Revision contract.
    /// </summary>
    public long Revision { get; private set; }

    /// <summary>
    /// The retained log, oldest first. A live read-only view valid on the
    /// simulation thread; views copy what they render.
    /// </summary>
    public IReadOnlyList<DebugLogEntry> Entries => _entries;

    /// <summary>Appends a line to the log, evicting the oldest at capacity.</summary>
    public void Log(DebugLogSeverity severity, string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (_entries.Count >= _logCapacity)
            _entries.RemoveAt(0);
        _entries.Add(new DebugLogEntry(_nextSequence++, severity, message));
        Revision++;
    }

    /// <summary>
    /// Registers a command. Names are case-insensitive (stored lower-case) and
    /// must be unique. The handler receives the whitespace-split arguments
    /// (command name excluded) and returns the text to log; exceptions it
    /// throws are caught and logged as errors — a broken debug command must
    /// never take the session down.
    /// </summary>
    public void RegisterCommand(string name, string help, Func<IReadOnlyList<string>, string> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        string key = NormalizeName(name);
        if (!_commands.TryAdd(key, new DebugCommand(help ?? string.Empty, handler)))
            throw new InvalidOperationException($"Debug command '{key}' is already registered.");
        Revision++;
    }

    /// <summary>Registers an invariant sweep (unique by <see cref="IDebugSweep.Name"/>).</summary>
    public void RegisterSweep(IDebugSweep sweep)
    {
        ArgumentNullException.ThrowIfNull(sweep);
        string key = NormalizeName(sweep.Name);
        if (!_sweeps.TryAdd(key, sweep))
            throw new InvalidOperationException($"Debug sweep '{key}' is already registered.");
        Revision++;
    }

    /// <summary>
    /// Parses and runs one input line (whitespace-split; no quoting). Echoes
    /// the input, then logs the command's output or error. Empty input is a
    /// no-op.
    /// </summary>
    public void Execute(string commandLine)
    {
        ArgumentNullException.ThrowIfNull(commandLine);
        string[] parts = commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return;

        Log(DebugLogSeverity.Info, $"> {commandLine.Trim()}");
        string key = parts[0].ToLowerInvariant();
        if (!_commands.TryGetValue(key, out DebugCommand command))
        {
            Log(DebugLogSeverity.Error, $"Unknown command '{key}'. Try 'help'.");
            return;
        }

        try
        {
            string output = command.Handler(parts[1..]);
            if (!string.IsNullOrEmpty(output))
                Log(DebugLogSeverity.Info, output);
        }
        catch (Exception e)
        {
            Log(DebugLogSeverity.Error, $"{key}: {e.GetType().Name}: {e.Message}");
        }
    }

    /// <summary>
    /// Runs every registered sweep in name order, logging each result, and
    /// returns the number of failures. The on-cadence debug-build scheduler
    /// (arriving with the first system that registers a sweep) calls this too.
    /// </summary>
    public int RunAllSweeps()
    {
        int failures = 0;
        foreach ((string name, IDebugSweep sweep) in _sweeps)
        {
            if (!RunSweep(name, sweep))
                failures++;
        }
        if (_sweeps.Count == 0)
            Log(DebugLogSeverity.Info, "No sweeps registered.");
        return failures;
    }

    private bool RunSweep(string name, IDebugSweep sweep)
    {
        SweepResult result;
        try
        {
            result = sweep.Run();
        }
        catch (Exception e)
        {
            result = SweepResult.Fail($"{e.GetType().Name}: {e.Message}");
        }
        Log(
            result.Passed ? DebugLogSeverity.Info : DebugLogSeverity.Error,
            $"sweep {name}: {(result.Passed ? "PASS" : "FAIL")} — {result.Detail}");
        return result.Passed;
    }

    private static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Contains(' '))
            throw new ArgumentException("Command/sweep names must be non-empty and contain no spaces.", nameof(name));
        return name.ToLowerInvariant();
    }

    private void RegisterBuiltins()
    {
        RegisterCommand("help", "List all commands.", _ =>
        {
            var lines = _commands.Select(pair => $"  {pair.Key} — {pair.Value.Help}");
            return "Commands:\n" + string.Join('\n', lines);
        });

        RegisterCommand("echo", "Echo the arguments back.", args => string.Join(' ', args));

        RegisterCommand("clear", "Clear the log buffer.", _ =>
        {
            _entries.Clear();
            Revision++;
            return string.Empty;
        });

        RegisterCommand("sweeps", "List registered invariant sweeps.", _ =>
        {
            if (_sweeps.Count == 0)
                return "No sweeps registered (systems register theirs as they are built).";
            var lines = _sweeps.Select(pair => $"  {pair.Key} — {pair.Value.Description}");
            return "Sweeps:\n" + string.Join('\n', lines);
        });

        RegisterCommand("sweep", "Run 'sweep all' or 'sweep <name>'.", args =>
        {
            if (args.Count == 0 || args[0].Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                int failures = RunAllSweeps();
                return failures == 0 ? "All sweeps passed." : $"{failures} sweep(s) FAILED.";
            }
            string key = args[0].ToLowerInvariant();
            if (!_sweeps.TryGetValue(key, out IDebugSweep? sweep))
                return $"Unknown sweep '{key}'. Try 'sweeps'.";
            RunSweep(key, sweep);
            return string.Empty;
        });
    }
}
