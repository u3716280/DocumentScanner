using System.Net;
using System.Net.Http;
using System.Text;
using DocumentScanner.Core;
using DocumentScanner.Infrastructure;
using Xunit;
namespace DocumentScanner.UnitTests;
public sealed class FrappeClientTests
{
    private static async Task<ErpException> Fails(Func<Task> action) => await Assert.ThrowsAsync<ErpException>(action);

    [Fact] public async Task AuthenticationSuccessReturnsTheErpUser()
    {
        var erp = new FakeErp(); using var client = erp.Client();
        Assert.Equal("user@example.com", await client.GetLoggedUser(default));
        Assert.Equal(25L * 1024 * 1024, await client.GetMaxFileSize(default));
    }

    [Fact] public async Task AuthenticationPermissionAndMissingDocumentAreDistinctErrors()
    {
        var erp = new FakeErp { Authenticated = false }; using var client = erp.Client();
        Assert.Equal(ErpErrorKind.Authentication, (await Fails(() => client.GetLoggedUser(default))).Kind);
        erp.Authenticated = true; erp.CanRead = false;
        Assert.Equal(ErpErrorKind.Permission, (await Fails(() => client.GetDocument(erp.DocType, erp.DocName, "u", default))).Kind);
        erp.CanRead = true; erp.DocumentExists = false;
        var missing = await Fails(() => client.GetDocument(erp.DocType, erp.DocName, "u", default));
        Assert.Equal(ErpErrorKind.NotFound, missing.Kind); Assert.Equal(404, missing.Status);
        erp.User = "Guest"; Assert.Equal(ErpErrorKind.Authentication, (await Fails(() => client.GetLoggedUser(default))).Kind);
    }

    [Fact] public async Task DocumentFieldsAreReadAndMissingOnesAreEmpty()
    {
        var erp = new FakeErp { DocStatus = 1 }; using var client = erp.Client();
        var doc = await client.GetDocument("Sales Invoice", erp.DocName, "user@example.com", default);
        Assert.Equal("Example Manufacturing Co.", doc.Party); Assert.Equal("2026-11-16", doc.Date); Assert.Equal("330.50", doc.Total); Assert.Equal("THB", doc.Currency);
        Assert.Equal(1, doc.DocStatus); Assert.Equal("", doc.Reference); Assert.Equal(FakeErp.Origin, doc.SiteOrigin);
        Assert.Equal("https://erp.example.com/app/sales-invoice/ACC-INV-2611-0001", doc.DocumentUrl);

        erp.DocType = "Purchase Invoice"; erp.DocName = "ACC-PINV-2611-0042"; erp.Fields = new() { ["supplier"] = "SUP-9", ["bill_no"] = "IV6811-0123" };
        var bare = await client.GetDocument("Purchase Invoice", erp.DocName, "u", default);
        Assert.Equal("SUP-9", bare.Party); Assert.Equal("IV6811-0123", bare.Reference); Assert.Equal("", bare.Date); Assert.Equal("", bare.Total); Assert.Equal("", bare.Status);
    }

    [Fact] public async Task DocumentNamesAreEscapedIntoThePathAndNeverLeaveTheOrigin()
    {
        var erp = new FakeErp { DocType = "Purchase Receipt", DocName = "PR/2569 #7?x=1&y=../../File" }; using var client = erp.Client();
        var doc = await client.GetDocument(erp.DocType, erp.DocName, "u", default);
        Assert.Equal(erp.DocName, doc.DocName);
        Assert.Equal("GET /api/resource/Purchase Receipt/" + erp.DocName, erp.Requests.Single());
        await Assert.ThrowsAsync<ScanLinkException>(() => client.GetDocument("User", "Administrator", "u", default));
    }

    [Fact] public async Task AnswerForADifferentDocumentOrWithoutDocstatusIsInvalid()
    {
        var erp = new FakeErp(); using var client = erp.Client();
        erp.Intercept = r => r.RequestUri!.AbsolutePath.Contains("/api/resource/Sales") ? FakeErp.Json(new { data = new { name = "OTHER-1", docstatus = 0 } }) : null;
        Assert.Equal(ErpErrorKind.InvalidResponse, (await Fails(() => client.GetDocument(erp.DocType, erp.DocName, "u", default))).Kind);
        erp.Intercept = r => r.RequestUri!.AbsolutePath.Contains("/api/resource/Sales") ? FakeErp.Json(new { data = new { name = erp.DocName } }) : null;
        Assert.Equal(ErpErrorKind.InvalidResponse, (await Fails(() => client.GetDocument(erp.DocType, erp.DocName, "u", default))).Kind);
        erp.Intercept = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>login</html>") };
        Assert.Equal(ErpErrorKind.InvalidResponse, (await Fails(() => client.GetLoggedUser(default))).Kind);
    }

    [Fact] public async Task NetworkTimeoutCertificateAndRedirectAreDistinct()
    {
        var erp = new FakeErp(); using var client = erp.Client();
        erp.Intercept = _ => throw new HttpRequestException("no route to host");
        Assert.Equal(ErpErrorKind.Network, (await Fails(() => client.GetLoggedUser(default))).Kind);
        erp.Intercept = _ => throw new TaskCanceledException("timed out");
        Assert.Equal(ErpErrorKind.Timeout, (await Fails(() => client.GetLoggedUser(default))).Kind);
        erp.Intercept = _ => throw FakeErp.TlsFailure();
        Assert.Equal(ErpErrorKind.Certificate, (await Fails(() => client.GetLoggedUser(default))).Kind);
        erp.Intercept = _ => { var r = new HttpResponseMessage(HttpStatusCode.Found); r.Headers.Location = new("https://evil.example/api/method/x"); return r; };
        Assert.Equal(ErpErrorKind.Redirect, (await Fails(() => client.GetLoggedUser(default))).Kind);
        Assert.All(erp.Requests, r => Assert.DoesNotContain("evil", r));
        erp.Intercept = _ => FakeErp.Error(HttpStatusCode.InternalServerError, "");
        Assert.Equal(ErpErrorKind.Server, (await Fails(() => client.GetLoggedUser(default))).Kind);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); erp.Intercept = null;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetLoggedUser(cancelled.Token));
    }

    [Fact] public async Task UploadIsMultipartPrivateAndCarriesTheExactBytesAndUtf8Name()
    {
        var erp = new FakeErp { DocType = "Purchase Invoice", DocName = "ACC-PINV-2611-0042" }; using var client = erp.Client();
        string file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
        byte[] bytes = Enumerable.Range(0, 5000).Select(i => (byte)(i * 31)).ToArray(); File.WriteAllBytes(file, bytes);
        try
        {
            const string name = "PI_ACC-PINV-2611-0042_ใบกำกับภาษี.pdf";
            var result = await client.Upload(erp.DocType, erp.DocName, file, name, default);
            var upload = Assert.Single(erp.Uploads);
            Assert.Equal(name, upload.FileName); Assert.Equal(bytes, upload.Content); Assert.Equal(name, result.FileName); Assert.True(result.IsPrivate);
            Assert.Contains("name=is_private", upload.Fields.Replace("\"", "")); Assert.Matches("name=\"?is_private\"?\\r\\n\\r\\n1\\r\\n", upload.Fields);
            Assert.Matches("name=\"?doctype\"?\\r\\n\\r\\nPurchase Invoice\\r\\n", upload.Fields); Assert.Matches("name=\"?docname\"?\\r\\n\\r\\nACC-PINV-2611-0042\\r\\n", upload.Fields);
            Assert.DoesNotContain("base64", upload.Fields, StringComparison.OrdinalIgnoreCase);
            await Assert.ThrowsAsync<ArgumentException>(() => client.Upload(erp.DocType, erp.DocName, file, "..\\evil.pdf", default));
        }
        finally { File.Delete(file); }
    }

    [Fact] public async Task UploadSuccessIsNotAssumedFromHttpStatusAlone()
    {
        var erp = new FakeErp(); using var client = erp.Client();
        string file = Path.GetTempFileName();
        try
        {
            foreach (object body in new object[] { new { }, new { message = "ok" }, new { message = new { name = "FILE-1", file_name = "a.pdf", is_private = 1, attached_to_doctype = "Sales Invoice", attached_to_name = "OTHER" } },
                new { message = new { name = "FILE-1", file_name = "a.pdf", is_private = 0, attached_to_doctype = erp.DocType, attached_to_name = erp.DocName } } })
            {
                erp.Intercept = r => r.Method == HttpMethod.Post ? FakeErp.Json(body) : null;
                Assert.Equal(ErpErrorKind.InvalidResponse, (await Fails(() => client.Upload(erp.DocType, erp.DocName, file, "a.pdf", default))).Kind);
            }
            erp.Intercept = null; erp.OnUpload = (_, _) => FakeErp.UploadBehaviour.TooLarge;
            Assert.Equal(ErpErrorKind.FileTooLarge, (await Fails(() => client.Upload(erp.DocType, erp.DocName, file, "a.pdf", default))).Kind);
            erp.OnUpload = null; erp.CanWrite = false;
            Assert.Equal(ErpErrorKind.Permission, (await Fails(() => client.Upload(erp.DocType, erp.DocName, file, "a.pdf", default))).Kind);
        }
        finally { File.Delete(file); }
    }

    [Fact] public void InvalidSitesAndCredentialsAreRefusedBeforeAnyRequest()
    {
        Assert.Throws<ArgumentException>(() => new FrappeClient(new ErpSite { Url = "http://erp.example.com" }, FakeErp.Credential, new FakeErp()));
        Assert.Throws<ErpException>(() => new FrappeClient(FakeErp.Site, new("key:colon", "s"), new FakeErp()));
        Assert.Throws<ErpException>(() => new FrappeClient(FakeErp.Site, new("key", "sec\nret"), new FakeErp()));
    }

    [Fact] public void SecretsAreRedactedFromAnyText()
    {
        using var client = new FakeErp().Client();
        string text = client.Safe("GET https://erp.example.com/api/x?sid=abc123&q=1 failed. Authorization: token key123456:secret-abcdef\napi_secret=secret-abcdef password: hunter2 Cookie: sid=zzz {\"api_key\":\"key123456\"}");
        foreach (string leak in new[] { "secret-abcdef", "hunter2", "abc123", "zzz", "key123456:" }) Assert.DoesNotContain(leak, text);
        Assert.Contains("https://erp.example.com/api/x?[redacted]", text);
        Assert.Equal("Scanner ready on WIA", Redactor.Clean("Scanner ready on WIA"));
        Assert.Equal("", Redactor.Clean(null));
    }
}
