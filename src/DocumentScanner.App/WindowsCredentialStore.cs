using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using DocumentScanner.Infrastructure;
namespace DocumentScanner.App;
public static class WindowsCredentialStore
{
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]private struct Credential
    {public uint Flags,Type;public string TargetName,Comment;public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;public uint CredentialBlobSize;public nint CredentialBlob;public uint Persist,AttributeCount;public nint Attributes;public string TargetAlias,UserName;}
    [DllImport("advapi32.dll",EntryPoint="CredWriteW",CharSet=CharSet.Unicode,SetLastError=true)]private static extern bool Write(ref Credential credential,uint flags);
    [DllImport("advapi32.dll",EntryPoint="CredReadW",CharSet=CharSet.Unicode,SetLastError=true)]private static extern bool Read(string target,uint type,uint flags,out nint credential);
    [DllImport("advapi32.dll")]private static extern void CredFree(nint credential);
    public static void Save(Guid site,ErpCredential value)
    {
        byte[] bytes=Encoding.Unicode.GetBytes(JsonSerializer.Serialize(value));nint blob=Marshal.AllocHGlobal(bytes.Length);
        try{Marshal.Copy(bytes,0,blob,bytes.Length);var c=new Credential{Type=1,TargetName="DocumentScanner/ERPNext/"+site,Comment="ERPNext API credentials — current Windows user",CredentialBlobSize=(uint)bytes.Length,CredentialBlob=blob,Persist=2,TargetAlias="",UserName="DocumentScanner"};if(!Write(ref c,0))throw new InvalidOperationException("Windows Credential Manager could not save credentials.");}
        finally{for(int i=0;i<bytes.Length;i++)Marshal.WriteByte(blob,i,0);Marshal.FreeHGlobal(blob);Array.Clear(bytes);}
    }
    public static ErpCredential? Load(Guid site)
    {
        if(!Read("DocumentScanner/ERPNext/"+site,1,0,out nint pointer))return null;
        try{var c=Marshal.PtrToStructure<Credential>(pointer);byte[] bytes=new byte[c.CredentialBlobSize];Marshal.Copy(c.CredentialBlob,bytes,0,bytes.Length);try{return JsonSerializer.Deserialize<ErpCredential>(Encoding.Unicode.GetString(bytes));}finally{Array.Clear(bytes);}}
        finally{CredFree(pointer);}
    }
}
