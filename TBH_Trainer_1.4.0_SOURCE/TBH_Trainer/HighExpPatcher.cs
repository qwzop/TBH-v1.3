namespace TBH_Trainer;

/// <summary>
/// Runtime "High EXP on Kill" feature, ported from BabyGroot's Cheat Engine
/// script (TaskBarHero DEMO, 2025-10-13).
///
/// Strategy:
///   1. AOB-scan the loaded GameAssembly.dll for the unique instruction that
///      reads the monster's EXP reward field (movss xmm?, [reg+0x3A8]) using
///      a wildcard pattern so it works regardless of which register is used.
///   2. Allocate an RWX code cave within ±1 GB of the site (so a 5-byte rel32
///      JMP can reach it).
///   3. Write into the cave:
///        mov  dword [reg+0x3A8], &lt;imm32 = (float)expValue bits&gt;   ; 10 bytes
///        movss xmm?, [reg+0x3A8]                                   ;  8 bytes (original)
///        jmp  &lt;site + 8&gt;                                          ;  5 bytes
///   4. Patch the site with:
///        jmp  &lt;cave&gt;                                              ;  5 bytes
///        nop nop nop                                              ;  3 bytes (padding)
///   5. The cave can be re-written cheaply to change the EXP value live.
///   6. If the game is restarted while the hook is live the site will already
///      be patched. TryReconnectExistingCave() detects this and reattaches.
///
/// Net effect: every time the game reads the monster's EXP reward, the cave
/// first overwrites the reward field with the chosen amount, so the read
/// returns our number. Restoring the original 8 bytes turns it off cleanly.
/// </summary>
internal sealed class HighExpPatcher
{
    private readonly GameMemory _mem;

    // Wildcard AOB: F3 0F 10 ?? A8 03 00 00 (movss xmm?, [reg+0x3A8])
    // null = wildcard byte — matches any ModRM so it works on both 1.00.08 and 1.00.09.
    private static readonly byte?[] AobPattern =
    {
        0xF3, 0x0F, 0x10, null,
        0xA8, 0x03, 0x00, 0x00
    };

    // Cave layout offsets (so we can hot-patch the immediate without rebuilding).
    private const int CaveImmOffset = 6;   // byte index of the imm32 inside the cave
    private const int CaveSize      = 32;  // plenty of room for our 23-byte payload

    private IntPtr _siteAddress = IntPtr.Zero;
    private IntPtr _caveAddress = IntPtr.Zero;
    private byte[]? _originalBytes;
    private float _currentValue;

    public HighExpPatcher(GameMemory mem) => _mem = mem;

    public bool IsEnabled => _siteAddress != IntPtr.Zero;
    public float CurrentValue => _currentValue;
    public IntPtr SiteAddress => _siteAddress;
    public IntPtr CaveAddress => _caveAddress;

    /// <summary>
    /// Installs the hook (if not already installed) and updates the EXP value
    /// the cave will write. If already installed, only updates the cave's
    /// immediate — cheap, no re-patch.
    /// </summary>
    public (bool ok, string msg) Enable(float expValue)
    {
        if (!_mem.IsAttached) return (false, "Not attached to game.");
        if (_mem.GameAssemblyBase == IntPtr.Zero) return (false, "GameAssembly.dll not located.");

        if (IsEnabled) return UpdateValue(expValue);

        // 1. AOB scan (wildcard-capable)
        var hits = _mem.ScanGameAssemblyForPattern(AobPattern, maxHits: 4);
        if (hits.Count == 0)
        {
            // Site may already be patched from a previous session — try to reconnect.
            var reconnect = TryReconnectExistingCave(expValue);
            if (reconnect.ok) return reconnect;
            return (false, "AOB pattern not found in GameAssembly.dll — wrong game version?");
        }
        if (hits.Count > 1)
            return (false, $"AOB pattern matched {hits.Count} times — must be unique. Aborted.");

        IntPtr site = hits[0];

        // 2. Read & verify original 8 bytes
        var original = _mem.ReadBytes(site, 8);
        if (original.Length != 8)
            return (false, "Could not read original site bytes.");

        bool match = true;
        for (int i = 0; i < original.Length; i++)
        {
            if (AobPattern[i].HasValue && original[i] != AobPattern[i]!.Value)
            { match = false; break; }
        }
        if (!match)
            return (false, "Site bytes changed between scan and read (race?). Aborted.");

        byte modRm = original[3]; // preserve actual ModRM byte for cave encoding

        // 3. Allocate cave near site (within rel32 reach)
        IntPtr cave = _mem.AllocateNear(site, CaveSize);
        if (cave == IntPtr.Zero)
            return (false, "Could not allocate code cave near target site.");

        // 4. Build & write cave bytes
        byte[] caveBytes = BuildCave(site, cave, expValue, modRm);
        if (!_mem.WriteBytes(cave, caveBytes))
        {
            _mem.FreeRemote(cave);
            return (false, "Failed to write code cave.");
        }

        // 5. Patch site with JMP rel32 to cave + NOP padding
        byte[] sitePatch = BuildSitePatch(site, cave);
        if (!_mem.WriteBytes(site, sitePatch))
        {
            _mem.FreeRemote(cave);
            return (false, "Failed to patch site.");
        }

        // 6. Verify the site really took the patch (catches stale handles / AV)
        var readback = _mem.ReadBytes(site, 8);
        if (readback.Length != 8 || !readback.AsSpan().SequenceEqual(sitePatch))
        {
            _mem.WriteBytes(site, original); // best-effort rollback
            _mem.FreeRemote(cave);
            return (false, "Site patch did not verify on read-back.");
        }

        _siteAddress   = site;
        _caveAddress   = cave;
        _originalBytes = original;
        _currentValue  = expValue;

        return (true,
            $"High EXP ENABLED at {expValue:0} XP/kill. " +
            $"Site=0x{site.ToInt64():X}  Cave=0x{cave.ToInt64():X}");
    }

    /// <summary>Hot-updates only the imm32 in the cave when already enabled.</summary>
    public (bool ok, string msg) UpdateValue(float expValue)
    {
        if (!IsEnabled) return (false, "High EXP is not enabled yet.");
        byte[] imm = BitConverter.GetBytes(BitConverter.SingleToInt32Bits(expValue));
        if (!_mem.WriteBytes(_caveAddress + CaveImmOffset, imm))
            return (false, "Could not write the new value into the cave.");
        _currentValue = expValue;
        return (true, $"High EXP value updated to {expValue:0}.");
    }

    /// <summary>Restores the original 8 bytes and frees the cave.</summary>
    public (bool ok, string msg) Disable()
    {
        if (!IsEnabled) return (true, "Already disabled.");
        if (_originalBytes == null) return (false, "Internal error: original bytes missing.");

        if (!_mem.WriteBytes(_siteAddress, _originalBytes))
            return (false, "Could not restore original site bytes.");

        _mem.FreeRemote(_caveAddress);
        _siteAddress   = IntPtr.Zero;
        _caveAddress   = IntPtr.Zero;
        _originalBytes = null;
        return (true, "High EXP DISABLED. Original bytes restored, cave freed.");
    }

    /// <summary>Clears local state without writing to the game. Used on detach.</summary>
    public void ResetWithoutRestore()
    {
        _siteAddress   = IntPtr.Zero;
        _caveAddress   = IntPtr.Zero;
        _originalBytes = null;
        _currentValue  = 0;
    }

    // --- Reconnect helper ---------------------------------------------------

    /// <summary>
    /// When AOB scan fails because the site was already patched in a previous
    /// session, scan for our JMP+NOP signature and reconnect to the existing cave.
    /// </summary>
    private (bool ok, string msg) TryReconnectExistingCave(float expValue)
    {
        if (_mem.GameAssemblyBase == IntPtr.Zero) return (false, "");
        var sitePattern = new byte?[] { 0xE9, null, null, null, null, 0x90, 0x90, 0x90 };
        var candidates = _mem.ScanGameAssemblyForPattern(sitePattern, maxHits: 16);
        foreach (var site in candidates)
        {
            var siteBytes = _mem.ReadBytes(site, 8);
            if (siteBytes.Length < 8) continue;
            int rel = BitConverter.ToInt32(siteBytes, 1);
            long caveAddr = site.ToInt64() + 5 + rel;
            var cavePtr   = new IntPtr(caveAddr);
            var caveBytes = _mem.ReadBytes(cavePtr, 23);
            if (caveBytes.Length < 23) continue;
            // Verify: C7 8? A8 03 00 00 = mov [reg+0x3A8], imm32
            if (caveBytes[0] != 0xC7 || caveBytes[2] != 0xA8 || caveBytes[3] != 0x03 ||
                caveBytes[4] != 0x00 || caveBytes[5] != 0x00) continue;
            float current = BitConverter.Int32BitsToSingle(BitConverter.ToInt32(caveBytes, 6));
            var original  = new byte[8];
            Array.Copy(caveBytes, 10, original, 0, 8);
            _siteAddress   = site;
            _caveAddress   = cavePtr;
            _originalBytes = original;
            _currentValue  = current;
            if (expValue != current) UpdateValue(expValue);
            return (true,
                $"High EXP reconnected to existing cave at {expValue:0} XP/kill. " +
                $"Site=0x{site.ToInt64():X}  Cave=0x{caveAddr:X}");
        }
        return (false, "");
    }

    // --- Encoder helpers ----------------------------------------------------

    /// <summary>
    /// Cave bytes (23 bytes total):
    ///   C7 &lt;movModRm&gt; A8 03 00 00 &lt;imm32&gt;  mov dword [reg+0x3A8], imm32   (10 bytes)
    ///   F3 0F 10 &lt;movssModRm&gt; A8 03 00 00   movss xmm?, [reg+0x3A8]        ( 8 bytes, original)
    ///   E9 &lt;rel32&gt;                           jmp to (site + 8)              ( 5 bytes)
    /// </summary>
    private static byte[] BuildCave(IntPtr site, IntPtr cave, float value, byte movssModRm)
    {
        byte movModRm = (byte)(movssModRm & 0xC7); // same base register, opcode extension /0
        var buf = new byte[23];
        int p = 0;

        // mov dword [reg+0x3A8], imm32
        buf[p++] = 0xC7;
        buf[p++] = movModRm;
        buf[p++] = 0xA8; buf[p++] = 0x03; buf[p++] = 0x00; buf[p++] = 0x00; // disp32 = 0x3A8
        int imm = BitConverter.SingleToInt32Bits(value);
        buf[p++] = (byte)(imm);
        buf[p++] = (byte)(imm >> 8);
        buf[p++] = (byte)(imm >> 16);
        buf[p++] = (byte)(imm >> 24);

        // Original instruction: movss xmm?, [reg+0x3A8]
        buf[p++] = 0xF3; buf[p++] = 0x0F; buf[p++] = 0x10; buf[p++] = movssModRm;
        buf[p++] = 0xA8; buf[p++] = 0x03; buf[p++] = 0x00; buf[p++] = 0x00;

        // jmp <site+8>
        long jmpFrom = cave.ToInt64() + p + 5;
        long jmpTo   = site.ToInt64() + 8;
        int  rel32   = checked((int)(jmpTo - jmpFrom));
        buf[p++] = 0xE9;
        buf[p++] = (byte)(rel32);
        buf[p++] = (byte)(rel32 >> 8);
        buf[p++] = (byte)(rel32 >> 16);
        buf[p++] = (byte)(rel32 >> 24);

        return buf;
    }

    /// <summary>
    /// Site patch (8 bytes): jmp rel32 to cave + 3 NOPs to pad to the
    /// original 8-byte instruction length so we never leave garbage halves.
    /// </summary>
    private static byte[] BuildSitePatch(IntPtr site, IntPtr cave)
    {
        var buf = new byte[8];
        long jmpFrom = site.ToInt64() + 5;
        long jmpTo   = cave.ToInt64();
        int  rel32   = checked((int)(jmpTo - jmpFrom));
        buf[0] = 0xE9;
        buf[1] = (byte)(rel32);
        buf[2] = (byte)(rel32 >> 8);
        buf[3] = (byte)(rel32 >> 16);
        buf[4] = (byte)(rel32 >> 24);
        buf[5] = 0x90; buf[6] = 0x90; buf[7] = 0x90;
        return buf;
    }
}
