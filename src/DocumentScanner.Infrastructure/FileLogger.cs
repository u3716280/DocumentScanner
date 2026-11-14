using System.IO;
using Microsoft.Extensions.Logging;
namespace DocumentScanner.Infrastructure;
public sealed class FileLoggerProvider(AppPaths paths) : ILoggerProvider
{
    private readonly object sync=new();
    public ILogger CreateLogger(string categoryName) => new Logger(this,categoryName);
    public void Dispose(){}
    private void Write(LogLevel level,string category,string message,Exception? error)
    {
        lock(sync)
        {
            try {
                var file=Path.Combine(paths.Logs,$"scanner-{DateTime.Now:yyyyMMdd}.log");
                if(File.Exists(file)&&new FileInfo(file).Length>4*1024*1024)File.Move(file,file+".previous",true);
                // Never include device endpoints, output paths, document names or image contents.
                File.AppendAllText(file,$"{DateTimeOffset.Now:O} [{level}] {category}: {message} {error?.GetType().FullName} HResult={error?.HResult}{Environment.NewLine}");
                foreach(var old in Directory.EnumerateFiles(paths.Logs,"scanner-*.log*").Where(p=>File.GetLastWriteTimeUtc(p)<DateTime.UtcNow.AddDays(-14))) File.Delete(old);
            } catch(IOException){}catch(UnauthorizedAccessException){}
        }
    }
    private sealed class Logger(FileLoggerProvider owner,string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState:notnull => null;
        public bool IsEnabled(LogLevel level)=>level>=LogLevel.Information;
        public void Log<TState>(LogLevel level,EventId id,TState state,Exception? ex,Func<TState,Exception?,string> formatter)
        { if(IsEnabled(level))owner.Write(level,category,formatter(state,ex),ex); }
    }
}
