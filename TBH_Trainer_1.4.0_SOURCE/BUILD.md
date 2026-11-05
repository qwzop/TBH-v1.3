# Build TBH Trainer 1.00.09

## Quick build

Extract the source ZIP, then double-click the root `build.bat`.

Successful builds are written to:

- `TrainerBuild\TBH Trainer.exe`
- `TrainerBuild\TBHHook.dll`

## Requirements

- Windows x64.
- .NET 10 SDK.
- One C++ compiler:
  - MinGW-w64 `g++` available on `PATH`, or
  - Visual Studio with the x64 C++ build tools.

The main build script checks both build stages and stops with an error message when a required tool is unavailable.

## Manual build

Build the native hook first:

```bat
cd TBHHook
build.bat
```

Then build the trainer:

```bat
cd ..\TBH_Trainer
build.bat
```

The trainer publish is self-contained, but the .NET SDK is required to compile the source.
