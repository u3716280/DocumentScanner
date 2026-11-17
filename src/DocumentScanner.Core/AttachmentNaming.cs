using System.Globalization;
using System.Text;
namespace DocumentScanner.Core;

/// <summary>
/// File names for scans that belong to an ERPNext document:
///   PDF    {CODE}_{DOCNAME}[_{description}][_{NNN}].pdf
///   image  {CODE}_{DOCNAME}[_{description}]_P{page:000}[_{NNN}].{jpg|png}
/// The document number always comes from ERPNext; the user only chooses the optional description.
/// </summary>
public static class AttachmentNaming
{
    public const int MaxFileName = 120;
    public const int MaxDescription = 40;
    public const int MaxSuffix = 999;
    private const int Reserved = 14; // "_P001" + "_001" + ".jpeg"
    private const string Invalid = "<>:\"/\\|?*";

    /// <summary>Makes one safe path segment: reserved and control characters become '-', no leading or trailing dots or spaces.</summary>
    public static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var text = new StringBuilder();
        bool space = false;
        foreach (char c in value.Normalize(NormalizationForm.FormC))
        {
            if (char.IsWhiteSpace(c)) { space = text.Length > 0; continue; }
            if (space) { text.Append(' '); space = false; }
            UnicodeCategory category = char.GetUnicodeCategory(c);
            text.Append(c < 32 || Invalid.Contains(c) || category is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.Surrogate or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned ? '-' : c);
        }
        return text.ToString().Trim(' ', '.');
    }

    /// <summary>The common stem of every file in one delivery. Throws when the document number cannot fit a safe file name.</summary>
    public static string BaseName(string doctype, string docname, string? description = null)
    {
        string code = ErpDocTypes.Require(doctype).Code, number = Sanitize(docname);
        if (number.Length == 0) throw new ArgumentException("The document number cannot be used in a file name.");
        string stem = code + "_" + number;
        if (stem.Length > MaxFileName - Reserved) throw new ArgumentException("The document number is too long for a safe file name.");
        string note = Sanitize(description);
        if (note.Length > MaxDescription) note = note[..MaxDescription].TrimEnd(' ', '.', '-');
        int room = MaxFileName - Reserved - stem.Length - 1;
        if (note.Length > room) note = room > 0 ? note[..room].TrimEnd(' ', '.', '-') : "";
        string result = note.Length == 0 ? stem : stem + "_" + note;
        FilenameRules.Validate(result);
        return result;
    }

    public static string Extension(ExportFormat format) => format switch
    {
        ExportFormat.Pdf => "pdf",
        ExportFormat.Jpg => "jpg",
        ExportFormat.Png => "png",
        _ => throw new ArgumentException("Unsupported format."),
    };

    /// <summary>page is null for a PDF and the 1-based page number for an image. suffix 0 means no number.</summary>
    public static string FileName(string baseName, ExportFormat format, int? page, int suffix)
    {
        if (suffix is < 0 or > MaxSuffix) throw new ArgumentException("Invalid duplicate number.");
        if (format == ExportFormat.Pdf != (page == null)) throw new ArgumentException("A PDF has no page number; an image needs one.");
        if (page is < 1 or > 9999) throw new ArgumentException("Invalid page number.");
        string name = baseName + (page == null ? "" : $"_P{page.Value.ToString("000", CultureInfo.InvariantCulture)}")
            + (suffix == 0 ? "" : $"_{suffix.ToString("000", CultureInfo.InvariantCulture)}") + "." + Extension(format);
        FilenameRules.Validate(name);
        return name;
    }

    /// <summary>
    /// The first name that is free everywhere it will be written. For "save and send", pass a test that is true
    /// when the name exists locally or on the ERPNext document, so both sides receive the same name.
    /// </summary>
    public static string Resolve(string baseName, ExportFormat format, int? page, Func<string, bool> taken)
    {
        for (int suffix = 0; suffix <= MaxSuffix; suffix++)
        {
            string candidate = FileName(baseName, format, page, suffix);
            if (!taken(candidate)) return candidate;
        }
        throw new IOException("No free file name is left for this document.");
    }

    /// <summary>Names for a whole delivery. A name given to one file of the set is not offered to the next.</summary>
    public static IReadOnlyList<string> ResolveAll(string baseName, ExportFormat format, int files, Func<string, bool> taken)
    {
        if (files < 1 || format == ExportFormat.Pdf && files != 1) throw new ArgumentException("Invalid file count.");
        var chosen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new List<string>(files);
        for (int i = 0; i < files; i++)
        {
            string name = Resolve(baseName, format, format == ExportFormat.Pdf ? null : i + 1, candidate => chosen.Contains(candidate) || taken(candidate));
            chosen.Add(name); names.Add(name);
        }
        return names;
    }

    /// <summary>True when a name differs from the one first proposed, so the user can be told before anything is written.</summary>
    public static bool WasRenamed(string baseName, ExportFormat format, int? page, string resolved) =>
        !string.Equals(FileName(baseName, format, page, 0), resolved, StringComparison.OrdinalIgnoreCase);
}
