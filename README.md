# PcmSuite (prototype)

One window for the three jobs of tuning a GM PCM: **Flash**, **Logger** and **Tuner**, tied together by a per-vehicle project folder.

This is an independent prototype built on [PCM Hammer](https://github.com/PcmHammer/PcmHammer). It is not an official PCM Hammer release.

- **Flash** and **Logger** are PCM Hammer's own Avalonia screens, used unmodified through the git submodule in `extern/PcmHammer`.
- **Tuner** is new: open a BIN with its XDF, edit tables, compare, correct the checksums, save, and hand the result to the Flash tab.

## Get it running

You need the **.NET 10 SDK**.

```
git clone --recursive https://github.com/GT238/PcmHammerSuite.git
cd PcmHammerSuite
dotnet build PcmSuite.sln -c Release
dotnet run --project src/PcmSuite.Desktop -c Release
```

Use `--recursive`. A plain `git clone` leaves `extern/PcmHammer` empty and the build fails. If you already cloned: `git submodule update --init`.

**Kernel files are not included.** PCM Hammer only allows its PCM-side kernels inside its official releases. To read or write a PCM, copy the `Kernel-*.bin` files from an official PCM Hammer release into the folder that contains `PcmSuite.exe`. The Tuner works without them.

**BIN and XDF files are not included.** Bring your own.

Developed and tested on Windows. It is Avalonia, so Linux and macOS should be possible, but nothing here has been tried on them.

## What the Tuner does

- Opens a BIN with its XDF (found by operating system ID, or chosen by hand). A BIN with no XDF still opens for the hex view, checksums and segment swap.
- Table editor with spreadsheet-style selection, fill, scale, interpolate, smooth, undo and redo, pins, tags and notes.
- Compare against another BIN: marks the changed tables and cells. The **compare workbench** lists every table that differs and copies chosen tables into the open BIN, with undo.
- **Hex view** with go-to, search, the table that covers a byte, and a list of every difference from another BIN.
- Patches from the XDF, and segment swap between BINs with an operating-system mismatch warning.
- **Save As** corrects the P01/P59 checksums and checks the result with PCM Hammer's own validator. A valid file can be sent to the Flash tab.
- Logger integration: ready-made logging profiles, a live trace of the cell the engine is in, a **log map** of where a drive went on a table, and a **VE wizard** that suggests corrections from a log (leaving out cold-engine, fuel-cut, moving-throttle and unsteady samples).
- Trouble codes in the BIN with descriptions and search.
- A change log of every edit, and a built-in Help tab.

## Status

A prototype, built by one person. Please read this before you flash anything.

- The checksum fixing is tested against PCM Hammer's own validator on real P01 files (P59 uses the same code but has had less testing), but **the Tuner's edits are suggestions you are responsible for**. Flash only to a PCM you can recover.
- Not yet tried on a real vehicle log: the log map and the VE wizard.
- Not yet tried in the live app: a BIN opened without its XDF, and handing the adapter between tabs.
- Only one tab can use the adapter at a time. Switching tabs releases and reconnects it. A shared connection would need small changes in PCM Hammer; see [docs/UPSTREAM-PROPOSALS.md](docs/UPSTREAM-PROPOSALS.md).

## Credits and licence

PcmSuite is licensed under the **GNU General Public License, version 3**, the same licence as the open-source part of PCM Hammer (see [LICENSE](LICENSE)). The PCM-side kernel files are not part of this repository and are not covered by it; they stay under PCM Hammer's own terms.

See [CREDITS.md](CREDITS.md) for the people and projects this builds on.
