# Document Scanner 1.1.0 — validation candidate

Native Windows WPF scanner workspace. USB / network scanner selection, WIA / TWAIN / eSCL, flatbed / feeder / duplex according to driver capabilities. Disk-backed PNG originals, thumbnails, page rotation, selection, reordering, Undo, profiles, recovery, JPG / PNG / multipage PDF export.

**Windows CI (v1.1.0):** [Build passed](https://github.com/u3716280/DocumentScanner/actions/runs/43609631356): 100 unit tests, 22 Windows integration tests, 7 isolated bridge contract tests, portable x86/x64 packages and bridge source ZIP. Physical scanner tests remain pending.

**Release status:** See `docs/Validation-Report.md`. A successful build is not evidence of hardware compatibility. Do not treat this source package as a hardware-certified production release.

## Run a portable build

1. Extract the complete architecture-specific ZIP into a writable folder.
2. Run `DocumentScanner.exe` with a standard Windows account. Keep all DLLs and the bundled `NAPS2.Worker.exe` beside it.
3. Install the manufacturer's scanner driver first for WIA/TWAIN. An eSCL-capable network scanner can work without a vendor driver.
4. Select a driver entry and click Scan. Refresh is F5. Use Scan More to append pages to the same document.
5. Ctrl/Shift-click thumbnails to select pages. Rotate, drag to reorder, delete, or Undo (Ctrl+Z).
6. Save (Ctrl+S), choose a format and destination. Filenames automatically avoid collisions without overwriting existing files.
7. On closing, choose whether to keep the session for recovery or discard it.

Default scan settings: Color, 300 DPI, A4, Auto deskew on, Auto crop off, blank-page marking off. Possible blanks are only flagged; they are never automatically deleted. Import images is useful to exercise the document workspace without hardware; multi-frame TIFF currently imports its first frame only.

Auto paper uses the driver's maximum reported scan area, falling back to A4. It does not promise physical page-edge detection. Auto Crop uses adaptive background detection and connected components to ignore isolated noise and thin scanner-edge artifacts. Content crops retain a 3 mm margin; white paper detected on a dark platen is cropped to its paper bounds. Review faint, colored or unusual originals before using it.

For network scanning, expand NETWORK SCANNER and enter an IP or a full endpoint such as `http://192.168.1.10/eSCL/`. Protocol support and URL path depend on the device. HTTPS certificate validation is retained.

## Windows and architecture

- Build targets: .NET 10.0.15 (SDK 10.0.404), win-x86 and win-x64, self-contained.
- Prefer x64 on Windows 11 or Windows 10 64-bit.
- x86 targets Windows 10 32-bit and 32-bit scanner drivers. A TWAIN 64-bit-only driver requires the x64 app.
- The x64 app includes the NAPS2 Win32 worker for 32-bit TWAIN data sources.
- Supported Windows editions/builds follow the .NET 10 support matrix; Windows 10 Home/Pro builds outside Microsoft's support policy are compatibility targets requiring testing, not a support guarantee.

## Build on Windows

Install the .NET 10 SDK and Python 3.12+, then open PowerShell in this directory:

```powershell
dotnet restore DocumentScanner.sln
dotnet build DocumentScanner.sln -c Release --no-restore
dotnet test tests/DocumentScanner.UnitTests -c Release
dotnet test tests/DocumentScanner.IntegrationTests -c Release
./build/Publish.ps1 -Runtime win-x64
./build/Publish.ps1 -Runtime win-x86
```

Output: `artifacts/DocumentScanner-v1.1.0-win-x64.zip`, x86 equivalent, and SHA-256 checksums. Build machine needs internet for NuGet and license source retrieval. Normal application use does not need internet.

Open `DocumentScanner.sln` in a Visual Studio version supporting .NET 10, or use the CLI. GitHub Actions is prepared in `.github/workflows/windows-build.yml`; upload this source tree to the desired repository, then run **Windows portable build** from Actions. Repository: https://github.com/u3716280/DocumentScanner. The workflow runs on pushes to main; hardware testing still requires a physical scanner.

## Data, privacy and recovery

Config, Logs and Sessions are beside the application when writable; otherwise under `%LOCALAPPDATA%/DocumentScanner`. Original scans can be sensitive: session folders contain unencrypted images. Keep/discard sessions explicitly and protect the folder with Windows account permissions. No telemetry, OCR downloads or scan-sharing server is enabled. ERPNext uploads are optional, explicitly confirmed actions and require the separately installed bridge. See docs/ERPNext-Setup.md.

Settings use JSON and are saved automatically. Invalid configuration is backed up before defaults are used. Session images are immutable; rotation and crop are metadata edits, so saving repeatedly does not repeatedly JPEG-encode the originals.

PNG is lossless relative to the acquired data. If a scanner itself transmits a JPEG, saving that acquired image as PNG cannot recover details already lost in the device.

PDF export defaults to original dimensions; optional A4/Letter rendering preserves aspect ratio. JPEG quality applies to JPG export. Export cancellation may wait for the SDK's PDF writer or the scanner driver to return; files are only finalized after validation. Completed image exports remain available after partial failure.

## Troubleshooting

- No scanner: install the scan driver (not just printer driver), Refresh, try WIA/TWAIN, or configure eSCL.
- Unsupported options: change source or use the native settings dialog. Drivers can report incomplete capabilities.
- TWAIN failure: use the x64 build with bundled worker; check that `NAPS2.Worker.exe` was not removed. The x64-specific entry accesses 64-bit sources.
- Paper jam/disconnect: acquired pages remain. Fix the device, then Scan More.
- Out of space: free storage in the session and output drives, then retry. Keep the session until export is verified.
- Logs contain operation categories, exception types and HRESULTs, without document image contents.

See `docs/Hardware-Tests.md`, `docs/Known-Limitations.md`, `docs/Architecture.md`, and `docs/Third-Party.md`.

## Auto Crop update (1.0.1)

Select one or more thumbnails and click **Auto Crop**. This applies crop, updates the selected preview and thumbnails, and affects JPG/PNG/PDF export. The checkbox **Auto crop (selected + new scans)** applies immediately to selected pages and sets the default for future scans. **Reset crop** restores the full image; Undo restores the previous crop flags. Original PNG files are never overwritten.

The preview footer reports the actual output pixel dimensions and whether a crop was found. A blank page or a page without removable margins stays at its original size. A4/Letter PDF output deliberately keeps the chosen paper size, even after cropping; use **Original** PDF page size or PNG/JPG when comparing the cropped image dimensions.

## Profile correction (1.0.2)

Save Profile captures Paper Source, DPI, Color Mode, Paper Size and all processing flags together. Load Profile queries the selected device first, then restores the saved settings as one update. Bound ComboBox null selections during list refresh are ignored/restored instead of overwriting stored values with enum defaults or zero DPI. Unsupported saved values produce visible warnings and do not change the saved profile itself. The selected profile name is synchronized with the name box.

Upgrade with the application closed. Preserve the existing Config folder (and Sessions if recovery is needed); the distribution does not contain a replacement settings.json. Existing schema-version-1 profile files remain compatible. If an older version already saved incorrect default values, select the intended Source/DPI/Color, save that profile again, change the settings, then Load Profile to verify restoration.

## Tone controls and repeated scan correction (1.0.3)

Brightness and Contrast show their exact integer values next to the sliders, with a live thumb tooltip and a **Reset Brightness / Contrast to 0** button. Zero means no app tone adjustment. These controls apply to new scans; the preview footer also reports the selected page's stored B/C values. Profiles retain their integer settings.

Each scan requests neutral brightness/contrast from WIA/TWAIN when the native scanner dialog is off. App adjustments are stored per page and rendered once from the immutable acquired PNG, after geometry operations. Auto Crop never changes acquisition brightness/contrast and never overwrites the original. Preview, cropped thumbnails and export use the same tone path. Opening old sessions defaults the new page fields to zero, preserving their existing image bytes.

After upgrading, click Reset to 0 and scan the same page again with Auto Crop on/off. If the native scanner settings dialog is enabled, it can apply its own exposure/contrast settings; reset those there for a controlled comparison. Earlier scans whose colors were already baked into the original cannot be repaired automatically by this update.

## Manual crop and document color correction (1.0.4)

Select exactly one page and click **Manual Crop**. Drag a rectangle over the oriented full image, then **Apply Crop**. Drag again to replace the selection; Cancel/Escape leaves the page unchanged. Resizing the dialog clears the selection. Manual Crop replaces Auto Crop for that page, persists in session recovery, and follows later page rotation. Reset crop restores the full page; Undo restores the previous edit.

Select one or more pages and click **Auto Color** to balance tinted paper and improve document contrast. **Reset Auto Color** disables it, keeping any manual brightness/contrast values. The conservative histogram correction estimates a white point per color channel; dark pages may remain unchanged. It is designed for documents: inspect photos, artwork and strongly colored paper before saving. It does not recover clipped or missing detail.

Preview, thumbnails and JPG/PNG/PDF export share the same renderer: crop, rotation, optional auto color, then the stored manual brightness/contrast. Every render starts from the original file, so repeatedly applying Auto Color does not accumulate processing. Both edits preserve the original and support Undo. No scanner-specific profile changes are required: these tools edit acquired/imported pages.

## Profiles, three Save Actions and ERPNext (1.1.0)

The native WPF workspace now follows the reviewed layout: toolbar Profile selector, grouped page editing, separate Next scan / Selected pages settings, prefix/format output strip and three explicit Save Actions. Current-page tone changes require Apply tone and do not change future scan defaults. Manual crop now has movable/resizable handles, numeric percent coordinates, keyboard movement/resizing and retained selection on window resize.

Manage profiles supports Create, Save, Save As, Rename, Delete, Default and JSON Import/Export across Scanner / Processing / Output / ERPNext groups. Profiles use schema 2 with stable IDs. Legacy configuration is backed up before migration; newer unsupported schemas block writes. Loading validates the exact device/driver and capabilities before applying settings; unavailable/unsupported values are not silently replaced. Unknown capabilities require explicit native-dialog handling. Profile loading never confirms an ERPNext destination.

Save Local exports selected pages only. Send to ERPNext needs no permanent local output folder. The combined action completes local copies before upload, retains successes and journals per-file outcomes for retry. In-app search shows real document details and requires separate Confirm destination and Send now actions. A new/recovered session starts unconfirmed. API credentials are held in Windows Credential Manager, outside profile JSON.

**ERPNext prerequisite:** install the companion `document_scanner_bridge` on an authorized staging site first. Source is under `integrations/document_scanner_bridge`; CI packages it as `DocumentScanner-ERPNext-Bridge-v1.1.0.zip`. See [setup and limitations](docs/ERPNext-Setup.md). The build does not deploy this app, configure credentials or establish live-site/hardware compatibility. The bridge contract tests use isolated fakes, not a running Frappe/Redis/database stack.
