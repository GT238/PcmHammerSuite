# Credits

PcmSuite builds on the work of others. The app's **Credits** tab shows this list followed by PCM Hammer's own
credits page, which is read from the PcmHammer submodule (`Apps/UI/Shared/credits.html`) at build time, so
it stays current when the submodule is updated. The page text for PcmSuite's own part is in
`src/PcmSuite.Desktop/Credits/PcmSuite-credits.html`; keep the two in step.

- **PCMHammer** (https://github.com/PcmHammer/PcmHammer, GPLv3 except the PCM-side kernel): flashing,
  logging, protocol code, checksum validation, and the Avalonia user interface. Used unmodified as a
  git submodule in `extern/PcmHammer`. The PCM kernel files are not part of this repository; they come
  from PCMHammer's official releases, with its licence.
- **Universal Patcher** (https://universalpatcher.net/, GPLv3, by Joukoy and contributors): the XDF
  reader in `src/PcmSuite.Tuning.Core/Parsers/XdfParser.cs` is written to cope with XDF files that
  Universal Patcher exports, and the `ref:'name'` table-reference idea in equations follows its
  convention. No Universal Patcher source code is included.
- **TunerPro** (https://www.tunerpro.net/): the XDF definition format that the Tune tab reads.
- **Avalonia**, **FluentAvalonia**, **CommunityToolkit.Mvvm**, **DynamicExpresso**: UI and support libraries.
