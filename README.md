# PcmSuite (prototype)

Tools for tuning a GM PCM, in two forms built from the same code:

- **PCM Tuner** (`PcmSuiteTuner`): the Tuner on its own. It needs no adapter and no kernels, and it cannot talk to a PCM, so it is the safe thing to try first.
- **PcmSuite**: one window with **Flash**, **Logger** and **Tuner** tabs, tied together by a per-vehicle project folder.

This is an independent prototype built on [PCM Hammer](https://github.com/PcmHammer/PcmHammer). It is not an official PCM Hammer release.

- **Flash** and **Logger** are PCM Hammer's own Avalonia screens, used unmodified through the git submodule in `extern/PcmHammer`.
- **Tuner** is new: open a BIN with its XDF, edit tables, compare, correct the checksums and save.

## Try the Tuner

Windows testers: download the latest zip from the [Releases](../../releases) page, unzip it anywhere and run `PcmSuiteTuner.exe`. Nothing needs installing.

To build it yourself you need the **.NET 10 SDK**:

```
git clone --recursive https://github.com/GT238/PcmHammerSuite.git
cd PcmHammerSuite
dotnet run --project src/PcmSuite.Tuner.Desktop -c Release
```

Use `--recursive`. A plain `git clone` leaves `extern/PcmHammer` empty and the build fails. If you already cloned: `git submodule update --init`.

## Build the full suite

```
dotnet build PcmSuite.sln -c Release
dotnet run --project src/PcmSuite.Desktop -c Release
```

PCM Hammer's own Avalonia projects also target Android, so building the suite needs the Android workload (`dotnet workload install android`). On Linux, build the two projects directly instead of the whole solution (a solution build also asks for the Android SDK):

```
dotnet build src/PcmSuite.Desktop -c Release
dotnet run --project src/PcmSuite.Desktop -c Release
```

The standalone Tuner does not need the Android workload.

**Kernel files are not included.** PCM Hammer only allows its PCM-side kernels inside its official releases. To read or write a PCM with the suite, copy the `Kernel-*.bin` files from an official PCM Hammer release into the folder that contains `PcmSuite.exe`. The Tuner works without them.

**BIN and XDF files are not included.** Bring your own.

Developed and tested on Windows. On Linux (Pop!_OS 24.04) it builds, the tests pass and the window starts; see [docs/LINUX-BUILD-RESULTS.md](docs/LINUX-BUILD-RESULTS.md). The Tuner window and its tabs open there, but the built-in Help viewer stayed blank on a Wayland desktop in a virtual machine (Pop!_OS 24.04); the **Open in your browser** button at the top of Help and Credits shows the same pages in your web browser. Other Linux screens and macOS are untried.

## What the Tuner does

- Opens a BIN with its XDF (found by operating system ID, or chosen by hand). A BIN with no XDF still opens for the hex view, checksums and segment swap.
- Table editor with spreadsheet-style selection, fill, scale, interpolate, smooth, undo and redo, pins, tags and notes. A right-click menu on the table list pins, tags and copies a table's name or address.
- Compare against another BIN: marks the changed tables and cells. The **compare workbench** lists every table that differs and copies chosen tables into the open BIN, with undo.
- A shared **Reference BINs** folder: compare dialogs start there, and an opened BIN is matched to a file in it by its GM part numbers, with a one-click **Compare with**.
- **Hex view** with go-to, search, the table that covers a byte, and a list of every difference from another BIN.
- Patches from the XDF, and segment swap between BINs with an operating-system mismatch warning.
- **Save As** corrects the P01/P59 checksums and checks the result with PCM Hammer's own validator. In the suite, a valid file can be sent to the Flash tab.
- A **log map** of where a logged drive went on a table, and a **VE wizard** that suggests corrections from a log (leaving out cold-engine, fuel-cut, moving-throttle and unsteady samples). In the suite there are also ready-made logging profiles and a live trace of the cell the engine is in.
- Trouble codes in the BIN with descriptions and search.
- A change log of every edit, and a built-in Help tab.

## Where things are kept

- Projects, the XDF library and the Reference BINs folder: `Documents\PcmSuite Projects`.
- Settings, and crash logs: `%APPDATA%\PcmSuite` (on Linux, the matching folder under `~/.config`).

## Reporting a problem

Open the **Logs** tab. Press **Copy build details for a bug report** and paste the result into your message: it names the version and the exact PcmSuite and PCM Hammer builds. If the program closed by itself, it left a crash log on the same tab. Read it, then copy it into your message. A crash log can include file paths (and so your user name), never the contents of your BIN files.

## Status

A prototype, built by one person. Please read this before you flash anything.

- The checksum fixing is tested against PCM Hammer's own validator on real P01 files (P59 uses the same code but has had less testing), but **the Tuner's edits are suggestions you are responsible for**. Flash only to a PCM you can recover.
- Not yet tried on a real vehicle log: the log map and the VE wizard.
- Not yet tried in the live app: a BIN opened without its XDF, and handing the adapter between tabs in the suite.
- Only one tab can use the adapter at a time. Switching tabs releases and reconnects it. A shared connection would need small changes in PCM Hammer; see [docs/UPSTREAM-PROPOSALS.md](docs/UPSTREAM-PROPOSALS.md).
- Not done yet: equation overrides for a bad XDF scaling, and a settings screen.

## Credits and licence

PcmSuite is licensed under the **GNU General Public License, version 3**, the same licence as the open-source part of PCM Hammer (see [LICENSE](LICENSE)). The PCM-side kernel files are not part of this repository and are not covered by it; they stay under PCM Hammer's own terms.

See [CREDITS.md](CREDITS.md) for the people and projects this builds on.
