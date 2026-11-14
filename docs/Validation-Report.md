# Validation report — 14 November 2026

Status: **v1.0.3 Windows CI passed; physical scanner and full manual application acceptance remain pending.**

Current Windows evidence: https://github.com/u3716280/DocumentScanner/actions/runs/43586676124 (code commit d9a656b1aca77a0d2c499685dcb2c1cca3b94940). Earlier authoring notes below describe Linux-only checks before this run.

| Check | Observed result |
|---|---|
| SDK | .NET SDK 10.0.404 Linux x64; downloaded archive verified using SHA-512 from official .NET release metadata |
| Solution compilation | PASS, 7 projects, 0 warnings, 0 errors |
| Unit tests | PASS, 73 passed, 0 failed, 0 skipped on Windows CI (also passed on Linux) |
| Windows image/PDF and WPF binding integration tests | PASS, 15 passed, 0 failed, 0 skipped on windows-2022 |
| win-x64 self-contained publish | PASS |
| win-x86 self-contained publish | PASS |
| PE architecture checks | PASS for app EXE, CoreCLR, x86 worker and matching PDFium native DLL |
| Main runtime inventory | .NET and Windows Desktop 10.0.15 included in each package |
| Win32 worker | NAPS2 supplied 1.6.0 binary present; x86 PE; embeds its upstream .NET 10.0.9 runtime |
| XAML, resource/project XML and workflow YAML parsing | PASS |
| UI resource name | Compiled `DocumentScanner.App.Resources.Strings.resources` verified in assembly |
| Package ZIP integrity | Checked after packaging |
| Native PDFium | x64 binary matched upstream Chromium 7947 release byte-for-byte; corresponding native license notices included |
| Full application UI launch/manual operation | NOT TESTED; WPF ComboBox bindings were exercised by CI |
| USB/WIA/TWAIN/eSCL scanning | NOT TESTED with physical hardware |
| ADF/duplex, jam/disconnection | NOT TESTED with physical hardware |
| 100-page/600-DPI memory measurements | NOT TESTED |
| Standard-user clean Windows with no .NET | NOT TESTED |
| GitHub Actions execution | PASS: restore, build, 73 unit tests, 15 Windows integration tests, x64/x86 publish, package checks and artifact upload |

Unit coverage includes invalid Windows filenames, Unicode filenames, numbering, delete/reorder/rotation/crop/clear/Undo, preservation of new pages across Undo, state transitions, invalid/null settings, configuration backup/round-trip, collision-safe file finalization and session recovery/path containment.

Nine Windows integration cases are provided (including a WPF ComboBox profile restoration case and three crop cases for JPG, PNG and PDF): JPG and PNG rotation/file count, PDF order and original physical dimensions, A4 fitting and blank-page non-destructive review, and cancelled PDF not being finalized. These passed on the Windows GitHub Actions runner.

The portable packages were cross-published on Linux. Successful compilation and static dependency checks do not prove runtime scanner compatibility. Follow Hardware-Tests.md before treating the application as production-ready.

Logs from the observed build, unit run, integration execution limitation and publish operations are included under docs/evidence in the source package. Package inventories include SHA-256 values for individual files. ZIP checksums are supplied with the deliverables.

## Auto Crop correction — 1.0.1

The previous checkbox affected only future scan acquisition, thumbnail rendering ignored crop metadata, and a global fixed-threshold bounding box could be pinned to scanner noise/shadows. The new checkbox applies to selected pages as well as future scans; Auto Crop and Reset crop are explicit, idempotent commands. Preview, thumbnails and export use the same renderer. The preview footer shows actual output dimensions and whether removable margins were found.

Seventeen new cross-platform test cases cover white/off-white/gray backgrounds, isolated noise, dark platen/paper bounds, scanner-edge strips, thin border frames, multiple content blocks, dark images, blank noisy pages, content near edges, invalid inputs, sparse small text and reversible multi-page crop flags. All 48 unit cases passed on Linux. Windows codec/UI regression tests are compiled but cannot be executed in this Linux environment. Original image files are preserved.

## Profile correction — 1.0.2

Twelve additional unit cases test the production ScanSettingsSelection component: reentrant collection-reset writes, selection stability during all property notifications, repeated capability refresh, switching device capabilities, source-specific option changes, unsupported settings with warnings, custom DPI with unknown detail caps, empty/unknown capabilities, persisted two-profile restart/load, deferred null SelectedItem writes and new user selections before saving. The resulting 60/60 unit cases passed.

The ViewModel uses the tested selection component, reads device capabilities before applying the saved profile, and binds the four ComboBoxes through nullable selection properties. Transient nulls cannot become Flatbed/Color/0 defaults. The model restores target notifications after deferred null selections. Saved profile records remain unchanged when unsupported values require an explicitly reported runtime fallback.

A Windows-only test uses actual WPF ComboBoxes with two-way bindings and dispatcher drains to check repeated profile restoration. It is compiled but has not run in this Linux environment. No Windows UI or physical-device pass is claimed.

## First Windows CI run — 14 November 2026

Windows portable build #1 succeeded on the unmodified v1.0.2 application source: 60/60 unit tests and 9/9 integration tests passed. The integration run includes actual WPF two-way ComboBox bindings with repeated profile restoration, PNG/JPG/PDF crop consistency, image orientation, PDF page size/order, blank-page handling and cancelled export. Both win-x64 and win-x86 self-contained packages, inventories and checksums were created and uploaded as the DocumentScanner-portable artifact. This supersedes the earlier compile-only Windows-test notes.

No physical scanner, native scanner dialog, full application interaction, clean-machine no-.NET deployment or 100-page hardware performance claim is made.

## Tone correction — 1.0.3

Local unit tests: 73 passed. The prior 60-unit/9-integration Windows evidence above belongs to v1.0.2. Six new Windows regression cases (15 total) exercise neutral crop retained-pixel identity, repeated export without accumulating a nonzero brightness level, neutral fresh scan options for WIA/TWAIN/eSCL, and live WPF slider/readout/reset bindings. All 15 Windows integration cases passed in the new CI run, including the six tone regressions.

The user's report identifies darkening in both preview and saved output; its physical device cause has not been reproduced here. Inspection found the earlier SDK-after-scan path skipped explicitly resetting WIA/TWAIN driver brightness/contrast. The new path resets supported non-native acquisition settings to neutral and applies app tone once as page metadata. No physical-device fix is claimed without a repeat scan on the user's scanner.

## Windows tone regression result — v1.0.3

Build run 43586676124 passed: 73/73 unit cases, 15/15 Windows integration cases, x64 and x86 publish, package verification and artifact upload. Neutral tone/crop processing retained every tested pixel byte; nonzero brightness stayed at one application across repeated crop toggles and PNG exports. Fresh WIA/TWAIN/eSCL option objects requested neutral acquisition tone, and the WPF slider/readout/reset bindings stayed synchronized. These are deterministic synthetic-input and configuration tests; no physical scanner reproduction or guarantee is implied.

## v1.0.4 manual crop / auto color validation

Cross-build: passed with no warnings/errors. Unit tests: 81 passed locally. Windows CI for this revision is pending at source submission. Five new Windows cases cover manual-crop pixel identity and PNG export at all four rotations plus repeatable auto-color preview/export/reset. Interactive mouse/dialog smoke tests remain pending; see Hardware-Tests.md.
