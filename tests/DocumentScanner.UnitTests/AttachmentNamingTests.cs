using DocumentScanner.Core;
using Xunit;
namespace DocumentScanner.UnitTests;
public sealed class AttachmentNamingTests
{
    [Theory]
    [InlineData("Sales Order", "SAL-ORD-2611-0001", "SO_SAL-ORD-2611-0001.pdf")]
    [InlineData("Sales Invoice", "ACC-INV-2611-0001", "SI_ACC-INV-2611-0001.pdf")]
    [InlineData("Purchase Receipt", "MAT-PRE-2611-0001", "PR_MAT-PRE-2611-0001.pdf")]
    [InlineData("Purchase Invoice", "ACC-PINV-2611-0001", "PI_ACC-PINV-2611-0001.pdf")]
    public void PdfNamesFollowTheStandard(string doctype, string docname, string expected) =>
        Assert.Equal(expected, AttachmentNaming.FileName(AttachmentNaming.BaseName(doctype, docname), ExportFormat.Pdf, null, 0));

    [Fact] public void ImageNamesCarryAThreeDigitPageNumberAndTheRealExtension()
    {
        string stem = AttachmentNaming.BaseName("Sales Invoice", "ACC-INV-2611-0001");
        Assert.Equal("SI_ACC-INV-2611-0001_P001.jpg", AttachmentNaming.FileName(stem, ExportFormat.Jpg, 1, 0));
        Assert.Equal("SI_ACC-INV-2611-0001_P012.png", AttachmentNaming.FileName(stem, ExportFormat.Png, 12, 0));
        Assert.Equal("SI_ACC-INV-2611-0001_P001_001.jpg", AttachmentNaming.FileName(stem, ExportFormat.Jpg, 1, 1));
        Assert.Equal("SI_ACC-INV-2611-0001_002.pdf", AttachmentNaming.FileName(stem, ExportFormat.Pdf, null, 2));
        Assert.Throws<ArgumentException>(() => AttachmentNaming.FileName(stem, ExportFormat.Pdf, 1, 0));
        Assert.Throws<ArgumentException>(() => AttachmentNaming.FileName(stem, ExportFormat.Jpg, null, 0));
        Assert.Throws<ArgumentException>(() => AttachmentNaming.FileName(stem, ExportFormat.Jpg, 0, 0));
    }

    [Fact] public void DescriptionIsOptionalAcceptsThaiAndIsLimited()
    {
        Assert.Equal("PI_ACC-PINV-2611-0042_ใบกำกับภาษี", AttachmentNaming.BaseName("Purchase Invoice", "ACC-PINV-2611-0042", "ใบกำกับภาษี"));
        Assert.Equal("PI_ACC-PINV-2611-0042", AttachmentNaming.BaseName("Purchase Invoice", "ACC-PINV-2611-0042", "   "));
        string longNote = AttachmentNaming.BaseName("Purchase Invoice", "X-1", new string('ก', 90));
        Assert.Equal("PI_X-1_".Length + AttachmentNaming.MaxDescription, longNote.Length);
    }

    [Theory]
    [InlineData("..\\..\\Windows\\win.ini", "..-..-Windows-win.ini")]
    [InlineData("../../etc/passwd", "..-..-etc-passwd")]
    [InlineData("A/B:C*D?E\"F<G>H|I", "A-B-C-D-E-F-G-H-I")]
    [InlineData("  spaced   out  ", "spaced out")]
    [InlineData("tab\tand\nnewline", "tab and newline")]
    [InlineData("trailing. ", "trailing")]
    public void UnsafeCharactersNeverReachTheFileSystem(string input, string expected)
    {
        Assert.Equal(expected.TrimStart('.'), AttachmentNaming.Sanitize(input).TrimStart('.'));
        string name = AttachmentNaming.FileName(AttachmentNaming.BaseName("Sales Invoice", input), ExportFormat.Pdf, null, 0);
        Assert.Equal(name, Path.GetFileName(name));
        Assert.DoesNotContain(name, c => "<>:\"/\\|?*".Contains(c) || c < 32);
        Assert.StartsWith("SI_", name);
    }

    [Fact] public void DocumentNumberIsNeverSilentlyShortened()
    {
        Assert.Throws<ArgumentException>(() => AttachmentNaming.BaseName("Sales Invoice", new string('9', 130)));
        Assert.Throws<ArgumentException>(() => AttachmentNaming.BaseName("Sales Invoice", " . "));
        Assert.Equal("SI_---", AttachmentNaming.BaseName("Sales Invoice", "///"));
        Assert.Throws<ScanLinkException>(() => AttachmentNaming.BaseName("Delivery Note", "DN-1"));
        string longest = AttachmentNaming.BaseName("Sales Invoice", new string('9', 100), new string('x', 40));
        Assert.True(AttachmentNaming.FileName(longest, ExportFormat.Jpg, 999, 999).Length <= AttachmentNaming.MaxFileName);
    }

    [Fact] public void DuplicateResolverAddsTheFirstFreeNumber()
    {
        string stem = AttachmentNaming.BaseName("Sales Invoice", "ACC-INV-2611-0001");
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SI_ACC-INV-2611-0001.pdf", "si_acc-inv-2611-0001_001.PDF" };
        string name = AttachmentNaming.Resolve(stem, ExportFormat.Pdf, null, taken.Contains);
        Assert.Equal("SI_ACC-INV-2611-0001_002.pdf", name);
        Assert.True(AttachmentNaming.WasRenamed(stem, ExportFormat.Pdf, null, name));
        Assert.False(AttachmentNaming.WasRenamed(stem, ExportFormat.Pdf, null, AttachmentNaming.Resolve(stem, ExportFormat.Pdf, null, _ => false)));
        Assert.Throws<IOException>(() => AttachmentNaming.Resolve(stem, ExportFormat.Pdf, null, _ => true));
    }

    [Fact] public void CommonResolutionPicksANameFreeOnBothSides()
    {
        string stem = AttachmentNaming.BaseName("Sales Invoice", "ACC-INV-2611-0001");
        var local = new HashSet<string> { "SI_ACC-INV-2611-0001_P001.jpg" };
        var server = new HashSet<string> { "SI_ACC-INV-2611-0001_P001_001.jpg", "SI_ACC-INV-2611-0001_P002.jpg" };
        var names = AttachmentNaming.ResolveAll(stem, ExportFormat.Jpg, 3, n => local.Contains(n) || server.Contains(n));
        Assert.Equal(new[] { "SI_ACC-INV-2611-0001_P001_002.jpg", "SI_ACC-INV-2611-0001_P002_001.jpg", "SI_ACC-INV-2611-0001_P003.jpg" }, names);
        Assert.Throws<ArgumentException>(() => AttachmentNaming.ResolveAll(stem, ExportFormat.Pdf, 2, _ => false));
    }
}
