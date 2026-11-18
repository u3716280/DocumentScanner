# Hardware acceptance checklist

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

Automated authoring/CI did not operate physical hardware. Separately, the project owner reported the planned 2.1.0 Windows acceptance passed on 18 November 2026 with an HP Smart Tank 615 and both x64/x86 packages. Untested stress and fault-injection rows in this broader matrix remain future environment-specific checks unless explicitly recorded.

Repeat-scan tone regression (v1.0.3): reset app Brightness/Contrast to 0; keep device/source/DPI/color settings fixed; disable the native driver dialog for a controlled test; scan the same test chart with Auto Crop off/on/on/off. Record preview and saved PNG colors, selected-page B/C values, driver type/model and whether hardware auto-exposure is active. Repeat with a nonzero app level and confirm the level is not accumulated across preview, crop toggles or export. Synthetic CI checks verify retained-pixel equality from the same input; physical acquisition may vary and is not covered by those checks.

## v1.0.4 editing smoke test (pending interactive Windows run)

- Import a non-square test page; rotate it 0/90/180/270 degrees. Open Manual Crop and select an asymmetric region. Verify the exact region in preview, thumbnail and PNG/JPG/PDF. Reopen crop to recover discarded edges; verify Cancel, reverse dragging, pointer clamping at image edges, Escape and resize selection reset.
- Verify Manual Crop requires one selected page; Auto Color and Reset Auto Color operate on all selected pages. Test Reset crop, Undo, session recovery and export after edits.
- Compare tinted paper with black text, grayscale pages, photographs and colored paper. Confirm white balance is useful for the document, inspect clipping, reset, and repeat correction/export to ensure it does not accumulate.

## 1.1.1 startup timing / behavior

Compare cold and warm launches with the same driver/device, first without profiles, then with Default Profile. Record log startup/recovery and per-backend/targeted probe timings, excluding user dialog delay when comparing driver speed. Test USB and network devices, powered-off/default devices, saved profiles without/stale defaults, explicitly loading another device/driver and Reset Profile. Confirm only first use and Reset invoke all-backend discovery; default/explicit profile load queries the exact target and one capability response per load. Confirm Source/DPI/Color stay correct, missing devices do not cause automatic substitution, Cancel cannot become late Ready, reset keeps profiles/pages/delivery journals, and F5 is equivalent to Reset Profile. Actual scan preflight still revalidates capabilities.

## 2.0.0: ERPNext scan link and HP Smart Tank 615

Run the manual checklist in `ERPNext-Scan-Link.md` (scan link, ERPNext v15, scanner). Record the Windows version,
browser and version, HP driver version, whether the driver is 32- or 64-bit, and the result of each item.
Project-owner acceptance was reported as passed on 18 November 2026. See `Validation-Report.md` and the completed checklist in `ERPNext-Scan-Link.md`.

## 2.2.0: tray icon and start-up speed (pending interactive Windows run)

Record the `Startup timing` log line for a cold launch (first after sign-in) with 2.1.0 and with 2.2.0 on the same PC. Then check: the scanner icon in the tray is sharp at 100/125/150 %; close (X) hides to the tray with a one-time notice, pages and the selected scanner unchanged; clicking the icon reopens instantly; a Scan link from ERPNext while hidden brings the window to the front with the destination set; double-clicking DocumentScanner.exe while hidden brings it back; tray Exit with pages asks keep/discard and Cancel keeps it running; tray Exit while a dialog is open only brings the dialog forward; signing out with pages offers recovery at the next start.

## 2.3.0: scan progress and accent buttons (pending interactive Windows run)

With the HP Smart Tank 615 on flatbed and on the feeder, with WIA and with TWAIN: the progress card appears at Scan, shows "Starting the scanner…", then the page number and a percentage that rises during each page (or a moving bar when the driver reports none); the taskbar button shows the same progress; each finished page appears in the list; Cancel and scanner errors clear the card. Hover and press every teal button in Settings, Save document, Profile manager and the send/retry dialogs: the text stays white and readable.
