# TBH Trainer 1.00.09 - Development Status

## Current build

- Source of truth: this `merge` directory.
- Build entry point: run `build.bat` from this directory.
- Build output: `TrainerBuild\TBH Trainer.exe` and `TrainerBuild\TBHHook.dll`.
- Required build tools:
  - .NET 10 SDK.
  - MinGW-w64 `g++` on `PATH`, or Visual Studio with the x64 C++ build tools.

## Completed for 1.00.09

- Added automatic build detection for game versions 1.00.08 and 1.00.09.
- Preserved the working Extra Features implementation.
- Added and validated the ACTk detector patches for 1.00.09.
- Added the injected bridge and runtime diagnostics.
- Added the `Hero Stats` tab and active-hero scanner.
- Added persistent `HP Lock`, which writes current and maximum HP and survives phase changes.
- Added persistent `Attack Speed Lock`. The displayed stat may not always refresh, but gameplay speed changes.
- Removed `Attack Damage Lock` from the trainer UI because it changes the reported runtime value but does not currently change gameplay damage.
- Improved the Stash Item Spawn layout to prevent controls and status text from overlapping.
- Locked the fixed WinForms layout and delegated 4K scaling to Windows so the complete UI scales as one unit.

## Known behavior

- Hero objects and health-controller addresses can be recreated when changing phases. Persistent locks must resolve the active heroes again before writing.
- `HP Lock` is confirmed to affect both the UI and gameplay.
- `Attack Speed Lock` is confirmed to affect gameplay even when the UI value does not refresh.
- Writing the current `AttackDamage` obscured field changes the getter and calculated diagnostic DPS, but actual damage dealt remains unchanged.

## Attack damage investigation

Attack damage work is paused. Do not expose it as a user-facing option until gameplay damage is confirmed.

The next investigation should trace the value consumed when a hit is created or applied, rather than relying only on the unit stat getter. Existing diagnostics added probes for:

- `DamageInfo.OriginDamage`
- `Unit.grf`
- cached damage payloads returned by `uh.jje`
- cached damage payloads returned by `uh.jjd`

Compare these payloads immediately before and after a real attack. Determine whether damage is cached when an attack, projectile, or skill object is created.

## Next steps

1. Validate the new fixed-DPI layout on 100%, 150%, 200%, and 4K displays.
2. Test Stash Item Spawn with long status messages and every grade label.
3. Verify the packaged source builds on a clean Windows machine using only the documented prerequisites.
4. Resume attack-damage tracing from the actual hit/damage pipeline.
5. Remove obsolete attack-damage command handling from the hook only after confirming it is no longer useful for diagnostics.

## Packaging notes

The distributable source ZIP intentionally excludes generated folders such as `bin`, `obj`, `publish`, `MergedBuild`, and `TrainerBuild`. After extraction, run the root `build.bat`; it recreates the required output folders.
