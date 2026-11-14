# ERPNext setup — Document Scanner 1.1.0 validation candidate

Local scanning/export works independently. ERPNext search, detail verification and uploads require the companion Frappe app **document_scanner_bridge** on the target site. The Windows build does not deploy it and does not contain any API credentials. No live ERPNext site was connected during development.

## Server installation (administrator, staging first)

1. Download `DocumentScanner-ERPNext-Bridge-v1.1.0.zip` from the same GitHub Actions artifact as the Windows packages.
2. Extract its `document_scanner_bridge` source directory into your bench's `apps/document_scanner_bridge` (the directory containing `pyproject.toml`).
3. From the bench root, install it editable into the bench virtualenv: `./env/bin/pip install -e apps/document_scanner_bridge`.
4. Add `document_scanner_bridge` as a line in `sites/apps.txt` if not already listed.
5. Run `bench --site YOUR_SITE install-app document_scanner_bridge`, then `bench --site YOUR_SITE migrate` and restart the bench services in your normal deployment workflow.
6. Configure a real HTTPS origin with a trusted certificate. The Windows client does not disable TLS validation or follow redirects with credentials.

Target API design was checked against Frappe v15 source. Compatibility on the actual installed ERPNext/Frappe version, database, Redis lock behavior and file hooks still needs staging validation. Do not treat isolated contract tests as proof of a deployed site's behavior.

## Site/user permissions

Use an ERPNext API user with read access to the required document types, write access to the selected destination document, and normal File creation/attachment permissions. The bridge checks document and field permissions; it does not bypass File permissions. The private receipt DocType is maintained internally by the bridge and readable by System Manager; scanner users do not need direct CRUD permission on receipt records.

Default enabled types: Sales Order, Sales Invoice, Delivery Note, Purchase Order, Purchase Invoice. Optional site configuration `document_scanner_doctypes` can explicitly choose others. Optional `document_scanner_search_fields` maps a DocType to allowed field names. The bridge intersects configured fields with actual metadata and permitted readable fields; it always supports document name. Default search candidates include customer_name, supplier_name, customer, supplier, title, po_no, bill_no and remarks where present/permitted. No arbitrary field/SQL expression is accepted.

The application shows the bridge's actual supported types and fields. It does not claim unavailable fields are searchable.

## Windows setup

1. Open **Manage profiles → ERPNext → Manage sites / credentials**.
2. Create a site profile with a display name and HTTPS origin, e.g. `https://erp.example.com` (example only).
3. Enter the user's API key and secret. Both are saved in Windows Credential Manager for the current Windows account, separately from portable Config/Profile JSON. Moving an app folder/profile to another PC does not transfer secrets.
4. Test connection. A successful result shows the authenticated account and bridge version. Error responses do not establish connection success.
5. Select that site reference in the scanner profile, enter a default DocType and choose Private/Public attachment preference, then save the profile. Loading it never confirms an actual document.
6. Scan/import, select pages, choose the Save Action. Search in the app, read the real details, confirm destination, then explicitly **Send now** or **Save locally, then send**.

Exported scanner profiles contain only the site reference/default DocType/privacy, not URL credentials, API secrets or a confirmed document. Site names/URLs are separate application configuration; credentials remain in Windows storage. To remove retained secrets after deleting a site reference, use Windows Credential Manager and remove its `DocumentScanner/ERPNext/<site-id>` Generic Credential.

## Save and retry semantics

- Save Local exports selected pages in their current order to the chosen folder. No ERPNext account or confirmation is required.
- Send to ERPNext creates frozen artifacts inside the recoverable session, without requiring a permanent local destination.
- Save Local & Send renders once to a recoverable outbox, saves local copies first, then uploads those same frozen bytes. A local phase failure prevents upload; completed files survive and can be resumed without duplicate copies.
- Results report local paths and per-artifact upload state independently. Retry re-reads the document, requires fresh confirmation for the same session/site origin/account/DocType/name/privacy, and queries durable server receipts before re-sending. A changed local artifact hash is rejected.
- A timeout is shown as Unknown, not assumed Failed. If reconciliation cannot complete, no blind upload occurs. A successful receipt is checked against target, privacy and SHA-256 content.
- The bridge uses a per-user/key distributed lock, a database locking receipt read, a unique receipt name, and commits attachment+receipt before releasing the lock. Staging tests must cover concurrent retries, response loss, rollback hooks, record deletion and network interruption.
- Original images and session data are retained until the user discards that session. A new document gets a new session and no confirmed destination. Scan More appends to the same document.

The bridge accepts PDF/PNG/JPG content only, with a 20 MiB per-file ceiling and basic file signatures. Site/NGINX/Frappe limits can be lower; the client reports failure and retains work. Adjust server limits explicitly if needed. This is not a malware/content-scanning service.

## Acceptance before production use

Verify search by number and actual party names; field and document permissions; real details and identity changes; private/public attachments; source file integrity; local-only offline use; multi-file partial success; timeout after server commit; concurrent retry; restart/recovery; profile import without credentials; and unrelated custom File hooks. Record actual server versions and outcomes.

Reference contracts: [Frappe REST API](https://docs.frappe.io/framework/user/en/api/rest), [Database API](https://docs.frappe.io/framework/user/en/api/database), [permission-filtered document retrieval](https://github.com/frappe/frappe/blob/version-15/frappe/client.py), [field metadata permissions](https://github.com/frappe/frappe/blob/version-15/frappe/model/meta.py).
