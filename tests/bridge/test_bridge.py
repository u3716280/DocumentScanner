"""Isolated bridge contract tests; no claim of a live Frappe database/Redis test."""
import base64
import hashlib
import importlib.util
from pathlib import Path
import sys
import types
import unittest
from contextlib import nullcontext
from unittest.mock import Mock

class Attr(dict):
    __getattr__ = dict.get

class BridgeTests(unittest.TestCase):
    def setUp(self):
        self.frappe = types.ModuleType('frappe')
        f = self.frappe
        f.session = Attr(user='user-a')
        f.conf = Attr()
        f.PermissionError = PermissionError
        def fail(message, exception=ValueError):
            raise exception(message)
        f.throw = fail
        f.whitelist = lambda **kwargs: lambda method: method
        f.db = Mock()
        f.db.sql.return_value = []
        f.cache = Mock()
        f.cache.lock.side_effect = lambda *a, **kw: nullcontext()
        self.target = Attr(modified='r1')
        self.target.check_permission = Mock()
        f.get_doc = Mock(return_value=self.target)
        client = types.ModuleType('frappe.client')
        client.get = Mock(return_value={'name': 'SO-1', 'modified': 'r1', 'customer_name': 'Customer'})
        sys.modules['frappe'] = f
        sys.modules['frappe.client'] = client
        path = Path(__file__).resolve().parents[2] / 'integrations/document_scanner_bridge/document_scanner_bridge/api.py'
        spec = importlib.util.spec_from_file_location('bridge_under_test', path)
        self.api = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(self.api)

    def test_guest_and_unlisted_type_are_denied(self):
        self.frappe.session.user = 'Guest'
        with self.assertRaises(PermissionError):
            self.api.ping()
        self.frappe.session.user = 'user-a'
        with self.assertRaises(PermissionError):
            self.api.document('User', 'Administrator')

    def test_document_revision_and_write_permission_checked(self):
        with self.assertRaises(ValueError):
            self.api._target('Sales Order', 'SO-1', 'changed', write=True)
        self.target.check_permission.assert_any_call('read')
        self.target.check_permission.assert_any_call('write')

    def test_operation_binding_is_scoped_to_authenticated_user(self):
        key = 'a' * 32
        first, binding = self.api._binding(key, 'Sales Order', 'SO-1', '1', 'b' * 64)
        self.frappe.session.user = 'user-b'
        second, _ = self.api._binding(key, 'Sales Order', 'SO-1', '1', 'b' * 64)
        self.assertNotEqual(first, second)
        self.assertEqual(binding['upload_user'], 'user-a')
        with self.assertRaises(ValueError):
            self.api._binding(key, 'Sales Order', 'SO-1', 2, 'b' * 64)

    def test_receipt_mismatch_never_reuses_another_destination(self):
        self.frappe.db.sql.return_value = [Attr(upload_user='user-a', target_doctype='Sales Order', target_name='SO-other', private_attachment=1, sha256='b'*64, file_id='file')]
        with self.assertRaises(PermissionError):
            self.api._existing('id', dict(upload_user='user-a', target_doctype='Sales Order', target_name='SO-1', private_attachment=1, sha256='b'*64))

    def test_matching_receipt_checks_actual_file_binding_and_content(self):
        data = b'%PDF-example'
        h = hashlib.sha256(data).hexdigest()
        binding = dict(upload_user='user-a', target_doctype='Sales Order', target_name='SO-1', private_attachment=1, sha256=h)
        self.frappe.db.sql.return_value = [Attr(**binding, file_id='FILE-1')]
        file = Attr(name='FILE-1', attached_to_doctype='Sales Order', attached_to_name='SO-1', is_private=1)
        file.check_permission = Mock()
        file.get_content = Mock(return_value=data)
        self.frappe.get_doc.return_value = file
        self.assertEqual(self.api._existing('id', binding), {'FileId': 'FILE-1'})
        file.check_permission.assert_called_with('read')
        self.assertIn('for update', self.frappe.db.sql.call_args.args[0])
        file.is_private = 0
        with self.assertRaises(ValueError):
            self.api._existing('id', binding)

    def test_bad_hash_type_and_path_rejected_before_insert(self):
        data = b'%PDF-example'
        args = dict(key='a'*32, doctype='Sales Order', name='SO-1', private=1, revision='r1', filename='../bad.pdf', content=base64.b64encode(data).decode(), sha256=hashlib.sha256(data).hexdigest())
        with self.assertRaises(ValueError):
            self.api.upload(**args)
        args['filename'] = 'good.pdf'
        args['sha256'] = 'b'*64
        with self.assertRaises(ValueError):
            self.api.upload(**args)
        self.frappe.db.commit.assert_not_called()

    def test_existing_upload_returns_durable_receipt_without_inserting(self):
        data = b'%PDF-example'
        self.api._existing = Mock(return_value={'FileId': 'FILE-1'})
        result = self.api.upload(key='a'*32, doctype='Sales Order', name='SO-1', private=1, revision='r1', filename='scan.pdf', content=base64.b64encode(data).decode(), sha256=hashlib.sha256(data).hexdigest())
        self.assertEqual(result, {'FileId': 'FILE-1'})
        self.frappe.db.commit.assert_not_called()

if __name__ == '__main__':
    unittest.main()
