# Known limitations and release gates

- Hardware compatibility, WPF runtime UI, native scanner dialogs and clean Windows deployment require physical Windows testing. Neither USB nor network devices have been tested here.
- Capabilities may be incomplete or incorrect. Duplex/ADF availability follows reported capabilities. Unknown capabilities require native UI; the app does not claim automatic duplex support.
- Auto paper uses maximum reported scan area, with A4 fallback. Content cropping is reversible and adapts to background brightness; it can also detect a light document on a dark platen. It is a heuristic, not a guarantee for every paper/background.
- Brightness/contrast are SDK post-scan adjustments. Actual results and supported modes require validation per backend.
- During scan/export/discovery, document edits and other scanner operations are disabled. Cancellation remains cooperative; a hung vendor driver can take time to return. Do not force-kill a driver while it is acquiring a page.
- PDF cancellation does not interrupt the SDK writer immediately. The UI remains responsive, and cancelled output is not finalized.
- No automatic correction of arbitrary vendor duplex order; test numbered front/back sheets. Page ordering can be corrected manually.
- Import images supports the first frame of a multi-frame TIFF. Import is auxiliary; PDF import, OCR, barcode detection and searchable PDF are outside scope.
- Adaptive crop can miss very faint content, complex shadows, photographs or colored backgrounds. Inspect the preview before export; Reset crop preserves access to the original.
- Session recovery is limited to completely written originals and committed manifests. A page still in the scanner/driver at a crash cannot be recovered. A corrupt manifest is retained but not automatically repaired.
- Session storage is unencrypted. Keep/discard is explicit; cleanup is normal deletion, not secure erasure.
- One app instance per Windows login prevents competing writes. External apps can still hold the scanner busy.
- 100-page and 600 DPI peak-memory targets must be measured on real x86/x64 Windows. Single-page rendering can still consume significant RAM.
- Unsigned portable EXEs may show Windows SmartScreen warnings. Code signing is not configured.
- Package license metadata and exact source references must be reviewed before external redistribution. Build scripts collect notices and LGPL source snapshots where repository commits are supplied.

Release gates: successful Windows tests, actual device matrix, no-.NET clean-machine startup for both architectures, complete license/source review, performance measurements.

Runtime inventory: the main application is published with .NET/WPF 10.0.15. The unmodified upstream NAPS2 Win32 worker is a separate self-contained binary and embeds .NET 10.0.9; updating the main runtime does not update that embedded worker runtime. Rebuilding or replacing the worker requires separate Windows compatibility validation.
