# Scanning from ERPNext

Document Scanner 2.1 is started from ERPNext. Open a Sales Order, Sales Invoice, Purchase Receipt or Purchase Invoice, click
**Scan with DocumentScanner**, and the application opens with that document as its fixed destination.

It uses the standard Frappe REST API only. Nothing is installed on the ERPNext server except four Client Scripts.
The Bridge app used by 1.x is no longer used.

Status: automated tests passed, followed by user-reported manual acceptance on 18 November 2026 with a live ERPNext v15 site, Chrome, Edge, HP Smart Tank 615 and both portable architectures. The completed checklist below records that acceptance.

## 1. Install the Client Scripts (ERPNext administrator, once per site)

The scripts are in `erpnext/client-scripts/`: `sales_order.js`, `sales_invoice.js`, `purchase_receipt.js`, `purchase_invoice.js`.

For each file:

1. In ERPNext open **Client Script** and create a new one.
2. Set **DocType** to the matching DocType and **Apply To** to **Form**.
3. Paste the file's content into **Script**, tick **Enabled**, and save.

The button appears on saved documents that are Draft or Submitted. It is not shown on cancelled documents, and on
an unsaved document it asks you to save first. No ERPNext core file is changed and no custom app is needed.

The link the button opens contains exactly three values: the site address, the DocType and the document name.

    documentscanner://scan?site=https%3A%2F%2Ferp.example.com&doctype=Sales%20Invoice&docname=ACC-INV-2611-0001

It never contains a password, API key, API secret, session cookie or token.

## 2. Set up each computer (every user, once)

1. Extract the portable ZIP anywhere you can write to and start `DocumentScanner.exe`. Administrator rights are
   not needed.
2. Open **Settings > Scan link** and click **Register ERPNext scan link**. This writes
   `HKEY_CURRENT_USER\Software\Classes\documentscanner` for your Windows account only.
3. Click **Test scan link**. A message confirms that Windows opens this copy.
4. If you move the folder later, open Settings again: it shows that the registered path no longer matches, and
   **Register** fixes it. **Remove registration** deletes the entry.

The first time you click the button in ERPNext, Chrome or Edge asks whether to open DocumentScanner. Allow it.

## 3. Add your ERPNext site and API key (every user, once)

Each person uses their own ERPNext account, so ERPNext applies that person's permissions.

1. In ERPNext open your **User** record, **Settings > API Access**, and click **Generate Keys**. Copy the API Secret
   when it is shown; ERPNext shows it only once.
2. In DocumentScanner open **Settings > ERPNext**, click **New site**, and enter a name, the site address
   (`https://…` only, no path), the API key and the API secret. Click **Save site**.
3. Click **Test connection**. It shows the ERPNext user you are connected as and the largest file the site accepts.

The key and secret are stored in Windows Credential Manager for your Windows account. They are not written to the
settings file, the application folder, profiles, links or logs, and they do not travel with the portable folder.

Scan links are accepted only from sites in this list. Scheme, host and port must match.

Attaching a file needs **write** permission on the document in ERPNext; read permission alone is not enough.

## 4. Scan and save

1. In ERPNext click **Scan with DocumentScanner**. The application reads the document and shows it under
   **ERPNext destination**: type, number, customer or supplier, date, status and total. It cannot be changed here.
2. Choose a scanner profile and scan. Rotate, crop, adjust, delete and reorder pages as usual.
3. Tick the pages to include. Nothing is selected for you.
4. Choose PDF (one file with all ticked pages), JPG or PNG (one file per page).
5. Click one of the three buttons.

| Button | What it does |
|---|---|
| **Send to ERPNext** | Attaches the files to the document as private attachments. No folder is asked for. A temporary copy is kept inside the scan session until ERPNext confirms each file, so a failed send can be retried without scanning again. |
| **Save to folder** | Saves to a folder you choose. Nothing is sent. |
| **Save and send** | Saves every file to the folder first. Only when all are saved does it send the same files to ERPNext. |

Before anything is sent you see a confirmation with the site, document, customer or supplier, your ERPNext user,
the number of pages and files, the final file names and "Attachment: Private".

The result window reports the folder and ERPNext separately, file by file. It says "Completed" only when every
file reached every place it was meant for. A file whose result is not known is shown with "?" and is never reported
as sent.

If you start DocumentScanner yourself instead of from ERPNext, only **Save to folder** is available. Scanning and
saving to a folder work without a network.

### File names

| Type | Pattern | Example |
|---|---|---|
| PDF | `{TYPE}_{DOCUMENT-NO}.pdf` | `SI_ACC-INV-2611-0001.pdf` |
| Image | `{TYPE}_{DOCUMENT-NO}_P{page}.{ext}` | `SI_ACC-INV-2611-0001_P001.jpg` |
| With description | `{TYPE}_{DOCUMENT-NO}_{description}` | `PI_ACC-PINV-2611-0042_ใบกำกับภาษี.pdf` |

`SO` = Sales Order, `SI` = Sales Invoice, `PR` = Purchase Receipt, `PI` = Purchase Invoice. The document number always comes from
ERPNext and cannot be typed. The optional description is picked from a list: the supplier's own number on the
document (customer purchase order, supplier invoice number, supplier delivery note) and the suggestions under **Settings > File names**. You
can also type one. Characters Windows does not allow are replaced with `-`.

Nothing is overwritten. If the name is already used in the folder or on the ERPNext document, a number is added
(`_001`, `_002`) and you are shown the final name before anything is written. For **Save and send** the number is
chosen so the same name is free in both places.

### Retry

If sending fails, the result window offers **Retry**, and so does the main window after a restart. A retry reads the
document again, checks that it is not cancelled, checks which files are already attached, and sends only what is
missing, using the same prepared files. Folder files that were already saved are not written again.

### Cancelled documents

Draft and submitted documents accept scans. A cancelled document does not: nothing is sent, the reason is shown, the
scanned pages are kept, and **Save to folder** still works. The status is checked when the link arrives, before the
confirmation, before each file is sent and before a retry.

## Upgrading from 1.x

- ERPNext sites, stored API keys and the ERPNext settings inside scanner profiles from 1.x are removed on first
  start. A copy of the old settings file is kept next to it (`settings.json.before-2.0-…`). Add your site again in
  Settings.
- Scanner profiles (device, source, DPI, colour, output folder, prefix) are kept unchanged.
- An unfinished Bridge delivery from 1.x cannot be retried and is discarded. Its scanned pages stay in the session.
- The Bridge app on the ERPNext server is no longer used by this application.
- Both win-x64 and win-x86 packages are provided. The x64 package uses the bundled 32-bit worker for 32-bit TWAIN drivers and can also enumerate 64-bit TWAIN sources; the x86 package supports 32-bit TWAIN sources.

## Known limitations

- **No exactly-once guarantee.** The standard API has no operation id. After a timeout the application checks the
  document's attachments for the same content before sending again. This makes a duplicate unlikely but cannot rule
  it out.
- **Cancellation race.** Checking the status and uploading are two separate requests. A document cancelled in the
  moment between them can still receive the file. ERPNext itself does not refuse attachments on a cancelled
  document. Closing this gap needs a server-side endpoint, which this version deliberately does not add.
- **Name check depends on your permissions.** ERPNext shows a user only the attachments that user may see. If a
  name is taken by a file you cannot see, ERPNext stores the upload under a slightly different name; the result
  window then shows the name it was stored as.
- **Same content is not attached twice.** A file whose content is already attached privately to the document is
  treated as delivered.
- **Large PDFs.** PDF pages are stored without lossy compression. A long colour document can exceed the site's
  upload limit; it is then refused before upload and can still be saved to a folder.
- **HTTPS only.** Links from an `http://` address are refused, including addresses inside the office network.
- The browser's "Open DocumentScanner?" question and any company policy that blocks custom links are outside the
  application's control.

## Security notes

- The link is treated as untrusted input: only the `scan` action, exactly three parameters, strict decoding, an
  allow-list of four DocTypes and an allow-list of sites. Request paths are built by the application; a link
  cannot name an endpoint.
- Redirects from the ERPNext site are refused and TLS certificate checks are never relaxed.
- A second copy of the application passes its link to the running one through a named pipe that only the same
  Windows user can open. The receiver validates it like any other link.
- Logs record time, site host, DocType, document name, HTTP status and error category. Secrets, authorization
  headers, cookies and file contents are not logged, and log text passes through a redaction filter.
- The destination and the delivery record stored with a scan session contain no credentials.

## Manual test checklist

Completed and reported as passed by the project owner on 18 November 2026. Checkmarks record the tested installation; they are not a universal hardware or policy certification.

Scan link
- [x] Chrome opens `documentscanner://` and asks for confirmation the first time
- [x] Edge does the same
- [x] Register, Test and Remove in Settings behave as described
- [x] After moving the portable folder, Settings reports the mismatch and Register fixes it
- [x] Link while the application is closed: it starts with the destination shown
- [x] Link while the application is open: the running window comes forward, no second window
- [x] Link while pages are already scanned: the three-way question appears and each answer does what it says
- [x] Link from a site not in Settings, and for another DocType, is refused with a clear message

ERPNext v15 (use a draft document you choose)
- [x] Sales Order, Sales Invoice, Purchase Receipt and Purchase Invoice each show the correct details
- [x] Send to ERPNext: file appears on the document as a private attachment with the expected name
- [x] Save and send: folder file and attachment have the same name
- [x] Sending the same scan twice does not attach it twice
- [x] A name already used on the document gets `_001`
- [x] A submitted document accepts the attachment
- [x] A user without write permission gets the permission message and nothing is attached
- [x] Cancel the document after scanning: sending is refused, Save to folder still works
- [x] Disconnect the network during an upload, reconnect, Retry: no duplicate
- [x] Close the application with a failed send, reopen, recover the session, Retry

Scanner (HP Smart Tank 615, TWAIN)
- [x] Device is found through TWAIN and scans from the flatbed
- [x] Automatic feeder, if this unit has one
- [x] Multi-page PDF, and JPG/PNG one file per page
- [x] File size of a typical colour document against the site's upload limit

## Build

On Windows with the .NET SDK from `global.json`:

    dotnet build DocumentScanner.sln -c Release
    dotnet test tests/DocumentScanner.UnitTests -c Release
    dotnet test tests/DocumentScanner.IntegrationTests -c Release
    ./build/Publish.ps1 -Runtime win-x64
    ./build/Publish.ps1 -Runtime win-x86

The package and its SHA-256 file are written to `artifacts/`. Third-party notices are collected into the package's
`Licenses` folder; see `docs/Third-Party.md`.
