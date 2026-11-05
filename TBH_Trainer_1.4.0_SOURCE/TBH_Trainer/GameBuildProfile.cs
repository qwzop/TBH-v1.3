namespace TBH_Trainer;

/// <summary>
/// Per-game-build offsets from Il2CppDumper (dump.cs). V1009 parsed from 1.00.09 hotfix dump.
/// </summary>
internal sealed class GameBuildProfile
{
    public required string VersionLabel { get; init; }
    public required int BuildId { get; init; }

    public required (string Name, int Rva, int DiskOffset, byte[] Signature)[] ActkTargets { get; init; }

    public required long HpStatic { get; init; }
    public required long[] HpOffsets { get; init; }
    public required long AtkStatic { get; init; }
    public required long[] AtkOffsets { get; init; }

    /// <summary>Stash insert: ud.Stash.jbg (1.00.08) / ue.Stash.jbn (1.00.09).</summary>
    public required int StashInsertRva { get; init; }
    /// <summary>Stash slot lookup: ud.Stash.jbp / ue.Stash.jbw.</summary>
    public required int StashSlotRva { get; init; }
    /// <summary>Stash init: ud.Stash.jbb / ue.Stash.jbi.</summary>
    public required int StashInitRva { get; init; }
    public required int StashSaveDataCtor { get; init; }
    /// <summary>ue.ti.blk — primary add-item API on 1.00.09 (0 = use stash path only).</summary>
    public required int ItemAddRva { get; init; }
    /// <summary>Il2cpp namespace for stash type: "ud" or "ue".</summary>
    public required string StashNamespace { get; init; }

    public required List<StaticPatcher.Feature> StaticFeatures { get; init; }

    // Legacy property names for TrainerBridge / hook shared mem
    public int StashJbg => StashInsertRva;
    public int StashJbp => StashSlotRva;
    public int StashJbb => StashInitRva;

    public static GameBuildProfile V1008 { get; } = new()
    {
        VersionLabel = "1.00.08",
        BuildId = 10008,
        ActkTargets =
        [
            ("ObscuredCheatingDetector.Check",       0x6CC1C0, 0x6CC1C0, [0x41, 0x56, 0x48, 0x83, 0xEC, 0x20]),
            ("ObscuredCheatingDetector.Compare",     0x6CC350, 0x6CC350, [0x48, 0x89, 0x5C, 0x24, 0x10, 0x48]),
            ("ObscuredCheatingDetector.CompareExt",  0x6CC430, 0x6CC430, [0x48, 0x89, 0x5C, 0x24, 0x10, 0x48]),
            ("InjectionDetector.Check",              0x6CB6E0, 0x6CB6E0, [0x40, 0x53, 0x48, 0x83, 0xEC, 0x20]),
            ("SpeedHackDetector.Update",             0x6D10D0, 0x6D10D0, [0x40, 0x56, 0x48, 0x83, 0xEC, 0x70]),
            ("SpeedHackDetector.OnApplicationPause", 0x6D1040, 0x6D1040, [0x48, 0x89, 0x5C, 0x24, 0x08, 0x57]),
        ],
        HpStatic = 0x5AEE670,
        HpOffsets = [0x28, 0x68, 0xB0, 0x40],
        AtkStatic = 0x57C6A50,
        AtkOffsets = [0xB8, 0x40, 0x10, 0x20, 0x18, 0x3C],
        StashInsertRva = 0x8F0320,
        StashSlotRva = 0x8F15D0,
        StashInitRva = 0x8EF8C0,
        StashSaveDataCtor = 0x99C9F0,
        ItemAddRva = 0,
        StashNamespace = "ud",
        StaticFeatures = BuildStaticFeatures1008(),
    };

    /// <summary>Taskbar Hero 1.00.09 hotfix — offsets verified Jun 2026.</summary>
    public static GameBuildProfile V1009 { get; } = new()
    {
        VersionLabel = "1.00.09",
        BuildId = 10009,
        ActkTargets =
        [
            // Signatures verified in live process Jun 2026.
            ("ObscuredCheatingDetector.Check",       0x6C1840, 0x6C0240, [0x41, 0x56, 0x48, 0x83, 0xEC, 0x20]),
            ("ObscuredCheatingDetector.Compare",     0x6C19D0, 0x6C03D0, [0x48, 0x89, 0x5C, 0x24, 0x10, 0x48]),
            ("ObscuredCheatingDetector.CompareExt",  0x6C1AB0, 0x6C04B0, [0x48, 0x89, 0x5C, 0x24, 0x10, 0x48]),
            ("InjectionDetector.Check",              0x6C0D60, 0x6BF760, [0x40, 0x53, 0x48, 0x83, 0xEC, 0x20]),
            ("SpeedHackDetector.Update",             0x6C6750, 0x6C5150, [0x40, 0x56, 0x48, 0x83, 0xEC, 0x70]),
            ("SpeedHackDetector.OnApplicationPause", 0x6C66C0, 0x6C50C0, [0x48, 0x89, 0x5C, 0x24, 0x08, 0x57]),
        ],
        // 1.00.09: HP resolves via static pointer [base+0x57BC000] -> +0xB8 -> +0x30 -> +0x20 -> +0xB0 -> +0x38.
        // AtkStatic/AtkOffsets unused for 1.00.09; HeroStats resolves Unit at runtime.
        HpStatic  = 0x57BC000,
        HpOffsets = [0xB8, 0x30, 0x20, 0xB0, 0x38],
        AtkStatic = 0,
        AtkOffsets = [],
        StashInsertRva = 0x8D62F0,  // ue.Stash.jbn(ulong, StashCache)
        StashSlotRva   = 0x8D75A0,  // ue.Stash.jbw(int)
        StashInitRva   = 0x8D5890,  // ue.Stash.jbi()
        StashSaveDataCtor = 0x94DEF0,
        ItemAddRva = 0x8BF830,      // ue.ti.blk(int itemKey, ulong uid, bool)
        StashNamespace = "ue",
        StaticFeatures = BuildStaticFeatures1009(),
    };

    public static IReadOnlyList<GameBuildProfile> All { get; } = [V1009, V1008];

    public static GameBuildProfile? Detect(GameMemory mem)
    {
        if (!mem.IsAttached || mem.GameAssemblyBase == IntPtr.Zero) return null;
        foreach (var p in All)
        {
            if (!p.IsConfigured) continue;
            if (p.MatchActk(mem)) return p;
        }
        return null;
    }

    /// <summary>
    /// Prefer in-memory signature match; then compare process bytes at RVA to on-disk bytes at DiskOffset.
    /// </summary>
    public static GameBuildProfile? DetectForAttach(GameMemory mem, string? gameAssemblyDllPath)
    {
        var fromMem = Detect(mem);
        if (fromMem != null) return fromMem;

        var path = ResolveDllPath(mem, gameAssemblyDllPath);
        if (path != null)
        {
            foreach (var p in All)
            {
                if (!p.IsConfigured) continue;
                if (p.MatchActkMemoryToDisk(mem, path)) return p;
            }

            var fromDisk = DetectFromDisk(path);
            if (fromDisk != null) return fromDisk;
        }

        return null;
    }

    public static string? ResolveDllPath(GameMemory mem, string? preferredPath)
    {
        if (!string.IsNullOrWhiteSpace(preferredPath) && File.Exists(preferredPath))
            return preferredPath;
        if (!string.IsNullOrWhiteSpace(mem.GameAssemblyPath) && File.Exists(mem.GameAssemblyPath))
            return mem.GameAssemblyPath;
        return StaticPatcher.AutoDetectDll();
    }

    public static GameBuildProfile? DetectFromDisk(string dllPath)
    {
        if (!File.Exists(dllPath)) return null;
        foreach (var p in All)
        {
            if (!p.IsConfigured) continue;
            if (p.MatchActkFile(dllPath)) return p;
        }
        return null;
    }

    public bool IsConfigured => ActkTargets.Length > 0 && ActkTargets[0].Rva > 0;

    public bool MatchActk(GameMemory mem)
    {
        foreach (var (_, rva, _, sig) in ActkTargets)
        {
            var cur = mem.ReadBytes(mem.GameAssemblyBase + rva, sig.Length);
            if (cur.Length != sig.Length) return false;
            for (int i = 0; i < sig.Length; i++)
                if (cur[i] != sig[i]) return false;
        }
        return true;
    }

    /// <summary>Loaded module at RVA must match the same bytes in GameAssembly.dll on disk.</summary>
    public bool MatchActkMemoryToDisk(GameMemory mem, string dllPath)
    {
        if (!mem.IsAttached || mem.GameAssemblyBase == IntPtr.Zero || !File.Exists(dllPath))
            return false;

        using var fs = new FileStream(dllPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        int matched = 0;
        foreach (var (_, rva, diskOff, sig) in ActkTargets)
        {
            int len = sig.Length;
            long off = diskOff > 0 ? diskOff : rva;
            if (off <= 0 || off + len > fs.Length) return false;

            var disk = new byte[len];
            fs.Seek(off, SeekOrigin.Begin);
            if (fs.Read(disk, 0, len) != len) return false;

            var live = mem.ReadBytes(mem.GameAssemblyBase + rva, len);
            if (live.Length == len && live.AsSpan().SequenceEqual(disk))
                matched++;
        }
        // Allow Obscured Check to differ in RAM vs file; other ACTk RVAs must match.
        return matched >= ActkTargets.Length - 1;
    }

    public byte[] ResolveActkSignature(string dllPath, int rva, int diskOff, byte[] fallback)
    {
        int len = fallback.Length;
        if (len <= 0) return fallback;

        if (!string.IsNullOrEmpty(dllPath) && File.Exists(dllPath))
        {
            long off = diskOff > 0 ? diskOff : rva;
            try
            {
                using var fs = new FileStream(dllPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (off > 0 && off + len <= fs.Length)
                {
                    fs.Seek(off, SeekOrigin.Begin);
                    var buf = new byte[len];
                    if (fs.Read(buf, 0, len) == len) return buf;
                }
            }
            catch { /* use fallback */ }
        }

        return fallback;
    }

    private bool MatchActkFile(string dllPath)
    {
        using var fs = new FileStream(dllPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        foreach (var (_, _, diskOff, sig) in ActkTargets)
        {
            long off = diskOff > 0 ? diskOff : 0;
            if (off <= 0 || off + sig.Length > fs.Length) return false;
            fs.Seek(off, SeekOrigin.Begin);
            var buf = new byte[sig.Length];
            if (fs.Read(buf, 0, buf.Length) != sig.Length) return false;
            for (int i = 0; i < sig.Length; i++)
                if (buf[i] != sig[i]) return false;
        }
        return true;
    }

    private static List<StaticPatcher.Feature> BuildStaticFeatures1008() => new()
    {
        new StaticPatcher.Feature
        {
            Name = "Unlimited Gold",
            Description = "Forces the gold reward to 1,000,000,000 (mov rdx, 0x3B9ACA00).",
            Offset = 0x8AAB6A,
            Original = [0x80, 0x3D, 0x1F, 0x5B, 0x24, 0x05, 0x00],
            Patched  = [0x48, 0xC7, 0xC2, 0x00, 0xCA, 0x9A, 0x3B],
        },
        new StaticPatcher.Feature
        {
            Name = "Crash Protection",
            Description = "Skips a run-once init that crashes alongside Unlimited Gold (jne -> jmp).",
            Offset = 0x8AAB77,
            Original = [0x75],
            Patched  = [0xEB],
        },
        new StaticPatcher.Feature
        {
            Name = "Cube XP (Synthesis)",
            Description = "Cube synthesis returns 1B XP for cube ID 11 and 1000 for the rest.",
            Offset = 0x95ABD0,
            Original =
            [
                0x48, 0x89, 0x5C, 0x24, 0x08, 0x57, 0x48, 0x83,
                0xEC, 0x40, 0x80, 0x3D, 0x0B, 0x61, 0x19, 0x05, 0x00,
            ],
            Patched =
            [
                0x83, 0xFA, 0x0B, 0x74, 0x06, 0xB8, 0xE8, 0x03,
                0x00, 0x00, 0xC3, 0xB8, 0x00, 0xCA, 0x9A, 0x3B, 0xC3,
            ],
        },
        new StaticPatcher.Feature
        {
            Name = "DLC Unlocker",
            Description = "DLC ownership check early-returns 1/true (mov eax, 1; ret).",
            Offset = 0xB9A400,
            Original = [0x40, 0x53, 0x48, 0x83, 0xEC, 0x20],
            Patched  = [0xB8, 0x01, 0x00, 0x00, 0x00, 0xC3],
        },
        new StaticPatcher.Feature
        {
            Name = "Unlock All Pets",
            Description = "Pet ownership check early-returns 1/true (mov eax, 1; ret).",
            Offset = 0x9976B0,
            Original = [0x48, 0x89, 0x5C, 0x24, 0x10, 0x57],
            Patched  = [0xB8, 0x01, 0x00, 0x00, 0x00, 0xC3],
        },
    };

    /// <summary>Static disk patches for 1.00.09 — verified Jun 2026.</summary>
    private static List<StaticPatcher.Feature> BuildStaticFeatures1009() => new()
    {
        new StaticPatcher.Feature
        {
            Name = "Unlimited Gold",
            Description = "Forces the gold reward to 1,000,000,000 (mov rdx, 0x3B9ACA00 instead of computed value).",
            Offset = 0x95C6F5,
            Original = [0x48, 0x8B, 0xD3, 0x48, 0x8B, 0xCD, 0xE8],
            Patched  = [0x48, 0xC7, 0xC2, 0x00, 0xCA, 0x9A, 0x3B],
        },
        new StaticPatcher.Feature
        {
            Name = "Crash Protection",
            Description = "Skips a run-once init that crashes alongside Unlimited Gold (jne -> jmp).",
            Offset = 0x95C70E,
            Original = [0x75],
            Patched  = [0xEB],
        },
        new StaticPatcher.Feature
        {
            Name = "Cube XP (Synthesis)",
            Description = "AccountStatus.jzc (RVA 0x948FB0) returns 1B XP for CubeExpPercent (ID 0x0B) and 1000 for all other synthesis types.",
            Offset = 0x948FB0,
            Original =
            [
                // Real prologue bytes from GameAssembly.dll 1.00.09
                0x48, 0x8B, 0x15, 0xB1, 0x03, 0xE4, 0x04,  // mov rdx, [rip+...]
                0x48, 0x8D, 0x4C, 0x24, 0x38,               // lea rcx, [rsp+38h]
                0xE8, 0x9F, 0xA7, 0x15, 0x02,               // call init_check
            ],
            Patched =
            [
                // cmp edx, 0x0B   -- is it CubeExpPercent?
                0x83, 0xFA, 0x0B,
                // je +6           -- yes → return 1,000,000,000
                0x74, 0x06,
                // mov eax, 1000   -- no → return 1000
                0xB8, 0xE8, 0x03, 0x00, 0x00,
                // ret
                0xC3,
                // mov eax, 1000000000 (0x3B9ACA00)
                0xB8, 0x00, 0xCA, 0x9A, 0x3B,
                // ret
                0xC3,
            ],
        },
        new StaticPatcher.Feature
        {
            Name = "DLC Unlocker",
            Description = "DLC ownership check (DLCManager.gxq) early-returns 1/true (mov eax, 1; ret).",
            Offset = 0xB7C350,
            Original = [0x40, 0x53, 0x48, 0x83, 0xEC, 0x20],
            Patched  = [0xB8, 0x01, 0x00, 0x00, 0x00, 0xC3],
        },
        new StaticPatcher.Feature
        {
            Name = "Unlock All Pets",
            Description = "Pet ownership check early-returns 1/true (mov eax, 1; ret).",
            Offset = 0x9960C0,
            Original = [0x48, 0x89, 0x5C, 0x24, 0x18, 0x56],
            Patched  = [0xB8, 0x01, 0x00, 0x00, 0x00, 0xC3],
        },
    };
}
