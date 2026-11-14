"""Frappe v15 bridge. All requests require an authenticated user and document permissions.
No deployment is performed by the Windows build. See docs/ERPNext-Setup.md.
"""
import base64
import hashlib
import re
from pathlib import PurePath
import frappe
from frappe.client import get as permitted_document

DEFAULT_TYPES = ("Sales Order", "Sales Invoice", "Delivery Note", "Purchase Order", "Purchase Invoice")
FIELDS = ("customer_name", "supplier_name", "customer", "supplier", "title", "po_no", "bill_no", "remarks")

def _user():
    if frappe.session.user == "Guest":
        frappe.throw("Sign in required", frappe.PermissionError)
    return frappe.session.user

def _types():
    return tuple(frappe.conf.get("document_scanner_doctypes") or DEFAULT_TYPES)

def _target(doctype, name, revision=None, write=False):
    _user()
    if doctype not in _types():
        frappe.throw("DocType is not enabled for Document Scanner", frappe.PermissionError)
    doc = frappe.get_doc(doctype, name)
    doc.check_permission("read")
    if write:
        doc.check_permission("write")
    if revision and str(doc.modified) != revision:
        frappe.throw("Document changed; review and confirm it again")
    return doc

def _summary(doctype, data):
    return dict(DocType=doctype, Name=data['name'], Party=str(data.get('customer_name') or data.get('supplier_name') or data.get('customer') or data.get('supplier') or ''),
                Description=str(data.get('title') or data.get('remarks') or data.get('po_no') or data.get('bill_no') or '')[:1000],
                Company=str(data.get('company') or ''), Date=str(data.get('transaction_date') or data.get('posting_date') or ''),
                Status=str(data.get('status') or data.get('docstatus', '')), Total=f"{data.get('currency') or ''} {data.get('grand_total', '')}".strip(), Revision=str(data.get('modified') or ''))

@frappe.whitelist(methods=['POST'])
def ping():
    return dict(Account=_user(), Version='1.1.0', DocTypes=[t for t in _types() if frappe.has_permission(t, 'read')])

@frappe.whitelist(methods=['POST'])
def search_documents(doctype, query='', field='All', start=0):
    _user()
    if doctype not in _types() or not frappe.has_permission(doctype, 'read'):
        frappe.throw('DocType access denied', frappe.PermissionError)
    meta = frappe.get_meta(doctype)
    permitted = set(meta.get_permitted_fieldnames(user=frappe.session.user)) | {'name', 'modified', 'docstatus'}
    configured = frappe.conf.get('document_scanner_search_fields', {}).get(doctype, FIELDS)
    fields = ['name'] + [f for f in configured if meta.has_field(f) and f in permitted and re.fullmatch(r'[a-z_][a-z0-9_]*', f)]
    if field != 'All' and field not in fields:
        frappe.throw('This search field is not available')
    query = str(query).strip()[:150]
    offset = max(0, int(start))
    # Escape LIKE wildcards; names, display names and relevant allowed fields are distinct searchable fields.
    value = '%' + query.replace('\\', '\\\\').replace('%', '\\%').replace('_', '\\_') + '%'
    selected = fields if field == 'All' else [field]
    rows = frappe.get_list(doctype, fields=['name'], or_filters=[[doctype, f, 'like', value] for f in selected] if query else None,
                           order_by='modified desc', start=offset, page_length=21)
    results = []
    for row in rows[:20]:
        # Frappe client.get applies both document and field-level read permissions.
        results.append(_summary(doctype, permitted_document(doctype, row.name)))
    return dict(Rows=results, More=len(rows)>20, SearchFields=['All']+fields)

@frappe.whitelist(methods=['POST'])
def document(doctype, name):
    _target(doctype, name)
    return _summary(doctype, permitted_document(doctype, name))

def _binding(key, doctype, name, private, sha256):
    user = _user()
    if not re.fullmatch(r'[a-f0-9]{32}', key) or not re.fullmatch(r'[a-f0-9]{64}', sha256):
        frappe.throw('Invalid operation identity')
    private = int(private)
    if private not in (0, 1):
        frappe.throw('Invalid attachment visibility')
    operation = hashlib.sha256((user + ':' + key).encode()).hexdigest()
    return operation, dict(upload_user=user, target_doctype=doctype, target_name=name, private_attachment=private, sha256=sha256)

def _existing(operation, binding):
    # Locking read sees the latest committed receipt, including under repeatable-read isolation.
    rows = frappe.db.sql('select upload_user, target_doctype, target_name, private_attachment, sha256, file_id from `tabDocument Scanner Upload` where name=%s for update', (operation,), as_dict=True)
    if not rows:
        return dict(FileId=None)
    row = rows[0]
    if any(str(row.get(k)) != str(v) for k, v in binding.items()):
        frappe.throw('Retry identity does not match its original destination/content', frappe.PermissionError)
    file = frappe.get_doc('File', row.file_id)
    file.check_permission('read')
    if file.attached_to_doctype != binding['target_doctype'] or file.attached_to_name != binding['target_name'] or int(file.is_private) != binding['private_attachment']:
        frappe.throw('Attachment receipt has changed; administrator review required')
    if hashlib.sha256(file.get_content()).hexdigest() != binding['sha256']:
        frappe.throw('Attachment content has changed; administrator review required')
    return dict(FileId=file.name)

@frappe.whitelist(methods=['POST'])
def receipt(key, doctype, name, private, sha256, revision):
    _target(doctype, name, revision, write=True)
    operation, binding = _binding(key, doctype, name, private, sha256)
    with frappe.cache.lock('scanner-upload:' + operation, timeout=300, blocking_timeout=120):
        return _existing(operation, binding)

@frappe.whitelist(methods=['POST'])
def upload(key, doctype, name, private, sha256, revision, filename, content):
    _target(doctype, name, revision, write=True)
    operation, binding = _binding(key, doctype, name, private, sha256)
    if len(content) > 28 * 1024 * 1024:
        frappe.throw('Attachment exceeds 20 MB limit')
    data = base64.b64decode(content, validate=True)
    if len(data) > 20 * 1024 * 1024 or hashlib.sha256(data).hexdigest() != sha256:
        frappe.throw('Invalid attachment content/hash')
    if not filename or len(filename)>180 or filename != PurePath(filename).name or re.search(r'[\\/\x00-\x1f]', filename):
        frappe.throw('Invalid attachment filename')
    extension = filename.rsplit('.', 1)[-1].lower()
    valid = extension == 'pdf' and data.startswith(b'%PDF-') or extension == 'png' and data.startswith(b'\x89PNG\r\n\x1a\n') or extension in ('jpg', 'jpeg') and data.startswith(b'\xff\xd8\xff')
    if not valid:
        frappe.throw('Only PDF, PNG and JPEG scanner artifacts are accepted')
    with frappe.cache.lock('scanner-upload:' + operation, timeout=300, blocking_timeout=120):
        previous = _existing(operation, binding)
        if previous['FileId']:
            return previous
        _target(doctype, name, revision, write=True)
        file = frappe.get_doc(dict(doctype='File', file_name=filename, content=data, is_private=int(private), attached_to_doctype=doctype, attached_to_name=name))
        file.insert()  # Normal File permissions and attachment validations still apply.
        frappe.get_doc(dict(doctype='Document Scanner Upload', name=operation, file_id=file.name, **binding)).insert(ignore_permissions=True)
        # Commit receipt + attachment before releasing the distributed lock so reconciliation sees a durable result.
        frappe.db.commit()
        return dict(FileId=file.name)
