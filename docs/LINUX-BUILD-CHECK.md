# Linux build check

Goal: find out whether PcmSuite builds, passes its tests and starts on Linux. Nothing here has been tried on Linux yet, so please write down every failure exactly as it appears.

## 1. Prerequisites

- A 64-bit Linux with a desktop (X11 or Wayland) if you want to run the window. Building and testing need no display.
- **.NET 10 SDK**. Check with `dotnet --version` (should start with 10).
  - Ubuntu: `sudo apt install dotnet-sdk-10.0` (if the package is missing, use Microsoft's install script: `curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0`, then add `~/.dotnet` to `PATH`).
- `git`.
- Avalonia on Linux usually wants these at run time (not for building):
  `sudo apt install libfontconfig1 libx11-6 libice6 libsm6 libgl1`
  Fonts: `sudo apt install fonts-dejavu-core` if text is missing or boxes appear.

## 2. Get the source

```
git clone --recursive https://github.com/GT238/PcmHammerSuite.git
cd PcmHammerSuite
```

If you cloned without `--recursive`: `git submodule update --init`.
Check `extern/PcmHammer` is not empty before going on.

## 3. Build

```
dotnet restore PcmSuite.sln
dotnet build PcmSuite.sln -c Release 2>&1 | tee build.log
```

Record: did it end with `0 Error(s)`? Copy any errors (first 20 lines) into your report.

Things most likely to break:
- Case-sensitive file names: a path in a `.csproj` or `.axaml` that differs in letter case from the file on disk (the "Could not find file" or "file not found" errors).
- Backslashes in project paths (`..\extern\...`). MSBuild usually fixes these, but report any that it does not.
- Linked assets from the submodule (tab icons, `credits.html`, `help.html`): look for warnings about missing items.
- Windows-only APIs (registry, `System.Management`, `System.IO.Ports` differences): these show up as CA1416 warnings or errors.

## 4. Test

```
dotnet test PcmSuite.sln -c Release --no-build 2>&1 | tee test.log
```

On Windows: 176 pass in total, with 5 skipped when no real BIN/XDF is present (a clean clone shows 156 pass, 5 skipped). On Linux the Windows-only migration test returns early and counts as a pass. Record the totals and the name of any failing test with its message.

Likely suspects for failures: path separators, case-sensitive paths, `Documents` folder location, line endings.

## 5. Run (needs a desktop session)

```
dotnet run --project src/PcmSuite.Desktop -c Release
```

Check and note for each:
1. The window opens. If it exits at once, copy the error text.
2. Tabs show: Flash, Logger, Tuner, Help. Icons and text visible?
3. Tuner: create a project. Where does it put the folder? (Expected: `~/Documents/PcmSuite Projects`, which may not exist on some Linux setups.)
4. Tuner: open a BIN (any file works as a BIN-only open) and look at the hex view.
5. Help tab renders.
6. Flash and Logger tabs: do they load without an adapter attached? You do not need to connect.
7. Serial ports: open the device list in Flash. Does it show `/dev/ttyUSB*` or `/dev/rfcomm*` ports if you have one? Skip if you have no adapter.

## 6. Report back

Please paste:
- `uname -a` and `dotnet --info` (the first 15 lines)
- `git log -1 --oneline`
- the tail of `build.log` and `test.log`
- what happened at each step in section 5

Do not fix anything on the Linux machine yet. A list of exactly what fails is what we need first.
