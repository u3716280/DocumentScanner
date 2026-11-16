using System.Net;
using System.Net.Http;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DocumentScanner.Core;
using DocumentScanner.Infrastructure;
namespace DocumentScanner.UnitTests;

/// <summary>
/// An in-memory stand-in for the standard Frappe REST endpoints this application uses. It keeps one document and
/// its attachments, and can be told to misbehave at a chosen upload. No network is involved.
/// </summary>
public sealed class FakeErp : HttpMessageHandler
{
    public const string Origin = "https://erp.example.com";
    public static readonly ErpSite Site = new() { Name = "Test", Url = Origin };
    public static readonly ErpCredential Credential = new("key123456", "secret-abcdef");

    public string User = "user@example.com";
    public string DocType = "Sales Invoice", DocName = "ACC-INV-2611-0001";
    public int DocStatus;
    public bool DocumentExists = true, CanRead = true, CanWrite = true, Authenticated = true;
    public long MaxFileSize = 25 * 1024 * 1024;
    public Dictionary<string, object?> Fields = new() { ["customer"] = "CUST-1", ["customer_name"] = "Example Manufacturing Co.", ["posting_date"] = "2026-11-16", ["grand_total"] = 330.5, ["currency"] = "THB", ["status"] = "Draft", ["modified"] = "2026-11-16 10:00:00.000000" };
    public List<(string Name, string FileName, string Hash, bool Private)> Files = [];
    public List<string> Requests = [];
    public List<(string FileName, byte[] Content, string Fields)> Uploads = [];
    /// <summary>Called for each upload with its 1-based number. Return a behaviour, or null for normal success.</summary>
    public Func<int, string, UploadBehaviour?>? OnUpload;
    public Func<HttpRequestMessage, HttpResponseMessage?>? Intercept;
    public Action? BeforeEachRequest;
    public enum UploadBehaviour { Succeed, StoreThenTimeout, TimeoutBeforeStore, ServerError, StoreThenGarbage, StoreUnderOtherName, TooLarge, Disconnect, PermissionDenied }

    public FrappeClient Client(ErpSite? site = null) => new(site ?? Site, Credential, this, TimeSpan.FromSeconds(30));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        BeforeEachRequest?.Invoke();
        string path = Uri.UnescapeDataString(request.RequestUri!.AbsolutePath);
        Requests.Add($"{request.Method} {path}");
        if (request.RequestUri.GetLeftPart(UriPartial.Authority) != Origin) throw new InvalidOperationException("Request left the configured origin.");
        if (Intercept?.Invoke(request) is { } custom) return custom;
        if (!Authenticated || request.Headers.Authorization?.Scheme != "token" || request.Headers.Authorization.Parameter != Credential.Key + ":" + Credential.Secret) return Error(HttpStatusCode.Unauthorized, "AuthenticationError");
        if (path == "/api/method/frappe.auth.get_logged_user") return Json(new { message = User });
        if (path == "/api/method/frappe.core.api.file.get_max_file_size") return Json(new { message = MaxFileSize });
        if (path == $"/api/resource/{DocType}/{DocName}" && request.Method == HttpMethod.Get)
        {
            if (!DocumentExists) return Error(HttpStatusCode.NotFound, "DoesNotExistError");
            if (!CanRead) return Error(HttpStatusCode.Forbidden, "PermissionError");
            var data = new Dictionary<string, object?>(Fields) { ["name"] = DocName, ["doctype"] = DocType, ["docstatus"] = DocStatus };
            return Json(new { data });
        }
        if (path.StartsWith("/api/resource/") && path != "/api/resource/File") return Error(HttpStatusCode.NotFound, "DoesNotExistError");
        if (path == "/api/resource/File")
            return Json(new { data = Files.Select(f => new { name = f.Name, file_name = f.FileName, file_url = "/private/files/" + f.FileName, is_private = f.Private ? 1 : 0, content_hash = f.Hash, file_size = 1 }) });
        if (path == "/api/method/upload_file" && request.Method == HttpMethod.Post)
        {
            byte[] raw = await request.Content!.ReadAsByteArrayAsync(ct);
            string text = Encoding.UTF8.GetString(raw);
            string fileName = Regex.Match(text, "name=\"file\"; filename=\"([^\"]*)\"").Groups[1].Value;
            string boundary = request.Content.Headers.ContentType!.Parameters.First(p => p.Name == "boundary").Value!.Trim('"');
            byte[] content = FilePart(raw, boundary);
            Uploads.Add((fileName, content, text[..Math.Min(text.Length, 2000)]));
            var behaviour = OnUpload?.Invoke(Uploads.Count, fileName) ?? UploadBehaviour.Succeed;
            if (!CanWrite || behaviour == UploadBehaviour.PermissionDenied) return Error(HttpStatusCode.Forbidden, "PermissionError");
            if (behaviour == UploadBehaviour.TimeoutBeforeStore) throw new TaskCanceledException("timeout");
            if (behaviour == UploadBehaviour.Disconnect) throw new HttpRequestException("connection reset");
            if (behaviour == UploadBehaviour.ServerError) return Error(HttpStatusCode.InternalServerError, "");
            if (behaviour == UploadBehaviour.TooLarge || content.Length > MaxFileSize) return Error(HttpStatusCode.ExpectationFailed, "MaxFileSizeReachedError");
            string stored = behaviour == UploadBehaviour.StoreUnderOtherName ? Path.GetFileNameWithoutExtension(fileName) + "a1b2c3" + Path.GetExtension(fileName) : fileName;
            string id = "FILE-" + (Files.Count + 1).ToString("000");
            Files.Add((id, stored, Convert.ToHexString(System.Security.Cryptography.MD5.HashData(content)).ToLowerInvariant(), true));
            if (behaviour == UploadBehaviour.StoreThenTimeout) throw new TaskCanceledException("timeout");
            if (behaviour == UploadBehaviour.StoreThenGarbage) return new(HttpStatusCode.OK) { Content = new StringContent("<html>proxy page</html>") };
            return Json(new { message = new { name = id, file_name = stored, file_url = "/private/files/" + stored, is_private = 1, attached_to_doctype = DocType, attached_to_name = DocName } });
        }
        return Error(HttpStatusCode.NotFound, "");
    }

    private static byte[] FilePart(byte[] raw, string boundary)
    {
        // Latin-1 keeps one char per byte, so offsets found in the text are byte offsets.
        string text = Encoding.Latin1.GetString(raw);
        int header = text.IndexOf("name=\"file\"", StringComparison.Ordinal);
        int start = text.IndexOf("\r\n\r\n", header, StringComparison.Ordinal) + 4;
        int end = text.IndexOf("\r\n--" + boundary, start, StringComparison.Ordinal);
        return raw[start..end];
    }
    public static HttpResponseMessage Json(object value, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
    public static HttpResponseMessage Error(HttpStatusCode status, string excType) => Json(new { exc_type = excType, exception = "frappe.exceptions." + excType }, status);
    public static HttpRequestException TlsFailure() => new("The SSL connection could not be established", new AuthenticationException("The remote certificate is invalid"));
}
