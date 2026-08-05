using System.IO.Compression;
using Hollowdeep.Core.Sim;

namespace Hollowdeep.Core.Save;

/// <summary>
/// The rolling battle checkpoint (ADR-0004): written after each actor activation
/// resolves, plus an "activation 0" checkpoint immediately post-swap. Double-buffered
/// pooled snapshots with coalesce-newest backpressure — the sim thread serializes into
/// a pooled buffer and never blocks on disk; the writer thread gzips the newest
/// resolved state to a temp file and renames atomically. Quit flushes and joins.
/// </summary>
public sealed class CheckpointService : IDisposable
{
    private readonly GameWorld _world;
    private readonly SaveService _saves;

    private readonly MemoryStream[] _buffers = { new(1 << 21), new(1 << 21) };
    private int _freeBuffer;              // index of the buffer the sim may write next
    private MemoryStream? _pendingNewest; // newest resolved snapshot awaiting disk
    private long _pendingCounter;
    private readonly object _lock = new();
    private readonly object _writeLock = new(); // WritePendingOnce is callable from sim (drain) and writer threads
    private readonly AutoResetEvent _signal = new(false);
    private readonly Thread _thread;
    private volatile bool _stop;

    public int CheckpointsWritten { get; private set; }
    public ulong LastCheckpointActivation { get; private set; }

    public CheckpointService(GameWorld world, SaveService saves)
    {
        _world = world;
        _saves = saves;
        world.Time.SwitchedToTurnBased += OnSwitchedToTurnBased;
        world.Combat.ActivationResolved += OnActivationResolved;
        world.Time.ReturnedToRealTime += OnReturnedToRealTime;
        _thread = new Thread(WriterLoop) { IsBackground = true, Name = "hd-checkpoint" };
        _thread.Start();
    }

    private void OnSwitchedToTurnBased(SwitchTransitionData data)
    {
        // Activation-0 checkpoint, synchronous: quitting during squad placement must
        // resume in-battle, not pre-battle. The same bytes serve as the switch-in
        // autosave (the corrupt-checkpoint fallback) — both from the checkpoint writer.
        long counter = _saves.NextCounter();
        _saves.WriteAtomically(_saves.SwitchInAutosavePath, SaveCodec.CheckpointWriterId, counter);
        _saves.WriteAtomically(_saves.CheckpointPath, SaveCodec.CheckpointWriterId, _saves.NextCounter());
        CheckpointsWritten++;
        LastCheckpointActivation = 0;
    }

    private void OnActivationResolved()
    {
        // Sim thread: snapshot into a pooled buffer (µs-scale), coalesce-newest, signal.
        if (_world.Time.Mode != SimMode.TurnBased) return;
        lock (_lock)
        {
            var buffer = _buffers[_freeBuffer];
            buffer.SetLength(0);
            SaveCodec.WritePayload(_world, buffer);
            if (_pendingNewest == null)
                _freeBuffer = 1 - _freeBuffer; // previous free buffer is now pending
            // else: the still-unwritten pending snapshot is REPLACED (coalesce-newest);
            // the old pending buffer becomes the next free buffer implicitly.
            _pendingNewest = buffer;
            _pendingCounter = _saves.NextCounter();
            LastCheckpointActivation = _world.Combat.Encounter.ActivationCounter;
        }
        _signal.Set();
    }

    private void OnReturnedToRealTime()
    {
        // Battle over: the checkpoint is stale. Autosave the reconciled colony; the
        // higher save-order counter makes it the resume point.
        DrainPending();
        _saves.SaveBattleEndAutosave();
        try { if (File.Exists(_saves.CheckpointPath)) File.Delete(_saves.CheckpointPath); }
        catch (IOException) { /* stale checkpoint is harmless: counter ordering wins */ }
    }

    private void WriterLoop()
    {
        while (!_stop)
        {
            _signal.WaitOne(200);
            WritePendingOnce();
        }
    }

    private void WritePendingOnce()
    {
        lock (_writeLock)
        {
            MemoryStream? snapshot;
            long counter;
            lock (_lock)
            {
                snapshot = _pendingNewest;
                counter = _pendingCounter;
                _pendingNewest = null;
                if (snapshot != null)
                    _freeBuffer = _buffers[0] == snapshot ? 1 : 0; // writer owns this one; sim gets the other
            }
            if (snapshot == null) return;
            WriteSnapshot(snapshot, counter);
        }
    }

    private void WriteSnapshot(MemoryStream snapshot, long counter)
    {

        string tmp = _saves.CheckpointPath + ".tmp";
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
        using (var w = new BinaryWriter(fs, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            w.Write(SaveCodec.Magic);
            w.Write(SaveCodec.Version);
            w.Write(SaveCodec.CheckpointWriterId);
            w.Write(counter);
            w.Write((byte)SimMode.TurnBased);
            using var gzip = new GZipStream(fs, CompressionLevel.Fastest, leaveOpen: true);
            snapshot.Position = 0;
            snapshot.CopyTo(gzip);
        }
        File.Move(tmp, _saves.CheckpointPath, overwrite: true);
        CheckpointsWritten++;
    }

    /// <summary>Blocks until the newest pending snapshot is on disk (quit path).</summary>
    public void DrainPending()
    {
        WritePendingOnce();
    }

    public void Dispose()
    {
        _stop = true;
        _signal.Set();
        _thread.Join();
        DrainPending(); // quit flushes and joins the writer (ADR-0004)
    }
}
