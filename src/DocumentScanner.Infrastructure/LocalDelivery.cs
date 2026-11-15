using System.Security.Cryptography;
using DocumentScanner.Core;
namespace DocumentScanner.Infrastructure;
public static class LocalDelivery
{
    public static string Hash(string path){using var stream=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();}
    public static bool Complete(UploadJournal journal,Action persist)
    {
        if(!journal.NeedsLocal)return true;
        try{
            Directory.CreateDirectory(journal.LocalFolder??throw new ArgumentException("Missing local folder."));
            foreach(var item in journal.Items)
            {
                if(Hash(item.File)!=item.Hash)throw new InvalidDataException("Frozen artifact changed.");
                string original=Path.GetFileName(item.File),stem=Path.GetFileNameWithoutExtension(original),extension=Path.GetExtension(original);int number=0;
                string candidate=item.LocalDestination??Path.Combine(journal.LocalFolder,original);
                string candidateFolder=Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetDirectoryName(candidate)!));
                string configuredFolder=Path.TrimEndingDirectorySeparator(Path.GetFullPath(journal.LocalFolder));
                if(!string.Equals(candidateFolder,configuredFolder,OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal))throw new InvalidDataException("Unsafe local retry path.");
                while(File.Exists(candidate)&&Hash(candidate)!=item.Hash)candidate=Path.Combine(journal.LocalFolder,$"{stem} ({++number}){extension}");
                item.LocalDestination=candidate;item.Name=Path.GetFileName(candidate);persist();
                if(!File.Exists(candidate))
                {
                    string temporary=SafeOutput.Temporary(journal.LocalFolder,extension);try{File.Copy(item.File,temporary);File.Move(temporary,candidate,false);}finally{if(File.Exists(temporary))File.Delete(temporary);}
                }
                if(!journal.LocalFiles.Contains(candidate))journal.LocalFiles.Add(candidate);persist();
            }
            journal.LocalComplete=true;journal.LocalError=null;persist();return true;
        }catch(Exception e) when(e is IOException or UnauthorizedAccessException or ArgumentException){journal.LocalComplete=false;journal.LocalError="Local save incomplete. Completed files are kept. "+e.Message;persist();return false;}
    }
}
