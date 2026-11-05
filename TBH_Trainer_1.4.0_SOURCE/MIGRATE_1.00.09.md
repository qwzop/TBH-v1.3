# Migrating TBH Trainer to Taskbar Hero 1.00.09

Every game hotfix recompiles `GameAssembly.dll`. RVAs and static pointer bases shift.
The trainer refuses to patch when bytes do not match (safe), but features will show
**SKIP / MISMATCH** until you fill in the new numbers.

## What you need

1. **Taskbar Hero 1.00.09** installed (Steam updated).
2. **Il2CppDumper** — https://github.com/Perfare/Il2CppDumper
3. From the game folder:
   - `GameAssembly.dll`
   - `TaskbarHero_Data/il2cpp_data/Metadata/global-metadata.dat`
4. Optional: **Cheat Engine** for HP/ATK pointer scans.
5. Optional: community **Il2CppDumper `.hpp`** exports (same data as `dump.cs`, easier to browse):
   - `ACTk.Runtime.dll.hpp` — anti-cheat detector RVAs
   - `Assembly-CSharp.dll.hpp` — game types (`Stash`, `ti`, `Unit`, `pf`, `StageManager`)

`GameBuildProfile.V1009` was filled from `dump.cs` and **cross-checked** against community HPP (Jun 2026); RVAs match.

## Step 1 — Dump il2cpp

```text
Il2CppDumper.exe
  GameAssembly.dll
  global-metadata.dat
  output_folder
```

Outputs: `dump.cs`, `script.json`, `il2cpp.h`.

## Step 2 — Update `GameBuildProfile.V1009`

Open `TBH_Trainer/GameBuildProfile.cs` — `V1009` is already populated; update only if a newer hotfix shifts RVAs.

### ACTk bypass (6 RVAs)

In `dump.cs`, search ACTk types:

| Trainer field | Dump search |
|---------------|-------------|
| `ObscuredCheatingDetector_Check` | `ObscuredCheatingDetector` → main check method RVA |
| `ObscuredCheatingDetector_Compare` | same class, compare method |
| `ObscuredCheatingDetector_CompareExt` | same class, extended compare |
| `InjectionDetector_Check` | `InjectionDetector` check method |
| `SpeedHackDetector_Update` | `SpeedHackDetector` → `Update()` |
| `SpeedHackDetector_OnAppPause` | `SpeedHackDetector` → `OnApplicationPause` |

Copy the **RVA:** hex from each line (without `0x` prefix in C# use `0x...`).

**Prologue signatures:** After updating RVAs, attach the trainer and run ACTk bypass.
If you get **Signature mismatch**, open the game in x64dbg at `GA_base + RVA` and
update the 6-byte `Signature` arrays in `ActkTargets` for that build only if the
compiler changed the function prologue (rare on hotfix).

### Stash spawn (1.00.09 API)

Namespace **`ue`** (was `ud` on 1.00.08). In `Assembly-CSharp.dll.hpp` or `dump.cs`:

| Trainer field | 1.00.09 method | RVA |
|---------------|----------------|-----|
| `StashInsertRva` | `ue.Stash.jbn(ulong, StashCache)` | `0x8D62F0` |
| `StashSlotRva` | `ue.Stash.jbw(int)` | `0x8D75A0` |
| `StashInitRva` | `ue.Stash.jbi()` | `0x8D5890` |
| `StashSaveDataCtor` | `StashSaveData.ctor(int)` | `0x94DEF0` |
| `ItemAddRva` | `ue.ti.blk(int, ulong, bool)` | `0x8BF830` |

Max stash slots: static `beds` on `Stash` (still **49**).

Shared-memory keys are still named `rvaStashJbg` / `jbp` / `jbb` in code — they carry the **1.00.09** RVAs above.

The hook reads these from shared memory — **rebuild TBHHook.dll once** after the
shared struct change, then only edit `GameBuildProfile.cs` for future patches.

### Hero HP / ATK

HPP gives **field offsets** (not static bases). `V1009` still has `HpStatic` / `AtkStatic` = 0 until CE scan.

Suggested chain (from `Assembly-CSharp.dll.hpp`):

1. `TaskbarHero.StageManager` singleton → `HeroList` @ **0x30** (`Hero[][]`)
2. Pick active `Hero` → `Unit` base
3. `Unit.UnitHealthController` @ **0xB0** → `pf*`
4. HP float: `pf.bchs` @ **0x40** (trainer final hop `+0x40`)
5. ATK-related float: `pf.bchr` @ **0x3C** (trainer final hop `+0x3C`)

Re-scan `HpStatic` + `HpOffsets[]` (4 hops) and `AtkStatic` + `AtkOffsets[]` (6 hops) in CE, then paste into `V1009`.

### Extra Features (static disk patches)

For each toggle in `BuildStaticFeatures1009()`:

1. Find the same logic in the new `dump.cs` (gold reward, cube XP, DLC, pets).
2. Note the new **RVA** from the dump.
3. In HxD, go to **file offset** = RVA (for typical il2cpp GA, RVA ≈ file offset for `.text`).
4. Copy the first 7–16 bytes at that offset into `Original[]`.
5. Set `Offset` to the file offset; keep `Patched[]` unchanged.

If unsure, use **MISMATCH** in the UI as feedback — wrong `Original` bytes refuse to apply.

## Step 3 — High EXP (AOB)

Uses pattern `F3 0F 10 8E A8 03 00 00` (movss xmm1, [rsi+0x3A8]).

Often **still works** on minor hotfixes. If Enable EXP fails:

1. CE → Memory scan → Array of byte → that pattern in `GameAssembly.dll`.
2. If found, no code change needed.
3. If not found, search for similar movss near monster EXP logic and update
   `HighExpPatcher.cs` `AobPattern` and cave write offset `0x3A8`.

## Step 4 — Speedhack

Uses `UnityEngine.Time.set_timeScale` via il2cpp **by name** — usually **no RVA update**.

## Step 5 — Build & test checklist

```bat
cd TBHHook && build.bat
cd ..\TBH_Trainer && build.bat
```

| Feature | Test |
|---------|------|
| Connect | Log shows `Detected game build: 1.00.09` |
| ACTk | 6/6 OK |
| Speed | timeScale changes |
| HP/ATK | in active run/battle |
| High EXP | enable, kill mob |
| Stash spawn | open stash, Spawn ItemKey |
| Extra Features | each toggle, game closed |

## Auto-detection

On **Connect**, the trainer scans ACTk prologue bytes at each profile's RVAs.
Newest profile (`V1009`) is tried first, then `V1008`.

If neither matches:

```text
WARNING: Could not auto-detect game build (1.00.08 / 1.00.09).
```

Fill `V1009` or verify you are on the expected patch.

## Quick diff vs 1.00.08

| Component | 1.00.08 anchor | 1.00.09 action |
|-----------|----------------|----------------|
| `GameBuildProfile.V1009` | filled (ACTk + stash) | HP/ATK statics + disk patches |
| `GameOffsets.cs` | legacy | optional; use profile |
| `dllmain.cpp` kRva* | 1.00.08 fallback | trainer pushes RVAs at runtime |
| `StaticPatcher` | per-profile list | add 1.00.09 Original bytes |
| `HighExpPatcher` | AOB | rescan if broken |
| UI strings | 1.00.08 | update when verified |
