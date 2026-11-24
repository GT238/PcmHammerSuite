# Testing the PCM Tuner (prototype)

Thank you for trying this. It is a prototype built by one person, so please expect rough edges.

## What this is

The standalone Tuner opens a BIN with its XDF, lets you edit tables, compare BINs, and **Save As** a new file with the checksums corrected.
It has no adapter support and no kernels, so it **cannot read or write a PCM**. Nothing it does can touch a vehicle.

## The one safety rule

A file this program saves is a suggestion. If you flash one with PCM Hammer or any other tool, that is your decision and your risk.
Keep your original read, use a stable power supply, and only flash a file that Save As reported as valid. The checksum fixing has been
tested on real P01 files; P59 uses the same code but has had much less testing.

## Starting

1. Unzip anywhere and run `PcmSuiteTuner.exe`.
2. Menu (the button at the top left), then **Projects...** to make a project for a vehicle, or **Add BIN...** to open one.
3. Put your XDFs in `Documents\PcmSuite Projects\XDFs`. The Tuner picks the one whose file name carries the BIN's operating system number.
4. Optional: put other BINs (factory files, a friend's tune) in `Documents\PcmSuite Projects\Reference BINs`.

Your files are never uploaded or sent anywhere: the program has no code that contacts a server. A link in Help or Credits opens your web browser only if you click it.

## What to try (and what we most want to hear about)

- Open a BIN that has an XDF, and one that does not (it should still open for the hex view).
- Edit a table, undo it, save with **Save As**, and check that the result is reported valid.
- **Compare with BIN...**, the **compare workbench**, and copying a table from one BIN into another (with undo).
- With a Logger CSV log from a real drive: the **log map** and the **VE wizard**. These have never been tried on a real vehicle log, so
  this is the most useful test. Do the suggested corrections look sensible for your engine?
- Anything that looks wrong, confusing, slow or ugly.

## Reporting a problem

Open the **Logs** tab and press **Copy build details for a bug report**, then paste it into your message and say what you were doing.
If the program closed by itself, the same tab lists a crash log: pick the newest, press **Copy log**, and paste it too. A crash log can include
file paths (so your user name), but never the contents of your BIN files.

## Where things are kept

- Projects, XDFs and Reference BINs: `Documents\PcmSuite Projects`
- Settings and crash logs: `%APPDATA%\PcmSuite`

To remove the program, delete its folder. Your projects stay where they are.
