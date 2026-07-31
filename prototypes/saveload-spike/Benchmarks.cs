// PROTOTYPE - NOT FOR PRODUCTION
// Question: what does a save cost in bytes and milliseconds at MVP and full-vision scale?
// Date: 2026-07-26

using System.Diagnostics;
using System.IO.Compression;

namespace SaveLoadSpike;

public static class Benchmarks
{
    public static void RunAll()
    {
        Console.WriteLine("\n=== COST ===");
        SizeAndTime();
        AutosaveBudget();
    }

    private static GameWorld Populate(int sx, int sy, int layers, int colonists)
    {
        var w = new GameWorld(sx, sy, layers);
        var rng = new Random(3);
        using (w.Manager.Window.Open())
        {
            // A dug-in colony: a good fraction of cells carry walls with varied HP/style.
            for (int z = 0; z < layers; z++)
                for (int y = 0; y < sy; y++)
                    for (int x = 0; x < sx; x++)
                        if (((x * 7 + y * 13 + z * 3) & 3) != 0)
                            w.World.SetWall(new CellCoord(x, y, z), w.Dirt, (byte)((x + y) & 3));
            for (int i = 0; i < colonists; i++)
                w.Colonists.Spawn($"C{i}", new CellCoord(i % sx, (i / sx) % sy, 0), 10);
            for (int i = 0; i < 40; i++) w.Doors.Spawn(new CellCoord(rng.Next(sx), rng.Next(sy), 0));
            for (int i = 0; i < 300; i++)
                w.Items.SpawnStack(i % 2 == 0 ? "dirt" : "rock_floor", rng.Next(1, 50),
                                   new CellCoord(rng.Next(sx), rng.Next(sy), rng.Next(layers)));
        }
        w.RebuildDerived();
        return w;
    }

    private static void SizeAndTime()
    {
        foreach ((string label, int sx, int sy, int lz, int colonists) in new[]
                 { ("MVP  128x128x16", 128, 128, 16, 10), ("Full 256x256x32", 256, 256, 32, 30) })
        {
            GameWorld w = Populate(sx, sy, lz, colonists);

            byte[] save = WorldSave.Save(w);                 // warm
            const int reps = 20;
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < reps; i++) save = WorldSave.Save(w);
            sw.Stop();
            double saveMs = sw.Elapsed.TotalMilliseconds / reps;

            var target = new GameWorld(sx, sy, lz);
            WorldSave.Load(target, save);                     // warm
            sw.Restart();
            for (int i = 0; i < reps; i++) WorldSave.Load(target, save);
            sw.Stop();
            double loadMs = sw.Elapsed.TotalMilliseconds / reps;

            using var ms = new MemoryStream();
            using (var gz = new GZipStream(ms, CompressionLevel.Fastest, true)) gz.Write(save);
            long gzipped = ms.Length;

            Console.WriteLine($"  {label}: save {save.Length / 1024.0 / 1024.0,5:F2} MB " +
                              $"({gzipped / 1024.0 / 1024.0,4:F2} MB gzipped, {100.0 * gzipped / save.Length:F0}%)   " +
                              $"write {saveMs,6:F1} ms   read {loadMs,6:F1} ms");
        }
    }

    private static void AutosaveBudget()
    {
        // CD-9: autosave fires at the mode switch into tactics and at battle end — both are
        // non-gameplay moments, but a multi-second stall would still be felt.
        GameWorld w = Populate(128, 128, 16, 10);
        byte[] save = WorldSave.Save(w);
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 10; i++) save = WorldSave.Save(w);
        sw.Stop();
        double ms = sw.Elapsed.TotalMilliseconds / 10;
        Console.WriteLine($"  MVP autosave at the mode switch: {ms:F1} ms " +
                          $"({(ms < 100 ? "imperceptible — no async save machinery needed for MVP" : "consider async/background save")})");
        Console.WriteLine($"  = {ms / 16.6:F1} frames if done synchronously");
    }
}
