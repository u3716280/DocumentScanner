using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using DocumentScanner.Core;
namespace DocumentScanner.App;

public sealed record UriRegistrationStatus(bool Registered, string? RegisteredPath, bool MatchesThisCopy)
{
    public string Describe(string currentPath) => !Registered ? "Not registered. The Scan button in ERPNext cannot open this application yet."
        : MatchesThisCopy ? "Registered for this copy:\n" + RegisteredPath
        : $"Registered for a different location:\n{RegisteredPath}\n\nThis copy runs from:\n{currentPath}\nRegister again so ERPNext opens this copy.";
}

/// <summary>
/// The documentscanner:// link for the current Windows user (HKEY_CURRENT_USER, so no administrator rights).
/// A portable copy that is moved keeps pointing at its old path until it is registered again.
/// </summary>
public static class UriRegistration
{
    private const string KeyPath = @"Software\Classes\" + ScanLinkParser.Scheme;
    public static string CurrentExecutable => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "DocumentScanner.exe");
    public static string Command(string executable) => $"\"{executable}\" \"%1\"";

    /// <summary>The executable inside a registered command line, or null when the value is not in the expected form.</summary>
    public static string? ExecutableFromCommand(string? command)
    {
        if (string.IsNullOrWhiteSpace(command) || !command.StartsWith('"')) return null;
        int end = command.IndexOf('"', 1);
        return end > 1 ? command[1..end] : null;
    }

    public static bool SamePath(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    public static UriRegistrationStatus Status()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath + @"\shell\open\command");
        string? path = ExecutableFromCommand(key?.GetValue(null) as string);
        return new(path != null, path, SamePath(path, CurrentExecutable));
    }

    public static void Register()
    {
        using var root = Registry.CurrentUser.CreateSubKey(KeyPath);
        root.SetValue(null, "URL:DocumentScanner scan link");
        root.SetValue("URL Protocol", "");
        using var command = root.CreateSubKey(@"shell\open\command");
        command.SetValue(null, Command(CurrentExecutable));
    }

    public static void Remove() => Registry.CurrentUser.DeleteSubKeyTree(KeyPath, false);

    /// <summary>Opens the test link through Windows, exactly as a browser would.</summary>
    public static void OpenTestLink() => Process.Start(new ProcessStartInfo(LaunchRequest.TestUri) { UseShellExecute = true });
}
