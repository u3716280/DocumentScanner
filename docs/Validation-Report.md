# Document Scanner 2.0.0 — ERPNext scan link

Status: see the CI run for the commit that carries this file. Figures below are from the local Linux build and are
confirmed or corrected by CI.

Automated, passing:
- Unit tests (Core and Infrastructure, run on Linux and on Windows CI): scan link parsing and refusal cases, site and DocType allow-lists, file naming and duplicate numbering, delivery record and outcome rules, settings upgrade, secret redaction, launch arguments.
- Simulated ERPNext (an in-memory stand-in for the five REST calls): authentication, permission, missing and cancelled documents, upload success, partial failure, timeout before and after the server stored the file, duplicate content, server-side rename, size limit, redirect, certificate failure, retry of only the unconfirmed files.
- The multipart upload request was parsed with Werkzeug 3.1.9 (the version in the target bench): Thai file name and file bytes arrive intact.
- Windows integration tests: window content renders with and without a destination, button states for direct start, scan-link start and a cancelled document, second-copy link forwarding over the named pipe, registration command format.

Not tested:
- A live ERPNext site. No request was sent to any real site and no file was attached to any real document.
- A browser opening `documentscanner://`, the Windows registration itself, and the Settings window.
- A shown window: the integrated title bar, dialogs, and the confirmation and result windows.
- Any physical scanner, including the HP Smart Tank 615.
- The published x64 package on a clean Windows machine.

Use the manual checklist in `ERPNext-Scan-Link.md`.

# Document Scanner 1.2.1 — delivery/PDF validation

Status: Windows CI passed for application commit `b13729b49a51997c386674449f2f25ffb613f6ce`.

- Run: https://github.com/u3716280/DocumentScanner/actions/runs/43713418612
- 121/121 unit tests and 29/29 Windows integration tests passed.
- Portable win-x64 and win-x86 packages were built with self-contained .NET/WPF 10.0.15 and passed PE architecture, TWAIN worker and PDFium checks.
- Delivery checks cover exact/over 20 MiB boundaries, streamed form-compatible payload bytes, definitive HTTP rejection, unknown/permanent state handling, receipt-first retry and trailing-separator local folders.
- PDF checks cover readable multipage output, page count/order, original/A4 dimensions in portrait and landscape, crop/original preservation, cancellation and a baseline three-page artifact below the Bridge limit.

These automated checks do not establish that every real 300 DPI colour document will remain below 20 MiB. Oversized output is now reported as a terminal ERPNext failure without an impossible retry loop; PDF compression policy still requires representative-document measurement. Physical scanner and live ERPNext acceptance remain pending.

# 1.2.0 Office workspace validation

Status: Windows CI passed for application commit `2a497b6546998e92a01b601629fa7d4c0d094f25`.

- Run: https://github.com/u3716280/DocumentScanner/actions/runs/43708990783
- 116/116 unit tests and 28/28 Windows integration tests passed.
- Portable win-x64 and win-x86 packages were built and passed static package/dependency verification.
- All five actual WPF captures were retrieved through the GitHub artifact connector and archive SHA-256 verified. Empty/review/edit and compact layout captures were inspected. This led to corrected collapsed ComboBox display names, more compact scan settings, non-transparent workspace background and viewport-based Fit sizing.
- Main output actions remain in bounds at 1440×900, 1280×800 and 1000×700 logical sizes. The preview does not acquire a horizontal scrollbar at Fit in the compact fixture.
- Retained startup tests verify profile-first discovery; the workspace fixture adds no extra device discovery/capability calls or automatic Bridge test.

Physical scanner, interactive per-monitor DPI and live ERPNext acceptance remain pending. Static render-scale fixtures do not replace those checks.

New checks cover default-profile startup without extra discovery, page selection/output summaries, selected-page tone reset without changing next-scan settings or original bytes, Original/Adjusted preview behavior, ERPNext HTTP advice and invalid connection payloads. CI emits actual WPF layout captures at 1440×900, 1280×800 (125% render), and 1000×700 (150% render), including empty, review and edit states. Rendered scale tests do not replace interactive per-monitor DPI testing on Windows hardware.

The existing image/export, scanner profile, recovery and delivery retry suite is retained. Native acquisition and initialization policy are unchanged. No live scanner, credentials or ERPNext site is connected by CI.

Previous validation history follows.

# Document Scanner 1.1.1 — Validation Report

Status: **Windows CI passed; validation candidate.** Physical scanner, interactive Windows/scaling and live ERPNext deployment acceptance remain pending.

Date: 14 November 2026.
Code commit: `0ac1805931961c67d799fa6494bf3be28887e1da`.
Evidence: [Windows portable build run 43612441172](https://github.com/u3716280/DocumentScanner/actions/runs/43612441172).

| Check | Result | Scope / limits |
|---|---|---|
| Windows Release build | Passed | Native WPF compilation; not a visual or hardware certification |
| Unit tests | 111 passed | Existing document/image/profile tests plus schema migration, future-schema preservation, full profile round-trip, exact capability handling, prefix contracts, selected-page tone, confirmation identity, HTTPS validation, local resume, upload response-loss reconciliation and acknowledged-item skipping |
| Windows integration tests | 27 passed | PNG/JPG/PDF/export orientation and pixel regressions; WPF bindings; profile load into an existing window updates Source/DPI/Color/Prefix and engine options; all three save actions are present and unavailable with no selected pages; Profile Manager groups exist without applying settings on open |
| Bridge contract tests | 7 passed | Isolated fake-Frappe tests of authentication, document/write/revision checks, per-user operation identity, receipt destination/privacy/content verification, bad payload rejection and reuse of a completed receipt |
| Portable win-x64 | Published and statically verified | Complete ZIP + SHA-256 checksum, dependency/license inventory and worker/runtime checks |
| Portable win-x86 | Published and statically verified | Complete ZIP + SHA-256 checksum, dependency/license inventory and worker/runtime checks |
| ERPNext bridge source | Packaged | Source ZIP included with portable artifact; not installed on a server by CI |

## Downloads

Open the CI run and download **Artifacts → DocumentScanner-portable**. It contains:

- `DocumentScanner-v1.1.1-win-x64.zip`
- `DocumentScanner-v1.1.1-win-x86.zip`
- Associated Windows ZIP checksums and inventories
- `DocumentScanner-ERPNext-Bridge-v1.1.0.zip`

Separate **test-results** artifact contains TRX reports. The run logs include isolated Python bridge test results.

## What this establishes

The requested native redesign and application features compile and pass the described automated checks. Profiles preserve scanner/processing/output/ERPNext fields in schema 2 and store no API credentials or reusable confirmed destination. Unsupported profile settings are checked before atomic application; unknown scanner capabilities require explicit native-dialog handling. Current-page edits remain metadata over originals.

Output operates on selected pages. Local-only saving is independent of ERPNext. Send-only uses recoverable temporary artifacts; combined delivery saves local copies before upload. Per-artifact journals retain hashes/receipts; retry queries the server before re-sending and skips acknowledged successes. Explicit destination confirmation and a separate final Send action are required.

## Required acceptance still pending

- Actual WIA/TWAIN/eSCL hardware: source/duplex/DPI/color behavior, repeated scans and native driver overrides.
- Interactive Windows layout, crop move/resize/keyboard interaction, High Contrast, screen readers, long localized labels and display scaling. Automated binding/layout checks do not establish pixel equivalence with the approved design images.
- Installed Frappe/ERPNext staging: bridge installation/migration, permission/field mappings, private/public File behavior, custom File hooks, database isolation, distributed Redis lock/receipt behavior, concurrent retries, crash/rollback and network interruption after server commit.
- Windows Credential Manager setup under the actual user's account and portability to another Windows account/PC.

No production site, live API credentials or real ERPNext records were used. Do not interpret fake transport/bridge tests as a production exactly-once or compatibility guarantee. See [ERPNext setup](ERPNext-Setup.md), [hardware checklist](Hardware-Tests.md) and [known limitations](Known-Limitations.md).

## Startup optimization evidence (1.1.1)

Automated startup cases verify:

| Scenario | Verified behavior with fake scanner transport |
|---|---|
| No profiles | One all-backend discovery, one selected-device capability request; repeated Initialize calls share the same task |
| Valid Default Profile | Zero full-discovery calls; one exact-device lookup and one capability request |
| Profiles without a default / stale default reference | No automatic scanner calls until explicit selection or Reset |
| Load another profile | Its device can be found and added without previous full enumeration; exact ID/driver/TWAIN architecture is checked |
| Reset Profile | Full discovery runs, active profile clears, saved/default profile records are retained |
| Bound WPF window | Startup/reset ordering stays correct with real ComboBox bindings; deferred null selection cannot erase the verified device or add another capability query |
| Unavailable/mismatched/cancelled target | No automatic full search/substitution; cancellation after a late capability response cannot return a prepared scanner |

WPF yields before recovery/driver work and recovery inspection runs off the UI thread. Actual scan preflight validation and serialized scanner access remain in place. New logs record recovery/startup, each full-search backend, exact-target lookup and capability durations without device endpoints/IDs or document contents.

These checks establish work ordering and call counts, **not a measured wall-clock startup speedup**. Cold/warm launches, network discovery, vendor initialization and actual hardware response still require the user's timing comparison. The unchanged ERPNext bridge remains version 1.1.0.

