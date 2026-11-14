using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using DocumentScanner.Core;
namespace DocumentScanner.Infrastructure;
public static class JsonFiles
{
    public static readonly JsonSerializerOptions Options = new() { WriteIndented=true, Converters={new JsonStringEnumConverter()} };
    public static void Write<T>(string path,T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {
            using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            { JsonSerializer.Serialize(stream,value,Options); stream.Flush(true); }
            File.Move(temporary,path,true);
        } finally { if(File.Exists(temporary))File.Delete(temporary); }
    }
    public static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path),Options) ?? throw new InvalidDataException("Empty configuration.");
}
public sealed class AppPaths
{
    public string Root {get;}
    public string Sessions => Path.Combine(Root,"Sessions");
    public string Logs => Path.Combine(Root,"Logs");
    public string Config => Path.Combine(Root,"Config","settings.json");
    public AppPaths(string? root = null)
    {
        Root=root??ChooseRoot(); Directory.CreateDirectory(Root); Directory.CreateDirectory(Sessions); Directory.CreateDirectory(Logs);
    }
    private static string ChooseRoot()
    {
        var beside=AppContext.BaseDirectory; var probe=Path.Combine(beside,$".write-test-{Guid.NewGuid():N}");
        try { using(File.Create(probe)){} File.Delete(probe); return beside; }
        catch(IOException){} catch(UnauthorizedAccessException){}
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DocumentScanner");
    }
}
public sealed class SettingsStore(AppPaths paths)
{
    public string? LoadWarning {get;private set;}
    private bool newerSchema;
    public AppSettings Load()
    {
        if(!File.Exists(paths.Config))return new();
        try {
            using(var check=JsonDocument.Parse(File.ReadAllText(paths.Config))){
                if(check.RootElement.TryGetProperty("SchemaVersion",out var schema)&&schema.GetInt32()>1||check.RootElement.TryGetProperty("Profiles",out var profiles)&&profiles.EnumerateArray().Any(p=>p.TryGetProperty("SchemaVersion",out var v)&&v.GetInt32()>2)){newerSchema=true;LoadWarning="Settings contain a newer unsupported schema. The original file is preserved and settings writes are blocked. Use a compatible application version.";return new();}
            }
            var value=JsonFiles.Read<AppSettings>(paths.Config);
            using var raw=JsonDocument.Parse(File.ReadAllText(paths.Config));
            bool legacy=!raw.RootElement.TryGetProperty("Output",out _);
            if(legacy){File.Copy(paths.Config,paths.Config+".before-profile-v2-"+DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"),false);
                var output=new OutputDefaults{Folder=value.OutputFolder,Format=value.Format,JpegQuality=value.JpegQuality};
                value=value with{Output=output,Profiles=value.Profiles.Select(p=>p with{Output=output}).ToList()};
                LoadWarning="Legacy settings were backed up and migrated to profile schema 2. New export/ERPNext fields use explicit defaults; review them before use.";
            }
            value.Validate();if(legacy)JsonFiles.Write(paths.Config,value);return value;
        }
        catch(Exception ex) when(ex is JsonException or ArgumentException or IOException or NotSupportedException or NullReferenceException or FormatException or InvalidOperationException)
        {
            LoadWarning="Settings could not be loaded. Safe defaults are in use; the previous file was preserved.";
            string backup=paths.Config+".invalid-"+DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
            try{File.Copy(paths.Config,backup,false);}catch(IOException){}catch(UnauthorizedAccessException){}
            return new();
        }
    }
    public void Save(AppSettings settings) { if(newerSchema)throw new InvalidOperationException("Newer settings schema: original file preserved; writes are blocked.");settings.Validate(); JsonFiles.Write(paths.Config,settings); }
}
public sealed class SessionStore(AppPaths paths)
{
    public string Folder(Guid id) => Path.Combine(paths.Sessions,id.ToString("N"));
    public void Save(DocumentSession session) => JsonFiles.Write(Path.Combine(Folder(session.Id),"session.json"),new SessionManifest(1,session.Id,DateTimeOffset.UtcNow,session.Pages.ToList()));
    public IReadOnlyList<SessionManifest> Recoverable()
    {
        var result=new List<SessionManifest>();
        foreach(var folder in Directory.EnumerateDirectories(paths.Sessions))
        {
            var file=Path.Combine(folder,"session.json"); if(!File.Exists(file))continue;
            try {
                var manifest=JsonFiles.Read<SessionManifest>(file);
                if(manifest.SchemaVersion!=1 || Path.GetFileName(folder)!=manifest.Id.ToString("N"))continue;
                if(manifest.Pages.Count>0 && manifest.Pages.All(p=>File.Exists(Resolve(folder,p.OriginalFile)))) result.Add(manifest);
            } catch(Exception ex) when(ex is IOException or JsonException or ArgumentException or InvalidDataException or NullReferenceException){}
        }
        return result.OrderByDescending(x=>x.Updated).ToList();
    }
    public static string Resolve(string folder,string relative)
    {
        if(Path.IsPathRooted(relative))throw new InvalidDataException("Session contains an absolute path.");
        string root=Path.GetFullPath(folder)+Path.DirectorySeparatorChar;
        string file=Path.GetFullPath(Path.Combine(folder,relative));
        if(!file.StartsWith(root,OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal))throw new InvalidDataException("Unsafe session path.");
        return file;
    }
    public void Delete(Guid id) { var folder=Folder(id); if(Directory.Exists(folder))Directory.Delete(folder,true); }
    public static void CleanupWorkFiles(string folder)
    {
        foreach(var name in new[]{"worker-temp","pdf-work"})
        {
            var path=Path.Combine(folder,name); if(Directory.Exists(path))Directory.Delete(path,true);
        }
    }
}
public static class SafeOutput
{
    // File.Move with overwrite=false performs the final collision check atomically.
    public static string Finalize(string temporary,string destination)
    {
        string directory=Path.GetDirectoryName(destination)!;
        string stem=Path.GetFileNameWithoutExtension(destination),extension=Path.GetExtension(destination);
        for(int i=0;i<10000;i++)
        {
            string candidate=i==0?destination:Path.Combine(directory,$"{stem} ({i}){extension}");
            try { File.Move(temporary,candidate,false); return candidate; }
            catch(IOException) when(File.Exists(candidate)){}
        }
        throw new IOException("No available output filename.");
    }
    public static string Temporary(string folder,string extension) => Path.Combine(folder,$".document-scanner-{Guid.NewGuid():N}{extension}");
}
