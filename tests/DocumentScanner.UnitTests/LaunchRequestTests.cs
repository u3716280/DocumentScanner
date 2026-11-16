using DocumentScanner.Core;
using Xunit;
namespace DocumentScanner.UnitTests;
public sealed class LaunchRequestTests
{
    private const string Link = "documentscanner://scan?site=https%3A%2F%2Ferp.example.com&doctype=Sales%20Invoice&docname=ACC-INV-2611-0001";

    [Fact] public void NoArgumentsOrUnrelatedArgumentsAreAPlainStart()
    {
        Assert.Equal(LaunchKind.None, LaunchRequest.From(null).Kind);
        Assert.Equal(LaunchKind.None, LaunchRequest.From([]).Kind);
        // Files, switches and other schemes on the command line are ignored, never opened.
        Assert.Equal(LaunchKind.None, LaunchRequest.From(["C:\\scan.pdf", "--verbose", "https://erp.example.com", "file:///c:/windows/win.ini"]).Kind);
    }

    [Fact] public void AScanLinkArgumentBecomesAValidatedRequest()
    {
        var request = LaunchRequest.From(["--ignored", "  " + Link + "  "]);
        Assert.Equal(LaunchKind.Scan, request.Kind); Assert.Equal(new ScanLink("https://erp.example.com", "Sales Invoice", "ACC-INV-2611-0001"), request.Link); Assert.Null(request.Error);
    }

    [Fact] public void ABadLinkIsReportedAndNeverTurnedIntoADestination()
    {
        var request = LaunchRequest.From(["documentscanner://scan?site=https%3A%2F%2Ferp.example.com&doctype=User&docname=Administrator"]);
        Assert.Equal(LaunchKind.Invalid, request.Kind); Assert.Null(request.Link); Assert.False(string.IsNullOrWhiteSpace(request.Error));
        Assert.Equal(LaunchKind.Invalid, LaunchRequest.From(["documentscanner://scan?api_secret=abc"]).Kind);
        Assert.Equal(LaunchKind.Invalid, LaunchRequest.From(["documentscanner:"]).Kind);
    }

    [Theory]
    [InlineData("documentscanner://test")][InlineData("documentscanner://test/")][InlineData("DOCUMENTSCANNER://TEST")]
    public void TheTestLinkIsRecognised(string uri) => Assert.Equal(LaunchKind.Test, LaunchRequest.From([uri]).Kind);

    [Fact] public void TheForwardedMessageIsTheLinkOrEmptyAndSurvivesTheRoundTrip()
    {
        Assert.Equal("", LaunchRequest.ForwardMessage(null)); Assert.Equal("", LaunchRequest.ForwardMessage(["C:\\scan.pdf"]));
        string message = LaunchRequest.ForwardMessage(["x", Link]); Assert.Equal(Link, message);
        // The running copy treats the message exactly like its own command line.
        Assert.Equal(LaunchRequest.From([Link]), LaunchRequest.From([message]));
        Assert.Equal(LaunchKind.None, LaunchRequest.From([""]).Kind);
    }
}
