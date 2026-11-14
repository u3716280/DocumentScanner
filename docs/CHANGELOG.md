# 1.0.3 — Visible tone values and repeat-scan color isolation

- Live integer Brightness/Contrast values, thumb tooltips and a reset-to-zero button.
- Integer setting notifications keep profile loads, sliders, and displayed values synchronized.
- New scan options explicitly request neutral WIA/TWAIN driver brightness/contrast instead of leaving those properties unset in SDK post-processing mode. Native UI remains controlled by the scanner dialog.
- Store the requested tone levels as per-page metadata and render them once from the immutable original. Auto Crop affects geometry only.
- Use the same tone renderer for preview, cropped/adjusted thumbnails and export; zero is a true no-op.
- Selected-page preview footer records the actual page B/C values.
- Add 13 core tests (73 total) and 6 Windows regressions (15 total), including full retained-pixel comparisons across crop toggles and repeated PNG exports.

Physical scanner repetition and device-native exposure behavior still require user verification.

# 1.0.2 — Profile Save/Load fix

- Preserve Paper Source, DPI, Color Mode and Paper Size across capability-list refreshes.
- Ignore/restore transient null selections rather than converting them to enum-zero/default values.
- Read the profile device capabilities first, then restore the complete saved snapshot atomically.
- Retain the saved profile unchanged when a setting is unsupported and display the runtime adjustment.
- Synchronize the selected profile name and the editable name field.
- Preserve the existing JSON profile schema and processing flags.
- Add 12 unit regressions (60 total passing) and one compiled Windows WPF binding regression.

Windows UI/driver execution remains unverified in this Linux environment.

# 1.0.1 — Auto Crop fix

- Apply the Auto crop checkbox immediately to selected existing pages and retain its default for future scans.
- Replace toggle behavior with explicit Auto Crop and Reset crop actions; avoid no-op Undo entries.
- Adapt to paper brightness; filter small isolated noise, thin scanner-edge strips and frames; detect light paper on a dark platen.
- Keep blank images unchanged and retain 3 mm around detected content.
- Update thumbnails, preview and export using the same crop renderer. Detach cached preview/thumbnail pixels from full-resolution sources.
- Show output dimensions and crop/no-margins status below the preview.
- Add 17 detector/state regression cases (48 unit tests total) and 3 Windows export regression cases.

Windows launch, physical scanner and Windows codec execution remain unverified here.
