# 2.1.0 limitations

- ERPNext: no exactly-once guarantee and a cancellation race remain under the standard API; the duplicate-name check is limited to attachments the user may see. Details and the reasons are in `ERPNext-Scan-Link.md`.
- PDF pages are embedded without lossy compression, so long colour documents can exceed the site's upload limit.
- Both win-x64 and win-x86 portable packages are produced and statically validated. The x64 application uses the bundled x86 NAPS2 worker for 32-bit TWAIN sources and can also enumerate 64-bit TWAIN sources; the x86 application supports 32-bit TWAIN sources only.
- The x86 process has a smaller address space. Long colour documents and 600–1200 DPI pages require physical memory testing; build success is not evidence that every large job fits in x86 memory.
- The WPF UI based window (integrated title bar), the scan link in a real browser, a live ERPNext site and a physical scanner are untested. Automated tests render the window content without showing a window.
- Sections below describe 1.x. Statements there about the Bridge app or a 20 MiB limit no longer apply.

# Known limitations and release gates

- Hardware compatibility, full application UI, native scanner dialogs and clean Windows deployment require manual testing. WPF ComboBox bindings and image/PDF codecs passed the 15-test Windows CI integration suite; USB and network scanner hardware remain untested.
- Capabilities may be incomplete or incorrect. Duplex/ADF availability follows reported capabilities. Unknown capabilities require native UI; the app does not claim automatic duplex support.
- Auto paper uses maximum reported scan area, with A4 fallback. Content cropping is reversible and adapts to background brightness; it can also detect a light document on a dark platen. It is a heuristic, not a guarantee for every paper/background.
- Brightness/contrast for new scans are per-page app adjustments, rendered once from the acquired original. Non-native WIA/TWAIN scans request neutral hardware values each time. A native scanner dialog can still apply its own adjustments; physical auto-exposure behavior requires device testing.
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

## 1.1.0 new integration boundary

ERPNext actions require installing/configuring the supplied bridge and API account separately; no live site deployment/test was performed. Target Frappe v15 source contracts were reviewed, but server version, private-file hooks, permissions, database and Redis retry semantics need staging acceptance. HTTPS with valid TLS is required. Per-file bridge limit is 20 MiB and site limits may be lower. WPF layout and interactive crop/profile flows require Windows/scaling smoke tests; source-level and automated checks do not establish pixel equivalence with design images.
