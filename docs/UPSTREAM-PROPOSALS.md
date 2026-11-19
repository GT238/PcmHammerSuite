# Proposed small changes to PcmHammer (not applied, for discussion with Antus)

PcmSuite keeps `extern/PcmHammer` unmodified. These two changes would let a host application that
embeds the Flash and Logger screens (as PcmSuite does) work with them more smoothly.

## 1. Let the host choose the BIN for Write / Compare / Validate  (draft ready)

File: `docs/preselect-bin.proposed.patch` (against `Apps/UI/Avalonia/PCMHammerAvalonia/ViewModels/MainWindowViewModel.cs`).
It builds (checked on a copy of the tree) and `git apply --check` accepts it on the submodule.

- Adds `public string? PreselectedBinPath { get; set; }` and a private `PickBinFileAsync()`.
- The three places that call `_fileDialogService.OpenBinFileDialog()` call `PickBinFileAsync()` instead.
- If `PreselectedBinPath` is set and the file exists, it is returned once and cleared; otherwise the
  normal file picker opens. With nothing setting the property, behaviour is identical to today.
- All the existing checks (OS ID warning, validation, the 10-second power warning dialog) still run.

Use in PcmSuite: after Save As produces a file that passes `FileValidator`, "Send to Flash" would set
`PreselectedBinPath` and switch to the Flash tab, so Write starts with the file already chosen. Today it
puts the path on the clipboard instead.

## 2. A shared adapter connection  (design only, nothing drafted or tested)

Today each screen opens its own adapter: the Flash view-model builds `Device` + `Protocol` +
`ToolPresentNotifier` + `Vehicle` in `InitializeDeviceAndVehicle` (private), and the Logger view-model does
the same in its Connect (fields `_device` / `_vehicle`, private). PcmSuite works around this by releasing
the adapter of the tab being left, so switching tabs reconnects each time.

Smallest shape that fixes it:

- A small class, for example `ConnectionSession { Device, Protocol, ToolPresentNotifier, Vehicle }`, created
  once by the host.
- `AdoptSession(ConnectionSession)` / `ReleaseSession()` on both view-models: they use the session instead
  of creating their own, and never dispose it themselves.
- One user at a time: the host refuses to hand the session over while a flash operation or logging is
  running (PcmSuite already blocks tab switches during a flash operation).

The Uno app's `ConnectionService` / `ConnectionLease` is the existing reference for the same idea.
Open questions before drafting: where the class should live (PcmLibrary or the Avalonia projects), how
4x speed and the tool-present notifier should be handed over, and whether Android's Bluetooth setup fits.

Neither change is needed for PcmSuite to work today; both make it smoother.
