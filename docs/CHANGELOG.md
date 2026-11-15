## 1.2.1 — Safe ERPNext delivery and PDF boundaries

- Add explicit retry/reconcile, keep and abandon choices for unfinished ERPNext deliveries; changed destinations start a new operation instead of reusing an idempotency key.
- Warn before New document, recovery discard or application close removes a session with an unfinished or unknown delivery.
- Classify definitive attachment/server rejection as Failed instead of Unknown. Attachments over the Bridge's 20 MiB limit are terminal and do not enter an impossible retry loop.
- Stream form-encoded Base64 directly from disk, avoiding simultaneous full file, UTF-16 Base64 and encoded-body copies on x86.
- Normalize Windows local retry folders, including a trailing separator and case-insensitive comparison.
- Remove completed/terminal delivery outboxes and journals while preserving unresolved frozen artifacts.
- Native scanner UI owns Auto paper selection; non-native Auto reads capabilities once for the selected source.
- Add exact 20 MiB boundary, streamed payload, failure classification, trailing-folder and baseline multipage PDF tests.

## 1.2.0 — Office workspace

- Office light theme and shared native WPF control styles; no new runtime UI library or hosted design account.
- One context-sensitive Scan/Scan more button, page selection checkboxes and explicit selection/output summaries.
- Separate Scan settings and Edit page tabs, with exact tone values and explicit selected-page apply/reset.
- Original/Adjusted preview modes and clearer local / ERPNext / combined save actions.
- Bridge prerequisite notices and installation guide in workspace, ERPNext profile, site settings and destination search. Main-window Bridge checks are explicit; startup does not contact ERPNext.
- Retry is visible only for unfinished session jobs. Existing session, profile-first discovery and non-destructive image paths are retained.
- Windows CI captures real WPF empty/review/edit views and compact layouts at 100/125/150 percent render scale using fictional documents.

# 1.1.1 — Profile-first initialization

- First-use startup searches all backends; Default Profile startup resolves only its exact device/driver and reads capabilities once.
- Saved profiles without a usable default wait for user selection; any selected profile can resolve its device without prior full enumeration.
- Add Reset Profile / F5: clear the active profile and discover all interfaces, retaining saved/default profiles and scan/delivery data.
- Prevent reentrant device-selector capability queries, duplicate startup initialization and late-cancellation readiness. Failed probes do not mark scanners available.
- Yield the first WPF frame before initialization; move recovery inspection off the UI thread; add privacy-preserving per-stage timings.
- Add startup-policy, targeted-query, unavailable/mismatched identity, cancellation and WPF orchestration tests. Actual hardware timing remains to be measured.
- ERPNext bridge remains version 1.1.0; this release changes Windows initialization only.

# 1.1.0 — Native redesign, complete profiles and ERPNext delivery

- Implement the reviewed native workspace, explicit selected-page save actions, output prefix/format preview and separate scan/page tone scopes.
- Add complete versioned profile lifecycle, default profile, import/export, backup migration, exact capability checks and atomic load, with Windows per-user credential storage for ERPNext sites.
- Add in-app document search/detail/confirmation, separate final send review, recoverable frozen output, independent local/upload reporting, resumable local copy and receipt-first upload retry.
- Include a separately deployed Frappe bridge with permission-filtered search/detail retrieval and scoped durable attachment receipts. No live deployment or production integration test is claimed.
- Improve manual crop with move/resize handles, numeric/keyboard controls and resize retention. Add New document/session isolation and original comparison.
- Add profile, WPF binding/layout, delivery failure/retry and isolated bridge contract tests. Actual hardware, interactive UI/scaling and live Frappe/Redis/database verification remain pending.

# 1.0.4 — Manual crop and document auto color

- Add a drag-to-select Manual Crop dialog for one page, using full oriented originals and normalized original-image coordinates.
- Persist crop and auto color edits in session manifests, with Undo and explicit reset actions.
- Add conservative document white balance/levels to selected pages, rendered once from original pixels across preview, thumbnails and export.
- Keep old sessions compatible: manual crop defaults to null and auto color defaults to false.
- Add coordinate, persistence, undo, alpha/dark-image, rotated crop/export and repeated color/export regression coverage. Physical scanner and interactive Windows mouse smoke tests remain pending.

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

