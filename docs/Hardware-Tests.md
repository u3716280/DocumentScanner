# Hardware acceptance checklist — NOT YET EXECUTED

Record for each run: date, tester, Windows edition/build, OS/process architecture, device model/firmware, connection (USB/Ethernet/Wi-Fi), driver name/version, settings and result.

| Case | Expected |
|---|---|
| Flatbed page | One correctly oriented page; actual DPI and dimensions recorded |
| Scan More with flatbed | New pages appended, originals retained |
| ADF 20 pages with numbered sheets | 20 pages in order |
| Duplex 10 numbered sheets | 20 pages, correct front/back order, only if hardware supports it |
| x64 app + TWAIN x86 | Worker loads driver, scans and exits cleanly |
| TWAIN x64 entry | Uses 64-bit data source, when available |
| eSCL discovery and manual endpoint | Same device reachable through both supported paths |
| Native scanner dialog | Parent window, settings, cancel and return work |
| Cancel during page acquisition | UI responsive; completed pages recoverable |
| Empty feeder before scan | Friendly error; session unchanged |
| Feeder naturally exhausted | Normal completion with correct count |
| Jam/disconnect after page 3 | Earlier pages retained; retry after device reset |
| 100-page color A4 300 DPI | Record peak private memory/temp storage; no out-of-memory |
| 600 DPI, x86 | Record memory, scan time; no full-document bitmap accumulation |
| Rotate/reorder/delete/undo | Preview and exports agree |
| Crash after received pages | Restart recovery returns all manifest-committed pages |
| PNG/JPG/multipage PDF | Independent viewer opens all outputs in expected order |
| A4/Letter PDF, mixed orientation | Correct physical dimensions and aspect ratio |
| Standard user, no .NET installed | Both extracted portable packages start; worker runs |
| Read-only app folder | Per-user configuration fallback |
| Read-only output / full disk | Controlled error; session kept; no corrupt finalized output |
| 100/150/200% display scale | Controls, preview, drag and native dialogs remain usable |

When testing HP Smart Tank 615 or another multifunction printer, record capabilities from its actual installed scanner driver. Never infer ADF/duplex from the printer's printing capabilities.

No real scanner, Windows desktop, jam, duplex, or clean-machine test has been performed in the Linux authoring environment.

Repeat-scan tone regression (v1.0.3): reset app Brightness/Contrast to 0; keep device/source/DPI/color settings fixed; disable the native driver dialog for a controlled test; scan the same test chart with Auto Crop off/on/on/off. Record preview and saved PNG colors, selected-page B/C values, driver type/model and whether hardware auto-exposure is active. Repeat with a nonzero app level and confirm the level is not accumulated across preview, crop toggles or export. Synthetic CI checks verify retained-pixel equality from the same input; physical acquisition may vary and is not covered by those checks.

## v1.0.4 editing smoke test (pending interactive Windows run)

- Import a non-square test page; rotate it 0/90/180/270 degrees. Open Manual Crop and select an asymmetric region. Verify the exact region in preview, thumbnail and PNG/JPG/PDF. Reopen crop to recover discarded edges; verify Cancel, reverse dragging, pointer clamping at image edges, Escape and resize selection reset.
- Verify Manual Crop requires one selected page; Auto Color and Reset Auto Color operate on all selected pages. Test Reset crop, Undo, session recovery and export after edits.
- Compare tinted paper with black text, grayscale pages, photographs and colored paper. Confirm white balance is useful for the document, inspect clipping, reset, and repeat correction/export to ensure it does not accumulate.
