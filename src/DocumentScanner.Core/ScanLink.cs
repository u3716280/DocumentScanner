using System.Text;
namespace DocumentScanner.Core;

/// <summary>One ERPNext DocType the scan link may target, with the fields shown for it.</summary>
public sealed record ErpDocTypeInfo(string DocType, string Code, string PartyField, string PartyNameField, string PartyLabel,
    string? ReferenceField, string? ReferenceLabel, bool HasTotal);

/// <summary>The only DocTypes this version accepts. A link naming anything else is refused.</summary>
public static class ErpDocTypes
{
    public static readonly IReadOnlyList<ErpDocTypeInfo> All =
    [
        new("Sales Invoice", "SI", "customer", "customer_name", "Customer", null, null, true),
        new("Purchase Receipt", "PR", "supplier", "supplier_name", "Supplier", "supplier_delivery_note", "Supplier delivery note", false),
        new("Purchase Invoice", "PI", "supplier", "supplier_name", "Supplier", "bill_no", "Supplier invoice number", true),
    ];
    public static ErpDocTypeInfo? Find(string? doctype) => All.FirstOrDefault(d => string.Equals(d.DocType, doctype, StringComparison.Ordinal));
    public static ErpDocTypeInfo Require(string? doctype) => Find(doctype)
        ?? throw new ScanLinkException($"This version only accepts {string.Join(", ", All.Select(d => d.DocType))}.");
}

/// <summary>A validated request from ERPNext: which site, DocType and document to scan for. Carries no secrets.</summary>
public sealed record ScanLink(string SiteOrigin, string DocType, string DocName);

public sealed class ScanLinkException(string message) : Exception(message);

public static class ScanLinkParser
{
    public const string Scheme = "documentscanner";
    public const string Action = "scan";
    public const int MaxLength = 2048;
    public const int MaxDocNameLength = 140; // Frappe's name column

    /// <summary>
    /// Parses documentscanner://scan?site=…&amp;doctype=…&amp;docname=…. Anything else is refused: other actions,
    /// extra or repeated parameters, credentials, fragments, non-HTTPS sites and unknown DocTypes.
    /// The result still has to be matched against the user's allowed sites and verified with ERPNext.
    /// </summary>
    public static ScanLink Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new ScanLinkException("The scan link is empty.");
        text = text.Trim();
        if (text.Length > MaxLength || text.Any(char.IsControl)) throw new ScanLinkException("The scan link is malformed.");
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || !string.Equals(uri.Scheme, Scheme, StringComparison.OrdinalIgnoreCase))
            throw new ScanLinkException("This is not a DocumentScanner link.");
        if (!string.Equals(uri.Host, Action, StringComparison.OrdinalIgnoreCase)) throw new ScanLinkException("Unsupported link action.");
        if (!string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment) || !uri.IsDefaultPort || uri.AbsolutePath is not ("" or "/"))
            throw new ScanLinkException("The scan link is malformed.");

        string? site = null, doctype = null, docname = null;
        // Read the query from the original text: Uri may normalise escapes, and the decoder below must see them as sent.
        if (text.Contains('#')) throw new ScanLinkException("The scan link is malformed.");
        int mark = text.IndexOf('?');
        string query = mark < 0 ? "" : text[(mark + 1)..];
        if (query.Length == 0) throw new ScanLinkException("The scan link has no destination.");
        foreach (string pair in query.Split('&'))
        {
            int equals = pair.IndexOf('=');
            if (equals <= 0) throw new ScanLinkException("The scan link is malformed.");
            string key = Decode(pair[..equals]), value = Decode(pair[(equals + 1)..]);
            if (value.Length == 0 || value.Any(char.IsControl)) throw new ScanLinkException("The scan link is malformed.");
            switch (key)
            {
                case "site" when site == null: site = value; break;
                case "doctype" when doctype == null: doctype = value; break;
                case "docname" when docname == null: docname = value; break;
                default: throw new ScanLinkException("The scan link contains an unexpected or repeated parameter.");
            }
        }
        if (site == null || doctype == null || docname == null) throw new ScanLinkException("The scan link is missing its site, document type or document number.");
        if (docname.Length > MaxDocNameLength || docname != docname.Trim()) throw new ScanLinkException("The document number in the scan link is not valid.");
        return new(SiteOrigins.Normalize(site), ErpDocTypes.Require(doctype).DocType, docname);
    }

    /// <summary>Builds the link the same way the ERPNext Client Script does. Used by Test Scan Link and by tests.</summary>
    public static string Build(string siteOrigin, string doctype, string docname) =>
        $"{Scheme}://{Action}?site={Uri.EscapeDataString(siteOrigin)}&doctype={Uri.EscapeDataString(doctype)}&docname={Uri.EscapeDataString(docname)}";

    // Strict percent-decoding: every escape must be two hex digits and the bytes must be valid UTF-8.
    // '+' is kept literal; encodeURIComponent writes a space as %20.
    private static string Decode(string value)
    {
        var bytes = new List<byte>(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (c == '%')
            {
                if (i + 2 > value.Length - 1 || !Uri.IsHexDigit(value[i + 1]) || !Uri.IsHexDigit(value[i + 2])) throw new ScanLinkException("The scan link is malformed.");
                bytes.Add(Convert.ToByte(value.Substring(i + 1, 2), 16)); i += 2;
            }
            else if (c > 127) bytes.AddRange(Encoding.UTF8.GetBytes(c.ToString()));
            else bytes.Add((byte)c);
        }
        try { return new UTF8Encoding(false, true).GetString(bytes.ToArray()); }
        catch (ArgumentException) { throw new ScanLinkException("The scan link is malformed."); }
    }
}

public static class SiteOrigins
{
    /// <summary>Returns https://host[:port] in canonical form, or throws when the value is not a plain HTTPS origin.</summary>
    public static string Normalize(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) || uri.AbsolutePath != "/" || uri.HostNameType == UriHostNameType.Unknown)
            throw new ScanLinkException("The ERPNext site must be an HTTPS address such as https://erp.example.com, without a path.");
        return uri.GetLeftPart(UriPartial.Authority);
    }

    /// <summary>Same scheme, host and port. Host comparison ignores case; the default port matches an explicit 443.</summary>
    public static bool Same(string? a, string? b)
    {
        try { return string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase); }
        catch (ScanLinkException) { return false; }
    }

    /// <summary>The configured site this link belongs to, or null when the user has not allowed that site.</summary>
    public static ErpSite? Match(ScanLink link, IEnumerable<ErpSite> allowed) => allowed.FirstOrDefault(site => Same(site.Url, link.SiteOrigin));
}

public enum LaunchKind { None, Scan, Test, Invalid }

/// <summary>What a set of command-line arguments, or a message forwarded from a second instance, asks for.</summary>
public sealed record LaunchRequest(LaunchKind Kind, ScanLink? Link = null, string? Error = null)
{
    public const string TestUri = ScanLinkParser.Scheme + "://test";
    public static readonly LaunchRequest None = new(LaunchKind.None);

    /// <summary>
    /// Only an argument that starts with the documentscanner scheme is looked at; anything else is ignored, so the
    /// application never opens files or runs commands named on its command line.
    /// </summary>
    public static LaunchRequest From(IEnumerable<string?>? arguments)
    {
        string? uri = arguments?.FirstOrDefault(a => a != null && a.TrimStart().StartsWith(ScanLinkParser.Scheme + ":", StringComparison.OrdinalIgnoreCase))?.Trim();
        if (uri == null) return None;
        if (string.Equals(uri.TrimEnd('/'), TestUri, StringComparison.OrdinalIgnoreCase)) return new(LaunchKind.Test);
        try { return new(LaunchKind.Scan, ScanLinkParser.Parse(uri)); }
        catch (ScanLinkException e) { return new(LaunchKind.Invalid, Error: e.Message); }
    }

    /// <summary>The single line a second instance sends to the running one: the link itself, or empty to just bring it forward.</summary>
    public static string ForwardMessage(IEnumerable<string?>? arguments) =>
        arguments?.FirstOrDefault(a => a != null && a.TrimStart().StartsWith(ScanLinkParser.Scheme + ":", StringComparison.OrdinalIgnoreCase))?.Trim() ?? "";
}
