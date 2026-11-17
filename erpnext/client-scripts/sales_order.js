// DocumentScanner scan link for Sales Order (ERPNext v15 Client Script, view: Form).
//
// Adds "Scan with DocumentScanner". It opens documentscanner://scan with exactly three
// values: this site's address, the DocType and the document name. No password, API key,
// session cookie or token is ever put in the link. DocumentScanner reads the document
// itself, with the API key of the person using it, before anything is attached.
frappe.ui.form.on('Sales Order', {
    refresh(frm) {
        // Cancelled documents do not accept scans.
        if (frm.doc.docstatus === 2) return;

        frm.add_custom_button(__('Scan with DocumentScanner'), () => {
            if (frm.is_new()) {
                frappe.msgprint({
                    title: __('Save first'),
                    indicator: 'orange',
                    message: __('Save this document, then click Scan with DocumentScanner again. A scan is attached to a saved document.'),
                });
                return;
            }
            if (window.location.protocol !== 'https:') {
                frappe.msgprint({
                    title: __('Open ERPNext over HTTPS'),
                    indicator: 'orange',
                    message: __('DocumentScanner only accepts scan links from an https:// address. Open this site through its HTTPS address and try again.'),
                });
                return;
            }
            const link = 'documentscanner://scan'
                + '?site=' + encodeURIComponent(window.location.origin)
                + '&doctype=' + encodeURIComponent(frm.doctype)
                + '&docname=' + encodeURIComponent(frm.doc.name);
            frappe.show_alert({
                message: __('Opening DocumentScanner. If nothing happens, open DocumentScanner, go to Settings > Scan link and register it on this computer.'),
                indicator: 'blue',
            }, 9);
            window.location.href = link;
        });
    },
});
