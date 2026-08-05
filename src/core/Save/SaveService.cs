using Hollowdeep.Core.Sim;

namespace Hollowdeep.Core.Save;

/// <summary>
/// Colony save slots (save anytime in RealTime; quicksave F5/F9). Manual and autosaves
/// are REFUSED in TurnBased — combat persists via the battle checkpoint only. Load
/// always resumes the latest save by the in-file monotonic counter (never mtime);
/// there is deliberately no pre-battle rewind.
/// </summary>
public sealed class SaveService
{
    private readonly GameWorld _world;
    private long _counter;

    public string Directory { get; }
    public string QuickSavePath => System.IO.Path.Combine(Directory, "quick.save");
    public string BattleEndAutosavePath => System.IO.Path.Combine(Directory, "auto-battleend.save");
    public string SwitchInAutosavePath => System.IO.Path.Combine(Directory, "auto-switchin.save");
    public string CheckpointPath => System.IO.Path.Combine(Directory, "battle.checkpoint");

    /// <summary>Fired when a save file exists but cannot be parsed (loud, never silent — ADR-0004).</summary>
    public event Action<string, Exception>? CorruptSaveEncountered;

    public SaveService(GameWorld world, string directory)
    {
        _world = world;
        Directory = directory;
        System.IO.Directory.CreateDirectory(directory);
    }

    public long NextCounter() => ++_counter;
    internal long PeekCounter() => _counter;
    internal void NoteCounter(long value) => _counter = Math.Max(_counter, value);

    /// <summary>Returns false (with a reason) instead of saving during TurnBased (ADR-0004).</summary>
    public bool TrySaveColony(string path, out string refusal)
    {
        if (_world.Time.Mode == SimMode.TurnBased)
        {
            refusal = "Saves are unavailable during battle — the battle checkpoint preserves progress automatically.";
            return false;
        }
        refusal = "";
        WriteAtomically(path, SaveCodec.ColonyWriterId, NextCounter());
        return true;
    }

    public bool TryQuickSave(out string refusal) => TrySaveColony(QuickSavePath, out refusal);

    /// <summary>Battle-end autosave, written after reconcile back in RealTime.</summary>
    public void SaveBattleEndAutosave() => WriteAtomically(BattleEndAutosavePath, SaveCodec.ColonyWriterId, NextCounter());

    internal void WriteAtomically(string path, byte writerId, long counter)
    {
        string tmp = path + ".tmp";
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
            SaveCodec.WriteFile(_world, fs, writerId, counter);
        File.Move(tmp, path, overwrite: true);
    }

    public SaveHeader Load(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
        var header = SaveCodec.ReadFile(_world, fs);
        NoteCounter(header.SaveOrder);
        return header;
    }

    /// <summary>All candidate saves with parseable headers, newest (highest counter) first.</summary>
    public List<(string Path, SaveHeader Header)> ListSaves()
    {
        var result = new List<(string, SaveHeader)>();
        if (!System.IO.Directory.Exists(Directory)) return result;
        foreach (var path in System.IO.Directory.EnumerateFiles(Directory))
        {
            if (!path.EndsWith(".save") && !path.EndsWith(".checkpoint")) continue;
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
                result.Add((path, SaveCodec.ReadHeader(fs)));
            }
            catch (Exception ex)
            {
                CorruptSaveEncountered?.Invoke(path, ex);
            }
        }
        result.Sort((a, b) => b.Item2.SaveOrder.CompareTo(a.Item2.SaveOrder));
        return result;
    }

    /// <summary>
    /// Resumes the newest save. A corrupt battle checkpoint falls back to the switch-in
    /// autosave (resuming in-battle at activation 0), never silently.
    /// </summary>
    public bool TryLoadLatest(out string loadedPath)
    {
        foreach (var (path, _) in ListSaves())
        {
            try
            {
                Load(path);
                loadedPath = path;
                return true;
            }
            catch (Exception ex)
            {
                CorruptSaveEncountered?.Invoke(path, ex);
            }
        }
        loadedPath = "";
        return false;
    }
}
