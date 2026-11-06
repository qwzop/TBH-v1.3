# Preserved Taskbar Hero findings

## Incident and evidence

The user reported an account block after trainer experimentation. Their screenshot
reads: **Error 403 — Suspicious activity was detected, so your account has been
blocked.** No expiry was displayed. We do not know which feature or event caused
it. Successful local writes, anti-cheat interception, or previous gameplay tests
never established that account detection was prevented. Restoring DLL bytes does
not undo an account restriction or necessarily undo saved progression.

Further work on the account was stopped. The universal framework does not import
ACTk bypasses, DLC/skin unlocks, injection, or TBH offsets as enabled features.

## Verified build input

Steam build **26211490**, GameAssembly.dll SHA256:
`e9e3f21ab135f5cc9a344789434e70f32b84c7d2a0ac0789098c19610de877a5`.
The IL2CPP metadata was version 31. For the investigated code section, disk offsets
were RVAs minus 0x1A00; this is section-dependent, not a universal conversion.

## Outcome ledger

| Work | Evidence and limit |
| --- | --- |
| EXP override and game speed | User confirmed changes worked. Account safety was not established. |
| Hero HP protection and monster damage | User confirmed worked. Negative finite health deltas were intercepted; normal death handling was preserved. |
| Chest probability | User saw a chest when the eligible counter advanced at 100%. This was a final probability roll after availability checks, not a guaranteed drop per kill. |
| Static Extra Features and skins | User confirmed worked. Local access checks and saved progression differed; DLL restoration did not revert all saved effects. |
| DLC character persistence | Character availability was overwritten from ownership checks on load. Pets and characters behaved differently. |
| Gear spawning | Catalog scan showed 6,696 gear rows, all marked Steam items; no eligible local gear rows under that test's rules. |
| Material refill | Saved counts and UID-based inventory records were found. Cube crafting sends ingredients through a backend exchange; no usable unlimited-material implementation verified. |
| Item duplication | Original add function rejected an already registered UID in isolated native tests. Fresh IDs reached a local construction branch with simulated dependencies; usability, attributes, refresh survival and persistence were never verified. |
| Extra monsters | Original normal-wave loop and a bounded multiplier were tested in isolation. User was sent an experimental runtime update, then reported an account restriction. Actual extra spawning/rewards were not confirmed. |

## Engineering lessons

* Reject unknown builds instead of falling back to older offsets.
* Verify whole-module identity and live instruction context before mutations.
* A matching signature verifies a location, not game semantics or account safety.
* Distinguish requested counts, successful writes, spawned entities, granted items,
  and persistent backend inventory. Label diagnostic counters accordingly.
* Preserve original bytes; track patch ownership before attempting a write;
  read back; recover partial writes and surface failed recovery.
* Avoid freeing a trampoline that an in-flight game thread may still execute.
* Separate disk restoration from save restoration and account state.
* Test the actual emitted native code when possible, but label simulated
  dependencies and the absence of live gameplay validation.
* Old MinGW.org GCC 6.3 (`mingw32`) was not the needed x64 toolchain. The user's
  installed MSYS2 was `E:\MSYS2`. Never assume a C-drive installation.
* Workspace download UI failed for this user. Large pasted base64 updates also
  caused copy errors. A standalone app and signed package installation reduce
  that delivery burden, but initial binary delivery must still be verified.

## Preserved workspace sources

These remain separate and unchanged by the new framework:

* `/workspace/trainer-skins-26211490` — last user-confirmed source baseline.
* `/workspace/trainer-waves-26211490` — experimental normal-wave update.
* `/workspace/game-inspection/material-validation/FINDINGS.md` — material trace.
* `/workspace/game-inspection/item-generation-validation/FINDINGS.md` — native
  generation/monster experiments with explicit test limits.
* `/workspace/game-inspection/wave-validation` — emitted-hook tests.

The uploaded game DLL and metadata are not redistributed in this repository.
A source-only archive of the confirmed baseline and experimental update is kept
in workspace artifacts. Source provenance/licensing must be reviewed before any
legacy third-party code is redistributed; the new framework is written separately.
