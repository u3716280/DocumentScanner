using System.IO;
using System.Runtime.CompilerServices;
namespace DocumentScanner.IntegrationTests;
// TEMPORARY diagnostic for the hung workspace test; remove before merge.
// Writes first-chance exceptions next to the UI captures. The .png extension is only there so the
// existing workflow uploads the file; it is plain text.
internal static class FirstChanceLog
{
    [ThreadStatic]private static bool writing;
    private static readonly object Gate=new();private static int count;
    [ModuleInitializer]internal static void Start()
    {
        string? folder=Environment.GetEnvironmentVariable("DOCUMENTSCANNER_UI_PREVIEW_DIR");if(string.IsNullOrWhiteSpace(folder))return;
        Directory.CreateDirectory(folder);string file=Path.Combine(folder,"zz-first-chance-log.txt.png");
        AppDomain.CurrentDomain.FirstChanceException+=(_,e)=>
        {
            if(writing||count>150)return;writing=true;
            try{lock(Gate){count++;File.AppendAllText(file,$"{DateTime.UtcNow:O} {e.Exception.GetType().FullName}: {e.Exception.Message}\n{Environment.StackTrace}\n\n");}}
            catch{}
            finally{writing=false;}
        };
    }
}
