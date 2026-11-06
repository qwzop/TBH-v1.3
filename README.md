# Trainer Studio — prototype 0.1.0

A reusable Windows trainer foundation developed from lessons learned while
investigating Taskbar Hero. Each game/build needs a separately verified profile;
this is not an automatic trainer for arbitrary games.

## Current application

Open **Trainer Studio.exe**, then choose **Open Dashboard**. Your browser shows
app state, the selected profile, verification status, and recent diagnostics,
refreshed every second. The dashboard listens only on 127.0.0.1, with a random
session path. Closing the app stops it. It has no remote control endpoints.

* **Verify Game Profile**: load a JSON profile and compare a chosen module's hash.
  This reads the file; it does not patch it.
* **Run Demo**: changes simulated health from 10 to 100 and restores 10.
* **Export Diagnostics**: save status/logs to a file you select. Review before sharing.
* **Install Update**: select a publisher-signed ZIP and a destination folder.
  The signature, version, file size, and hash are checked before installing a
  separate version folder. Launch that version manually; the old app remains.

Live game attachment, game features, a hosted dashboard, automatic update
checking, and chat access to the local app are **not implemented**. Localhost
cannot be viewed from a remote chat. A future hosted relay needs authentication,
explicit session sharing, and a selected hosting service. Do not forward the
local dashboard port publicly.

The EXE is self-contained Windows x64 and does not require a separate .NET
runtime installation. It is not Windows Authenticode-signed; update-package RSA
signatures are a separate mechanism. Windows SmartScreen behavior is untested.

## Build and check

With the .NET 10 SDK, double-click `build.cmd` from any installation drive.
The EXE appears in `artifacts/windows`. No administrator access is requested.

```text
dotnet run --project tests/Trainer.Checks -c Release
dotnet run --project src/Trainer.Cli -- demo
dotnet run --project src/Trainer.Cli -- verify profiles/example.json path/to/module
```

`profiles/example.json` is a synthetic example, not a supported game profile.
Core memory offsets are relative to the adapter's chosen region. A future Windows
adapter must identify process/module/build, translate RVAs, handle process exit,
check memory protection, and flush the instruction cache. File-hash verification
alone does not establish loaded-process identity or that gameplay is safe.

See [preserved findings](docs/TBH-LESSONS.md), [architecture](docs/ARCHITECTURE.md),
and [release workflow](docs/RELEASES.md).
