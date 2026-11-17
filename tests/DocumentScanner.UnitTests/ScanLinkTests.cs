using DocumentScanner.Core;
using Xunit;
namespace DocumentScanner.UnitTests;
public sealed class ScanLinkTests
{
    private const string Site = "https://erp.example.com";

    [Fact] public void ParsesTheExampleFromTheSpecification()
    {
        var link = ScanLinkParser.Parse("documentscanner://scan?site=https%3A%2F%2Ferp.example.com&doctype=Sales%20Invoice&docname=ACC-INV-2611-0001");
        Assert.Equal(new ScanLink(Site, "Sales Invoice", "ACC-INV-2611-0001"), link);
    }

    [Theory]
    [InlineData("Sales Order", "SAL-ORD-2026-00001")]
    [InlineData("Sales Invoice", "ACC-SINV-2026-00001")]
    [InlineData("Purchase Receipt", "MAT-PRE-2611-0001")]
    [InlineData("Purchase Invoice", "ใบซื้อ/2569 #7 & 50%+1")]
    public void BuildAndParseRoundTripIncludingReservedAndThaiCharacters(string doctype, string docname)
    {
        string uri = ScanLinkParser.Build(Site, doctype, docname);
        Assert.DoesNotContain(" ", uri);
        Assert.Equal(new ScanLink(Site, doctype, docname), ScanLinkParser.Parse(uri));
    }

    [Fact] public void LinkProducedByTheBrowsersEncoderIsRead()
    {
        // Output of encodeURIComponent in Node for the Client Script's three values.
        const string fromBrowser = "documentscanner://scan?site=https%3A%2F%2Fhouse147.eakthai.com&doctype=Purchase%20Invoice&docname=%E0%B9%83%E0%B8%9A%E0%B8%8B%E0%B8%B7%E0%B9%89%E0%B8%AD%2F2569%20%237%20%26%2050%25%2B1";
        Assert.Equal(new ScanLink("https://house147.eakthai.com", "Purchase Invoice", "ใบซื้อ/2569 #7 & 50%+1"), ScanLinkParser.Parse(fromBrowser));
        Assert.Equal(fromBrowser, ScanLinkParser.Build("https://house147.eakthai.com", "Purchase Invoice", "ใบซื้อ/2569 #7 & 50%+1"));
    }

    [Fact] public void SchemeAndActionAreCaseInsensitiveButDocTypeIsExact()
    {
        Assert.Equal("PINV-1", ScanLinkParser.Parse("DocumentScanner://SCAN?site=https%3A%2F%2Ferp.example.com&doctype=Purchase%20Invoice&docname=PINV-1").DocName);
        Assert.Throws<ScanLinkException>(() => ScanLinkParser.Parse(ScanLinkParser.Build(Site, "sales invoice", "X")));
    }

    [Theory]
    [InlineData("Delivery Note")]
    [InlineData("File")]
    [InlineData("User")]
    [InlineData("Sales Invoice ")]
    public void DocTypesOutsideTheAllowlistAreRefused(string doctype) =>
        Assert.Throws<ScanLinkException>(() => ScanLinkParser.Parse(ScanLinkParser.Build(Site, doctype, "X-1")));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://erp.example.com/app/sales-invoice/X")]
    [InlineData("documentscanner://open?site=https%3A%2F%2Ferp.example.com&doctype=Sales%20Invoice&docname=X")]
    [InlineData("documentscanner:scan?site=https%3A%2F%2Ferp.example.com&doctype=Sales%20Invoice&docname=X")]
    [InlineData("documentscanner://scan")]
    [InlineData("documentscanner://scan?site=https%3A%2F%2Ferp.example.com&doctype=Sales%20Invoice")]
    [InlineData("documentscanner://scan?site=https%3A%2F%2Ferp.example.com&doctype=Sales%20Invoice&docname=")]
    [InlineData("documentscanner://scan?site=https%3A%2F%2Ferp.example.com&doctype=Sales%20Invoice&docname=X&docname=Y")]
    [InlineData("documentscanner://scan?site=https%3A%2F%2Ferp.example.com&doctype=Sales%20Invoice&docname=X&api_secret=abc")]
    [InlineData("documentscanner://scan?site=https%3A%2F%2Ferp.example.com&doctype=Sales%20Invoice&docname=X&endpoint=%2Fapi%2Fmethod%2Fx")]
    [InlineData("documentscanner://scan?site=https%3A%2F%2Ferp.example.com&doctype=Sales%20Invoice&docname=X#fragment")]
    [InlineData("documentscanner://user:pw@scan?site=https%3A%2F%2Ferp.example.com&doctype=Sales%20Invoice&docname=X")]
    [InlineData("documentscanner://scan/extra?site=https%3A%2F%2Ferp.example.com&doctype=Sales%20Invoice&docname=X")]
    [InlineData("documentscanner://scan?site=https%3A%2F%2Ferp.example.com&doctype=Sales%20Invoice&docname=%ZZ")]
    [InlineData("documentscanner://scan?site=https%3A%2F%2Ferp.example.com&doctype=Sales%20Invoice&docname=%E0%B8")]
    [InlineData("documentscanner://scan?site=https%3A%2F%2Ferp.example.com&doctype=Sales%20Invoice&docname=X%0D%0AY")]
    [InlineData("documentscanner://scan?site=https%3A%2F%2Ferp.example.com&doctype=Sales%20Invoice&docname=%20X")]
    public void MalformedOrUnexpectedLinksAreRefused(string? uri) => Assert.Throws<ScanLinkException>(() => ScanLinkParser.Parse(uri));

    [Theory]
    [InlineData("http://erp.example.com")]
    [InlineData("https://erp.example.com/app")]
    [InlineData("https://erp.example.com/?next=https://evil.example")]
    [InlineData("https://user:pw@erp.example.com")]
    [InlineData("ftp://erp.example.com")]
    [InlineData("//erp.example.com")]
    [InlineData("javascript:alert(1)")]
    public void OnlyAPlainHttpsOriginIsAcceptedAsSite(string site) =>
        Assert.Throws<ScanLinkException>(() => ScanLinkParser.Parse(ScanLinkParser.Build(site, "Sales Invoice", "X")));

    [Fact] public void OverlongLinksAndNamesAreRefused()
    {
        Assert.Throws<ScanLinkException>(() => ScanLinkParser.Parse(ScanLinkParser.Build(Site, "Sales Invoice", new string('A', ScanLinkParser.MaxDocNameLength + 1))));
        Assert.Throws<ScanLinkException>(() => ScanLinkParser.Parse("documentscanner://scan?site=" + new string('a', ScanLinkParser.MaxLength)));
    }

    [Fact] public void SiteAllowlistComparesSchemeHostAndPort()
    {
        var allowed = new[] { new ErpSite { Name = "Main", Url = "https://ERP.example.com/" }, new ErpSite { Name = "Alt", Url = "https://erp.example.com:8443" } };
        Assert.Equal("Main", SiteOrigins.Match(new(Site, "Sales Invoice", "X"), allowed)?.Name);
        Assert.Equal("Main", SiteOrigins.Match(new("https://erp.example.com:443", "Sales Invoice", "X"), allowed)?.Name);
        Assert.Equal("Alt", SiteOrigins.Match(new("https://erp.example.com:8443", "Sales Invoice", "X"), allowed)?.Name);
        Assert.Null(SiteOrigins.Match(new("https://erp.example.com:9443", "Sales Invoice", "X"), allowed));
        Assert.Null(SiteOrigins.Match(new("https://erp.example.com.evil.test", "Sales Invoice", "X"), allowed));
        Assert.Null(SiteOrigins.Match(new("https://evil.test", "Sales Invoice", "X"), allowed));
        Assert.False(SiteOrigins.Same("http://erp.example.com", Site));
    }

    [Fact] public void DocTypeCodesMatchTheNamingStandard() =>
        Assert.Equal(new[] { "Sales Order=SO", "Sales Invoice=SI", "Purchase Receipt=PR", "Purchase Invoice=PI" }, ErpDocTypes.All.Select(d => $"{d.DocType}={d.Code}"));
}
