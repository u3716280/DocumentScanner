# CLAUDE.md

Guidance for Claude Code working in this repository. This repository is **public**, so this file never names real sites, people, credentials or internal hosts.

## What this is
Document Scanner: a Windows desktop scanning app (.NET 10, WPF + WPF-UI 4.3, NAPS2.Sdk 1.6). It scans with WIA, TWAIN or eSCL, edits pages (crop, tone, reorder), exports to PDF/JPG/PNG and sends files to ERPNext from a `documentscanner://scan?...` link. The portable packages are x64 and x86.

## Layout
- `src/DocumentScanner.Core`: models and pure logic (scan links, progress text, settings). No WPF.
- `src/DocumentScanner.Scanning`: NAPS2 wrapper (`Naps2ScannerService`).
- `src/DocumentScanner.Export`: PDF/image export.
- `src/DocumentScanner.Infrastructure`: paths, settings/session stores, credential store, ERPNext client.
- `src/DocumentScanner.App`: WPF UI. `Program.cs` is the entry point: single instance through a mutex and a named pipe, plus the splash screen. Also here: `App.xaml.cs`, `MainWindow`, `MainViewModel*.cs`, `TrayIcon.cs`, `Themes/Office.xaml` (colour tokens), and `Ui.cs` (code-built dialogs).
- `tests/DocumentScanner.UnitTests`: run anywhere. `tests/DocumentScanner.IntegrationTests`: WPF, Windows only.
- `build/`: `Publish.ps1` (portable package), `collect_licenses.py`, `verify_package.py`, and `make_icon.py` (draws `Resources/scanner.ico`).
- `erpnext/client-scripts/`: the ERPNext Client Scripts that add the Scan button.
- `docs/`: CHANGELOG, Architecture, Third-Party, Hardware-Tests, Known-Limitations, ERPNext-Scan-Link.

## Build and test
- On Linux: `DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH dotnet build DocumentScanner.sln`, then `dotnet test tests/DocumentScanner.UnitTests`. The SDK version is in `global.json`.
- Integration tests and packaging run only on Windows, in CI: `.github/workflows/windows-build.yml`. Portable ZIPs: `gh workflow run windows-build.yml --ref <branch> -f publish_portable=true`.
- `promote-release.yml` turns a tested tag's ZIPs into a GitHub Release. `prune-old-portable-artifacts.yml` keeps only the newest portable artifact.

## Working rules
- `main` is the only long-lived branch. Do new work on a short-lived branch off an up-to-date `main` and open a PR; the owner merges it. Never push straight to `main`.
- Never create tags or GitHub Releases unless the owner asks. The version is set in `Directory.Build.props` and as the default in `build/Publish.ps1`. Keep both in step, along with a `docs/CHANGELOG.md` entry, `README.md`, and `START-HERE-TH.txt` (Thai notes for users).
- Changes to behaviour need tests. Put pure logic in Core with unit tests; use integration tests for WPF.
- Primary (teal) buttons must be `Wpf.Ui.Controls.Button` with `Appearance=Primary`. Don't set colours by hand: the hover state turns the background light and the white text vanishes.
- The scan link never carries passwords, API keys or tokens. API credentials live only in Windows Credential Manager.
- Use placeholder hosts in code, tests and docs: `erp.example.com`, `192.168.1.10`.

## License
DocumentScanner's own code is MIT (`LICENSE`). The NAPS2 SDK is LGPL. PDFium and other notices are under `build/license-overrides`. Packages ship every notice in `Licenses/`, and `verify_package.py` checks that `Licenses/DocumentScanner-LICENSE.txt` is there.
