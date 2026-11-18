namespace DocumentScanner.Core;

/// <summary>The scanner is working on page <paramref name="Page"/> (1-based). <paramref name="Fraction"/> is 0–1, or null when the
/// page has started but the driver reports no percentage.</summary>
public sealed record ScanProgress(int Page, double? Fraction);

/// <summary>What the progress bar shows while a scan runs.</summary>
public readonly record struct ScanProgressView(string Text, double Percent, bool Indeterminate);

public static class ScanProgressText
{
    /// <summary>
    /// <paramref name="current"/> is the latest report, or null before the scanner has started a page.
    /// <paramref name="received"/> counts pages of this scan that are already shown in the page list.
    /// </summary>
    public static ScanProgressView Describe(ScanProgress? current, int received)
    {
        if (current == null)
            return received == 0 ? new("Starting the scanner…", 0, true) : new($"Page {received} received · waiting for the next page…", 0, true);
        int page = Math.Max(1, current.Page);
        // The page in the report has arrived; the scanner has not started another one yet.
        if (received >= page) return new($"Page {received} received · waiting for the next page…", 0, true);
        if (current.Fraction is not double fraction || double.IsNaN(fraction)) return new($"Scanning page {page}…", 0, true);
        double percent = Math.Round(Math.Clamp(fraction, 0, 1) * 100);
        return new($"Scanning page {page} · {percent:0} %", percent, false);
    }
}
