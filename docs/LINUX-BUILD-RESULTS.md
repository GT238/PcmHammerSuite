# Linux build check: results

Run on 2026-11-23 following [LINUX-BUILD-CHECK.md](LINUX-BUILD-CHECK.md). No source files were changed.

## Environment

- Linux 6.18.7, Pop!_OS 24.04, x86_64, Wayland session (`DISPLAY=:1`)
- .NET SDK 10.0.404, installed to `~/.dotnet` with Microsoft's install script (the machine only had .NET 8)
- Avalonia runtime libraries and `fonts-dejavu-core` were already installed
- Commit: `1d8e802 Add Linux build check instructions`; submodule `extern/PcmHammer` at `d71fafd4`

## Build

`dotnet build PcmSuite.sln -c Release` **fails** with 11 errors.

1. **Android target (2 distinct errors, repeated per project).** `PCMHammerAvalonia` and `PCMLoggerAvalonia` in the submodule also target `net10.0-android`: `XA5300: The Android SDK directory could not be found`. A full solution build is impossible without the Android SDK.
2. **Duplicate attributes in `PcmLibrary` (9 x `CS0579`).** Duplicate `TargetFrameworkAttribute` and `Assembly*Attribute`.
   - `BaseIntermediateOutputPath` evaluates to `obj\` (with a backslash) on Linux, so the default exclusion of `obj/**` does not match.
   - A stale `obj/Debug/netstandard2.0/*.cs` was compiled alongside the Release files.
   - A folder literally named `obj\Debug` was also created in `extern/PcmHammer/Apps/PcmLibrary` (it holds only `SlaveLibrary.archive`). What creates it is not yet known.
   - Deleting the generated `obj/Debug` and `obj\Debug` folders made the error go away.
3. **Workaround used:** `dotnet build src/PcmSuite.Desktop -c Release` and `dotnet build tests/PcmSuite.Tests -c Release` each finish with 0 errors.

Warnings:
- `CA1416`: `XdfParser.cs` lines 61 and 197 call `Type.GetTypeFromProgID`, which is Windows-only.
- `NU1903`: `Tmds.DBus` 0.20.0 has a known high-severity vulnerability.
- `NU1702`: net48 `J2534DotNet` and `PcmLibraryWindowsApi` are referenced from net10.0 projects.

## Tests

`dotnet test tests/PcmSuite.Tests -c Release --no-build`:

**Failed 0, Passed 163, Skipped 13, Total 176.**

The doc expected 5 skipped on a clean clone; 8 more are skipped here. They are the tests that need real BIN/XDF files.

## Run

`dotnet run --project src/PcmSuite.Desktop -c Release` stayed running for the 25 s tried, with no error output (stopped by `timeout`).

Not yet checked (needs someone looking at the screen):
- tabs and icons (Flash, Logger, Tuner, Help)
- Tuner project creation and its folder (`~/Documents/PcmSuite Projects` is not created until a project is made)
- opening a BIN in the hex view
- Help tab rendering
- Flash and Logger without an adapter
- serial port list

## Open questions

- Should the Android target be removed or conditioned for non-Android builds?
- Where does the `obj\` backslash path come from, and what recreates the `obj\Debug` folder?
