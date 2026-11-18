using DocumentScanner.Core;
using Xunit;
namespace DocumentScanner.UnitTests;
public sealed class ScanProgressTextTests
{
    [Fact] public void BeforeTheFirstPageTheBarMovesWithoutAPercentage()
    {
        var view = ScanProgressText.Describe(null, 0);
        Assert.Equal("Starting the scanner…", view.Text);
        Assert.True(view.Indeterminate);
        Assert.Equal(0, view.Percent);
    }

    [Fact] public void APageWithoutDriverPercentageShowsItsNumberOnly()
    {
        var view = ScanProgressText.Describe(new(2, null), 1);
        Assert.Equal("Scanning page 2…", view.Text);
        Assert.True(view.Indeterminate);
    }

    [Theory]
    [InlineData(0.0, 0, "Scanning page 1 · 0 %")]
    [InlineData(0.454, 45, "Scanning page 1 · 45 %")]
    [InlineData(1.0, 100, "Scanning page 1 · 100 %")]
    [InlineData(1.7, 100, "Scanning page 1 · 100 %")]
    [InlineData(-0.2, 0, "Scanning page 1 · 0 %")]
    public void DriverPercentageIsShownAndClamped(double fraction, double percent, string text)
    {
        var view = ScanProgressText.Describe(new(1, fraction), 0);
        Assert.False(view.Indeterminate);
        Assert.Equal(percent, view.Percent);
        Assert.Equal(text, view.Text);
    }

    [Fact] public void AReceivedPageWaitsForTheNextOne()
    {
        var view = ScanProgressText.Describe(new(1, 1.0), 1);
        Assert.Equal("Page 1 received · waiting for the next page…", view.Text);
        Assert.True(view.Indeterminate);
        Assert.Equal("Page 3 received · waiting for the next page…", ScanProgressText.Describe(null, 3).Text);
    }

    [Fact] public void NaNAndZeroPageNumbersDoNotBreakTheText()
    {
        Assert.Equal("Scanning page 1…", ScanProgressText.Describe(new(0, double.NaN), 0).Text);
    }
}
