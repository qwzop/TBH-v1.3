#pragma warning disable CA1416

namespace TBH_Trainer;

/// <summary>
/// Applies the "forum" cheats by editing GameAssembly.dll on disk while the
/// game is CLOSED. Every write is guarded: the current bytes must match the
/// expected "original" (or "patched") signature before anything is written,
/// so a wrong game version refuses safely instead of corrupting the file.
/// A one-time .denoiser.bak backup lets the user Restore the pristine DLL.
///
/// Offsets are per-build in <see cref="GameBuildProfile.StaticFeatures"/>.
/// </summary>
internal sealed class StaticPatcher
{
    public enum FeatureState { Unknown, Original, Patched, Mismatch }

    public sealed class Feature
    {
        public required string Name { get; set; }
        public required string Description { get; set; }
        public required long Offset { get; set; }
        public required byte[] Original { get; set; }
        public required byte[] Patched { get; set; }
        public FeatureState State { get; set; } = FeatureState.Unknown;
    }

    public string DllPath { get; }
    public string BackupPath => DllPath + ".denoiser.bak";

    public StaticPatcher(string dllPath) => DllPath = dllPath;

    public bool DllExists => File.Exists(DllPath);
    public bool BackupExists => File.Exists(BackupPath);

    /// <summary>Best-effort auto-detection of the Steam install; null if not found.</summary>
    public static string? AutoDetectDll()
    {
        string[] candidates =
        {
            @"C:\Program Files (x86)\Steam\steamapps\common\TaskbarHero\GameAssembly.dll",
            @"C:\Program Files\Steam\steamapps\common\TaskbarHero\GameAssembly.dll",
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>True if the DLL is locked (e.g. the game is running).</summary>
    public bool IsFileLocked()
    {
        try
        {
            using var fs = new FileStream(DllPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException) { return true; }
        catch (UnauthorizedAccessException) { return true; }
    }

    /// <summary>Reads the bytes at each offset and classifies the feature state.</summary>
    public void Refresh(IReadOnlyList<Feature> features)
    {
        using var fs = new FileStream(DllPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        foreach (var f in features)
            f.State = Classify(fs, f);
    }

    private static FeatureState Classify(FileStream fs, Feature f)
    {
        var buf = new byte[Math.Max(f.Original.Length, f.Patched.Length)];
        fs.Seek(f.Offset, SeekOrigin.Begin);
        int read = fs.Read(buf, 0, buf.Length);
        if (read < f.Original.Length) return FeatureState.Mismatch;
        if (buf.AsSpan(0, f.Original.Length).SequenceEqual(f.Original)) return FeatureState.Original;
        if (buf.AsSpan(0, f.Patched.Length).SequenceEqual(f.Patched)) return FeatureState.Patched;
        return FeatureState.Mismatch;
    }

    /// <summary>Reads raw bytes at a file offset for manual inspection/debugging.</summary>
    public byte[] ReadBytes(long offset, int count)
    {
        using var fs = new FileStream(DllPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        fs.Seek(offset, SeekOrigin.Begin);
        var buf = new byte[count];
        fs.Read(buf, 0, count);
        return buf;
    }

    private void EnsureBackup()
    {
        if (!BackupExists) File.Copy(DllPath, BackupPath);
    }

    public (bool ok, string msg) Apply(Feature f)  => Write(f, applying: true);
    public (bool ok, string msg) Revert(Feature f) => Write(f, applying: false);

    private (bool ok, string msg) Write(Feature f, bool applying)
    {
        if (!DllExists) return (false, "GameAssembly.dll not found.");
        if (IsFileLocked()) return (false, "GameAssembly.dll is in use. Close the game first.");

        byte[] expect = applying ? f.Original : f.Patched;
        byte[] write  = applying ? f.Patched  : f.Original;

        // Read current bytes (shared read) before taking an exclusive handle.
        var cur = new byte[Math.Max(expect.Length, write.Length)];
        using (var rs = new FileStream(DllPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            rs.Seek(f.Offset, SeekOrigin.Begin);
            rs.ReadExactly(cur, 0, cur.Length);
        }

        if (cur.AsSpan(0, write.Length).SequenceEqual(write))
        {
            f.State = applying ? FeatureState.Patched : FeatureState.Original;
            return (true, applying ? "Already applied." : "Already reverted.");
        }
        if (!cur.AsSpan(0, expect.Length).SequenceEqual(expect))
            return (false,
                $"Bytes at 0x{f.Offset:X} don't match expected [{Hex(expect)}] (found [{Hex(cur.AsSpan(0, expect.Length).ToArray())}]). " +
                "Wrong game version? Aborted — file untouched.");

        // Make the safety backup while no exclusive handle is held.
        try { EnsureBackup(); }
        catch (Exception ex) { return (false, $"Could not create backup: {ex.Message}. Aborted."); }

        using (var ws = new FileStream(DllPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            ws.Seek(f.Offset, SeekOrigin.Begin);
            ws.Write(write, 0, write.Length);
            ws.Flush();

            ws.Seek(f.Offset, SeekOrigin.Begin);
            var check = new byte[write.Length];
            ws.ReadExactly(check, 0, check.Length);
            if (!check.SequenceEqual(write))
                return (false, "Write verification failed — read-back did not match.");
        }

        f.State = applying ? FeatureState.Patched : FeatureState.Original;
        return (true, applying ? "Applied." : "Reverted.");
    }

    public (bool ok, string msg) RestoreBackup()
    {
        if (!BackupExists) return (false, "No backup found (.denoiser.bak).");
        if (IsFileLocked()) return (false, "GameAssembly.dll is in use. Close the game first.");
        try
        {
            File.Copy(BackupPath, DllPath, overwrite: true);
            return (true, "Restored GameAssembly.dll from backup.");
        }
        catch (Exception ex) { return (false, $"Restore failed: {ex.Message}"); }
    }

    private static string Hex(byte[] b) => string.Join(" ", b.Select(x => x.ToString("X2")));
}
