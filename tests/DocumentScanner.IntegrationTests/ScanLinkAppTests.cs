using DocumentScanner.App;
using Xunit;
namespace DocumentScanner.IntegrationTests;
public sealed class ScanLinkAppTests
{
    [Fact] public async Task SecondCopyForwardsItsLinkToTheFirstAndCannotBecomeFirst()
    {
        string name = "DocumentScanner-Test-" + Guid.NewGuid().ToString("N");
        using var first = new SingleInstance(name); Assert.True(first.IsFirst);
        var received = new List<string>(); var signal = new SemaphoreSlim(0);
        first.Listen(message => { lock (received) received.Add(message); signal.Release(); });
        using var second = new SingleInstance(name); Assert.False(second.IsFirst);
        const string link = "documentscanner://scan?site=https%3A%2F%2Ferp.example.com&doctype=Purchase%20Invoice&docname=%E0%B9%83%E0%B8%9A-1";
        Assert.True(second.Forward(link)); Assert.True(await signal.WaitAsync(TimeSpan.FromSeconds(10)));
        // The listener accepts the next message as well: one pipe instance per message.
        Assert.True(second.Forward("")); Assert.True(await signal.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.False(second.Forward(new string('x', 5000)));
        lock (received) Assert.Equal(new[] { link, "" }, received);
    }

    [Fact] public void ForwardReportsFailureWhenNoCopyIsListening()
    {
        using var alone = new SingleInstance("DocumentScanner-Test-" + Guid.NewGuid().ToString("N"));
        Assert.False(alone.Forward("documentscanner://test", 300));
    }

    [Fact] public void RegistrationCommandQuotesThePathAndIsReadBackExactly()
    {
        const string path = @"C:\Users\Some User\Portable Apps\DocumentScanner\DocumentScanner.exe";
        string command = UriRegistration.Command(path);
        Assert.Equal("\"" + path + "\" \"%1\"", command);
        Assert.Equal(path, UriRegistration.ExecutableFromCommand(command));
        Assert.Null(UriRegistration.ExecutableFromCommand(null)); Assert.Null(UriRegistration.ExecutableFromCommand("DocumentScanner.exe %1")); Assert.Null(UriRegistration.ExecutableFromCommand("\"unterminated"));
        Assert.True(UriRegistration.SamePath(path, path.ToUpperInvariant())); Assert.True(UriRegistration.SamePath(path, @"C:\Users\Some User\Portable Apps\DocumentScanner\.\DocumentScanner.exe"));
        Assert.False(UriRegistration.SamePath(path, @"D:\Moved\DocumentScanner.exe")); Assert.False(UriRegistration.SamePath(path, null));
        Assert.Contains("different location", new UriRegistrationStatus(true, @"D:\Old\DocumentScanner.exe", false).Describe(path));
        Assert.Contains("Not registered", new UriRegistrationStatus(false, null, false).Describe(path));
    }
}
