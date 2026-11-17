# Architecture

App: WPF MVVM; simple commands and dependency injection. A dedicated STA dispatcher hosts each scanner/export operation so the UI is not blocked. NAPS2 additionally routes Win32 TWAIN through its bundled worker. Scanner service uses a semaphore across discovery/capabilities/acquisition.

Core: no WPF or NAPS2 dependency. PageData records, source capabilities, scan/export validation, document ordering/undo and explicit acquisition-state transitions.

Infrastructure: JSON atomic write via temp + rename; writable app folder/per-user fallback; disk session manifests and recovery; no-overwrite output finalization; bounded daily logs.

Scanning: direct NAPS2 APIs; per-driver entries and per-source capabilities; URI-identified eSCL devices; lossless local page persistence. NAPS2 Pdfium native binaries are included because lossless eSCL transport can return PDF.

Export: load and render one page at a time, encode PNG/JPG using WPF; import prepared pages into a file-backed NAPS2 context and export PDF. PdfPig validates readable page structures and expected count before finalization. Fixed-paper output is raster-composed with a white background, so it may require re-rendering at up to 600 DPI. No OCR.

Session: immutable PNG originals; separate small thumbnails; rotation/crop flags and list ordering in JSON. Undo retains page references, not bitmap copies. Acquired pages are added to earlier undo snapshots so Undo cannot discard newly scanned pages. A document may span many scan operations. Keep/Discard on close is independent of successful Export.

Security: optional explicit ERPNext uploads through the standard Frappe REST API with the user's own API key (2.0; 1.x used a separately installed bridge app); no scan-sharing server or telemetry, no arbitrary command execution. Export basename rejects Windows device names and path characters; recovery paths must remain under the session root. HTTP/HTTPS endpoints are explicit user input and verified through scanner capabilities. TLS certificate checks are not disabled.


1.1.0 adds profile schema 2, immutable output snapshots/outboxes, per-artifact delivery journals, local copy resume, receipt reconciliation, Windows Credential Manager and in-app destination verification. Scanner profiles hold site references, never credentials or reusable document confirmations. The bridge validates document read/write and field permissions, scopes idempotency keys to authenticated users, verifies target/privacy/hash, and commits attachment/receipt before releasing its lock. Source and setup are versioned separately from site deployment.

2.0.0 replaces the 1.1.0 ERPNext design. A `documentscanner://scan` link from an ERPNext Client Script names a site, DocType and document. `ScanLinkParser` (Core) validates it; the site must be in the user's list and the DocType one of three. `FrappeClient` (Infrastructure) reads the document, lists its attachments, reads the upload limit and uploads with `upload_file`; every path is built from validated values and redirects are refused. The destination is fixed for the session and stored beside it. A delivery renders once into the session outbox, names every file (`AttachmentNaming`, `DeliveryPlanner`), saves locally first when asked (`LocalDelivery`), then uploads (`UploadRunner`), re-reading the document and its attachments before each file. `SingleInstance` forwards a second copy's link over a per-user named pipe; `UriRegistration` manages the HKCU scheme entry. Profiles no longer hold ERPNext settings.
