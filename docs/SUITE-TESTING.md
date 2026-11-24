# PcmSuite (prototype): notes for PCM Hammer's maintainers

This is the full suite: PCM Hammer's own Avalonia Flash and Logger screens, used unmodified from your repository (the exact commit is on the
**About** tab), plus a new Tuner, in one window. It is an independent prototype, not a PCM Hammer release.

Unlike the standalone Tuner download, **this build can talk to a PCM and can flash it.** It is meant for you to look at and try, not for general testing.

## Running it

1. Unzip anywhere and run `PcmSuite.exe`. Nothing needs installing.
2. **Kernel files are not included.** Copy the `Kernel-*.bin` files from an official PCM Hammer release into the folder that holds `PcmSuite.exe`.
   Without them the Flash tab cannot read or write, and the status bar at the bottom says so. The Tuner and Logger do not need them.
3. Make a project in the Tuner tab (menu, then Projects...). Reads, edits and logs for one vehicle then live in one folder under `Documents\PcmSuite Projects`.

## How it is put together

- `extern/PcmHammer` is your repository as a git submodule, pinned to one `develop` commit and unmodified. `PCMHammerAvalonia` and
  `PCMLoggerAvalonia` are referenced as projects. The Logger tab is your `MainView` as it is.
- The Flash tab is our own copy of the desktop layout that lives inside your `MainWindow.axaml` (a window cannot be hosted in a tab). It is bound to your
  unchanged `MainWindowViewModel`. Delete it if that layout ever moves into a control.
- **The adapter is not shared.** Each of your view models opens and owns its own device, so the shell lets go of the adapter when you leave a tab:
  leaving Flash disposes its device, and leaving the Logger disconnects it, except between Logger, Tuner and Help, where it stays connected for the Tuner's live
  trace. A real shared connection needs both view models to accept a device from outside; see `docs/UPSTREAM-PROPOSALS.md` in the repository.

## What has not been tried

Please be careful with anything on this list. It has not been tested on hardware:

- The hand-over of the adapter between the Flash and Logger tabs.
- The Select Device dialog opened from the Flash tab in this hosting.
- The Logger's live trace feeding the Tuner after the layout change.
- A real flash write started from this build. Read, write and verify are your code, but the hosting around them is new.

The Tuner's own checksum fixing is tested against your validator on real P01 files; P59 uses the same code with less testing. Anything you flash from a file
the Tuner saved is your own decision and risk.

## Reporting a problem

Open the **Logs** tab and press **Copy build details for a bug report**, then paste the result into your message. If the program closed by itself, the same tab lists a
crash log (it can include file paths, never the contents of your BIN files).

## Licence

GNU General Public License v3, the same as the open-source part of PCM Hammer (see `LICENSE.txt`). Kernel files are not part of this download and stay under
PCM Hammer's own terms.
