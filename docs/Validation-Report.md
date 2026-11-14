# Document Scanner 1.1.0 — Validation Report

Status: **Windows CI passed; validation candidate.** Physical scanner, interactive Windows/scaling and live ERPNext deployment acceptance remain pending.

Date: 14 November 2026.
Code commit: `78edd9926b6d6277ee17666db616177a56195c78`.
Evidence: [Windows portable build run 43609631356](https://github.com/u3716280/DocumentScanner/actions/runs/43609631356).

| Check | Result | Scope / limits |
|---|---|---|
| Windows Release build | Passed | Native WPF compilation; not a visual or hardware certification |
| Unit tests | 100 passed | Existing document/image/profile tests plus schema migration, future-schema preservation, full profile round-trip, exact capability handling, prefix contracts, selected-page tone, confirmation identity, HTTPS validation, local resume, upload response-loss reconciliation and acknowledged-item skipping |
| Windows integration tests | 22 passed | PNG/JPG/PDF/export orientation and pixel regressions; WPF bindings; profile load into an existing window updates Source/DPI/Color/Prefix and engine options; all three save actions are present and unavailable with no selected pages; Profile Manager groups exist without applying settings on open |
| Bridge contract tests | 7 passed | Isolated fake-Frappe tests of authentication, document/write/revision checks, per-user operation identity, receipt destination/privacy/content verification, bad payload rejection and reuse of a completed receipt |
| Portable win-x64 | Published and statically verified | Complete ZIP + SHA-256 checksum, dependency/license inventory and worker/runtime checks |
| Portable win-x86 | Published and statically verified | Complete ZIP + SHA-256 checksum, dependency/license inventory and worker/runtime checks |
| ERPNext bridge source | Packaged | Source ZIP included with portable artifact; not installed on a server by CI |

## Downloads

Open the CI run and download **Artifacts → DocumentScanner-portable**. It contains:

- `DocumentScanner-v1.1.0-win-x64.zip`
- `DocumentScanner-v1.1.0-win-x86.zip`
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

## 1.1.1 startup optimization candidate

Local cross-build and unit checks are in progress; Windows CI evidence will be updated after completion. New tests cover first use, default-profile startup, no/stale default, explicit profile loading, Reset Profile, exact identity, failed lookups and cancellation. Hardware cold/warm startup timing has not been benchmarked. The ERPNext bridge source remains 1.1.0.
