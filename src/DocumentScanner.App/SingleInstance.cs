using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
namespace DocumentScanner.App;

/// <summary>
/// One running copy per Windows user. A second copy hands its scan link to the first through a named pipe that
/// only the same user can open, then exits. The receiver validates the line exactly like a command-line argument.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    // Same mutex name as 1.x, so an old and a new copy never write the same session folders at once.
    private const string MutexName = @"Local\DocumentScanner-1.0";
    private const int MaxMessage = 4096;
    private readonly Mutex mutex;
    private readonly CancellationTokenSource stop = new();
    private readonly string pipe;
    public bool IsFirst { get; }

    public SingleInstance(string? name = null)
    {
        pipe = name ?? DefaultPipeName();
        mutex = new Mutex(true, name == null ? MutexName : @"Local\" + name, out bool first);
        IsFirst = first;
    }

    public static string DefaultPipeName()
    {
        string user = OperatingSystem.IsWindows() ? WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName : Environment.UserName;
        return "DocumentScanner-ScanLink-" + new string(user.Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray());
    }

    /// <summary>Starts listening. <paramref name="received"/> is called on a background thread, once per message.</summary>
    public void Listen(Action<string> received)
    {
        _ = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(pipe, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync(stop.Token);
                    using var limit = CancellationTokenSource.CreateLinkedTokenSource(stop.Token); limit.CancelAfter(TimeSpan.FromSeconds(5));
                    byte[] buffer = new byte[MaxMessage]; int total = 0;
                    while (total < buffer.Length)
                    {
                        int read = await server.ReadAsync(buffer.AsMemory(total), limit.Token);
                        if (read == 0) break;
                        total += read;
                    }
                    // An over-long message is dropped rather than truncated into something else.
                    if (total < buffer.Length) received(Encoding.UTF8.GetString(buffer, 0, total).Trim());
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { return; }
                catch (OperationCanceledException) { }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { await Task.Delay(1000); }
            }
        });
    }

    /// <summary>Sends one message to the running copy. False when it does not answer in time.</summary>
    public bool Forward(string message, int timeoutMilliseconds = 5000)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", pipe, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect(timeoutMilliseconds);
            byte[] bytes = Encoding.UTF8.GetBytes(message);
            if (bytes.Length >= MaxMessage) return false;
            client.Write(bytes, 0, bytes.Length); client.Flush();
            return true;
        }
        catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException) { return false; }
    }

    public void Dispose()
    {
        stop.Cancel();
        if (IsFirst) { try { mutex.ReleaseMutex(); } catch (ApplicationException) { } }
        mutex.Dispose(); stop.Dispose();
    }
}
