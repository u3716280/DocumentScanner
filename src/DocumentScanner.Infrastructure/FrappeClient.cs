using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DocumentScanner.Core;
namespace DocumentScanner.Infrastructure;

public sealed record ErpCredential(string Key, string Secret);

/// <summary>Why an ERPNext request did not succeed. Each kind is shown to the user with its own message.</summary>
public enum ErpErrorKind { Authentication, Permission, NotFound, Validation, FileTooLarge, Network, Timeout, Certificate, Redirect, Server, InvalidResponse }

public sealed class ErpException(ErpErrorKind kind, string message, int? status = null, Exception? inner = null) : Exception(message, inner)
{
    public ErpErrorKind Kind { get; } = kind;
    public int? Status { get; } = status;
}

/// <summary>A File record attached to an ERPNext document.</summary>
public sealed record ErpAttachment(string Name, string FileName, string FileUrl, bool IsPrivate, string ContentHash, long Size);

/// <summary>Removes credentials from text before it is logged or shown.</summary>
public static partial class Redactor
{
    [GeneratedRegex(@"(?i)\b(token|basic|bearer)\s+[^\s""',;]+")] private static partial Regex Scheme();
    [GeneratedRegex(@"(?i)\b(authorization|cookie|set-cookie)\s*[:=]\s*[^\r\n]+")] private static partial Regex Header();
    [GeneratedRegex(@"(?i)\b(api[_-]?key|api[_-]?secret|password|pwd|sid|access[_-]?token|secret)\b(\s*[""']?\s*[:=]\s*[""']?)[^\s""'&,;}]+")] private static partial Regex Pair();
    [GeneratedRegex(@"(?i)(https?://[^\s?#""']+)\?[^\s""']*")] private static partial Regex Query();

    public static string Clean(string? text, params string?[] secrets)
    {
        if (string.IsNullOrEmpty(text)) return "";
        foreach (string? secret in secrets) if (!string.IsNullOrEmpty(secret) && secret.Length >= 4) text = text.Replace(secret, "[redacted]", StringComparison.Ordinal);
        text = Header().Replace(text, m => m.Groups[1].Value + ": [redacted]");
        text = Scheme().Replace(text, m => m.Groups[1].Value + " [redacted]");
        text = Pair().Replace(text, m => m.Groups[1].Value + m.Groups[2].Value + "[redacted]");
        return Query().Replace(text, m => m.Groups[1].Value + "?[redacted]");
    }
}

/// <summary>
/// Talks to ERPNext through the standard Frappe REST API only. Every path is built here from validated values;
/// nothing from a scan link is used as a URL. Redirects are refused, and TLS validation is never relaxed.
/// </summary>
public sealed class FrappeClient : IDisposable
{
    private readonly HttpClient http;
    private readonly string secret;
    public ErpSite Site { get; }
    public string Origin { get; }

    public FrappeClient(ErpSite site, ErpCredential credential, HttpMessageHandler? handler = null, TimeSpan? timeout = null)
    {
        site.Validate();
        if (string.IsNullOrWhiteSpace(credential.Key) || string.IsNullOrWhiteSpace(credential.Secret) || credential.Key.Contains(':') || (credential.Key + credential.Secret).Any(c => char.IsControl(c) || char.IsWhiteSpace(c)))
            throw new ErpException(ErpErrorKind.Authentication, "The API key or secret is not valid. Enter them again in Settings.");
        Site = site; Origin = SiteOrigins.Normalize(site.Url); secret = credential.Secret;
        // An injected handler belongs to the caller and is not disposed with this client.
        http = new(handler ?? new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }, handler == null) { BaseAddress = new Uri(Origin + "/"), Timeout = timeout ?? TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("token", credential.Key + ":" + credential.Secret);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <summary>The ERPNext user these credentials belong to.</summary>
    public async Task<string> GetLoggedUser(CancellationToken ct)
    {
        using var result = await Send(() => new HttpRequestMessage(HttpMethod.Get, "api/method/frappe.auth.get_logged_user"), ct);
        string? user = result.RootElement.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String ? message.GetString() : null;
        if (string.IsNullOrWhiteSpace(user) || user == "Guest") throw new ErpException(ErpErrorKind.Authentication, "ERPNext did not accept the API key and secret.");
        return user;
    }

    /// <summary>The site's upload limit in bytes.</summary>
    public async Task<long> GetMaxFileSize(CancellationToken ct)
    {
        using var result = await Send(() => new HttpRequestMessage(HttpMethod.Get, "api/method/frappe.core.api.file.get_max_file_size"), ct);
        if (result.RootElement.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.Number && message.TryGetInt64(out long bytes) && bytes > 0) return bytes;
        throw new ErpException(ErpErrorKind.InvalidResponse, "ERPNext did not report its file size limit.");
    }

    /// <summary>Reads the document with this user's own permissions. Missing optional fields come back empty.</summary>
    public async Task<ErpDestination> GetDocument(string doctype, string docname, string account, CancellationToken ct)
    {
        var info = ErpDocTypes.Require(doctype);
        using var result = await Send(() => new HttpRequestMessage(HttpMethod.Get, $"api/resource/{Uri.EscapeDataString(info.DocType)}/{Uri.EscapeDataString(docname)}"), ct);
        if (!result.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object) throw Invalid();
        // Never trust a response for a different document than the one asked for.
        if (Text(data, "name") != docname || Text(data, "doctype") is { Length: > 0 } returned && returned != info.DocType) throw Invalid();
        if (!data.TryGetProperty("docstatus", out var status) || status.ValueKind != JsonValueKind.Number || !status.TryGetInt32(out int docstatus) || docstatus is < 0 or > 2) throw Invalid();
        string party = Text(data, info.PartyNameField); if (party.Length == 0) party = Text(data, info.PartyField);
        return new()
        {
            SiteId = Site.Id, SiteOrigin = Origin, Account = account, DocType = info.DocType, DocName = docname, Party = party,
            Date = Text(data, info.DateField), Reference = info.ReferenceField == null ? "" : Text(data, info.ReferenceField),
            Total = info.HasTotal ? Amount(data, "grand_total") : "", Currency = info.HasTotal ? Text(data, "currency") : "",
            Status = Text(data, "status"), DocStatus = docstatus, Modified = Text(data, "modified"),
        };
        static ErpException Invalid() => new(ErpErrorKind.InvalidResponse, "ERPNext returned an unexpected answer for this document.");
    }

    /// <summary>Files attached to the document that this user is allowed to see.</summary>
    public async Task<IReadOnlyList<ErpAttachment>> ListAttachments(string doctype, string docname, CancellationToken ct)
    {
        var info = ErpDocTypes.Require(doctype);
        string filters = JsonSerializer.Serialize(new object[] { new object[] { "attached_to_doctype", "=", info.DocType }, new object[] { "attached_to_name", "=", docname } });
        const string fields = "[\"name\",\"file_name\",\"file_url\",\"is_private\",\"content_hash\",\"file_size\"]";
        string path = $"api/resource/File?filters={Uri.EscapeDataString(filters)}&fields={Uri.EscapeDataString(fields)}&limit_page_length=1000";
        using var result = await Send(() => new HttpRequestMessage(HttpMethod.Get, path), ct);
        if (!result.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) throw new ErpException(ErpErrorKind.InvalidResponse, "ERPNext returned an unexpected attachment list.");
        return data.EnumerateArray().Where(row => row.ValueKind == JsonValueKind.Object).Select(Attachment).Where(a => a.Name.Length > 0).ToList();
    }

    /// <summary>
    /// Attaches one file as a private attachment. The answer is checked against the request: a 200 without a
    /// File record for this exact document is not treated as success.
    /// </summary>
    public async Task<ErpAttachment> Upload(string doctype, string docname, string file, string fileName, CancellationToken ct)
    {
        var info = ErpDocTypes.Require(doctype);
        FilenameRules.Validate(fileName);
        using var result = await Send(() =>
        {
            var body = new MultipartFormDataContent("----DocumentScanner" + Guid.NewGuid().ToString("N"));
            // Browsers send the file name as raw UTF-8, and that is what Frappe's parser expects.
            body.HeaderEncodingSelector = (_, _) => Encoding.UTF8;
            body.Add(new StringContent("1"), "is_private");
            body.Add(new StringContent(info.DocType, Encoding.UTF8), "doctype");
            body.Add(new StringContent(docname, Encoding.UTF8), "docname");
            body.Add(new StringContent("Home"), "folder");
            var content = new StreamContent(new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true));
            content.Headers.ContentType = new MediaTypeHeaderValue(MediaType(fileName));
            content.Headers.TryAddWithoutValidation("Content-Disposition", $"form-data; name=\"file\"; filename=\"{fileName.Replace("\"", "")}\"");
            body.Add(content);
            return new HttpRequestMessage(HttpMethod.Post, "api/method/upload_file") { Content = body };
        }, ct);
        if (!result.RootElement.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object) throw Incomplete();
        var attachment = Attachment(message);
        if (attachment.Name.Length == 0 || attachment.FileName.Length == 0 || Text(message, "attached_to_doctype") != info.DocType || Text(message, "attached_to_name") != docname || !attachment.IsPrivate) throw Incomplete();
        return attachment;
        static ErpException Incomplete() => new(ErpErrorKind.InvalidResponse, "ERPNext answered, but did not confirm a private attachment on this document.");
    }

    private async Task<JsonDocument> Send(Func<HttpRequestMessage> build, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        HttpResponseMessage response;
        try
        {
            using var request = build();
            response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (OperationCanceledException e) { throw new ErpException(ErpErrorKind.Timeout, "ERPNext did not answer in time.", inner: e); }
        catch (HttpRequestException e) when (e.InnerException is AuthenticationException || e.HttpRequestError == HttpRequestError.SecureConnectionError)
        { throw new ErpException(ErpErrorKind.Certificate, "The secure connection to ERPNext could not be verified. Nothing was sent.", inner: e); }
        catch (HttpRequestException e) { throw new ErpException(ErpErrorKind.Network, "ERPNext could not be reached. Check the network connection.", inner: e); }
        catch (IOException e) { throw new ErpException(ErpErrorKind.Network, "The connection to ERPNext was interrupted.", inner: e); }
        using (response)
        {
            int status = (int)response.StatusCode;
            string body;
            try { body = await response.Content.ReadAsStringAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception e) when (e is HttpRequestException or IOException or OperationCanceledException) { throw new ErpException(ErpErrorKind.Network, "The answer from ERPNext was cut off.", status, e); }
            if (status is >= 300 and < 400) throw new ErpException(ErpErrorKind.Redirect, "The ERPNext site redirected the request to another address. Nothing was sent; check the site address in Settings.", status);
            JsonDocument? json = null;
            try { if (body.Length > 0) json = JsonDocument.Parse(body); } catch (JsonException) { }
            if (response.IsSuccessStatusCode)
            {
                if (json == null || json.RootElement.ValueKind != JsonValueKind.Object) { json?.Dispose(); throw new ErpException(ErpErrorKind.InvalidResponse, "ERPNext returned an answer this application cannot read.", status); }
                return json;
            }
            using (json) throw Classify(response.StatusCode, json == null ? "" : json.RootElement.ValueKind == JsonValueKind.Object ? Text(json.RootElement, "exc_type") : "");
        }
    }

    internal static ErpException Classify(HttpStatusCode code, string excType)
    {
        int status = (int)code;
        (ErpErrorKind kind, string message) = excType switch
        {
            "AuthenticationError" or "InvalidAuthorizationToken" or "InvalidAuthorizationHeader" or "InvalidAuthorizationPrefix" => (ErpErrorKind.Authentication, Auth),
            "PermissionError" => (ErpErrorKind.Permission, Permission),
            "DoesNotExistError" => (ErpErrorKind.NotFound, Missing),
            "MaxFileSizeReachedError" => (ErpErrorKind.FileTooLarge, TooLarge),
            _ => code switch
            {
                HttpStatusCode.Unauthorized => (ErpErrorKind.Authentication, Auth),
                HttpStatusCode.Forbidden => (ErpErrorKind.Permission, Permission),
                HttpStatusCode.NotFound => (ErpErrorKind.NotFound, Missing),
                HttpStatusCode.RequestEntityTooLarge => (ErpErrorKind.FileTooLarge, TooLarge),
                HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout => (ErpErrorKind.Timeout, "ERPNext did not answer in time."),
                _ when status is >= 400 and < 500 => (ErpErrorKind.Validation, "ERPNext refused the request" + (excType.Length > 0 ? $" ({excType})." : ".")),
                _ => (ErpErrorKind.Server, "ERPNext reported a server error. Try again later."),
            },
        };
        return new(kind, $"{message} (HTTP {status})", status);
    }
    private const string Auth = "ERPNext did not accept the API key and secret. Check them in Settings.";
    private const string Permission = "Your ERPNext user is not allowed to do this. Attaching a file needs write permission on the document.";
    private const string Missing = "ERPNext has no such document, or this site address is wrong.";
    private const string TooLarge = "The file is larger than this ERPNext site accepts.";

    private static ErpAttachment Attachment(JsonElement row) => new(Text(row, "name"), Text(row, "file_name"), Text(row, "file_url"),
        row.TryGetProperty("is_private", out var p) && (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out int flag) && flag == 1 || p.ValueKind == JsonValueKind.True),
        Text(row, "content_hash").ToLowerInvariant(), row.TryGetProperty("file_size", out var size) && size.ValueKind == JsonValueKind.Number && size.TryGetInt64(out long bytes) ? bytes : 0);

    private static string Text(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? "",
        JsonValueKind.Number => value.GetRawText(),
        _ => "",
    } : "";

    private static string Amount(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out decimal amount)
        ? amount.ToString("#,##0.00", CultureInfo.InvariantCulture) : Text(element, name);

    private static string MediaType(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        _ => "application/octet-stream",
    };

    /// <summary>Text for logs and dialogs, with this client's secret and anything that looks like a credential removed.</summary>
    public string Safe(string? text) => Redactor.Clean(text, secret);
    public void Dispose() => http.Dispose();
}
