using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DocumentScanner.Core;
namespace DocumentScanner.Infrastructure;
public sealed record ErpCredential(string Key,string Secret);
public sealed record ErpConnection(string Account,string Version,string[] DocTypes);
public sealed record ErpSearch(List<ErpDocument> Rows,bool More,string[] SearchFields);
public sealed record ErpReceipt(string? FileId);
public sealed class ErpClient : IDisposable
{
    private readonly HttpClient http;
    private static readonly JsonSerializerOptions json=new(){PropertyNameCaseInsensitive=true};
    public ErpSite Site{get;}
    public ErpClient(ErpSite site,ErpCredential credential,HttpMessageHandler? handler=null)
    {
        site.Validate();if(string.IsNullOrWhiteSpace(credential.Key)||string.IsNullOrWhiteSpace(credential.Secret)||credential.Key.Contains(':')||(credential.Key+credential.Secret).Any(char.IsControl))throw new ArgumentException("Invalid API credentials.");
        Site=site;http=new(handler??new HttpClientHandler{AllowAutoRedirect=false}){BaseAddress=new Uri(site.Url.TrimEnd('/')+"/"),Timeout=TimeSpan.FromMinutes(2)};
        http.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("token",credential.Key+":"+credential.Secret);
    }
    public async Task<T> Call<T>(string method,Dictionary<string,string>? values=null,CancellationToken ct=default)
    {
        if(!new[]{"ping","search_documents","document","receipt","upload"}.Contains(method))throw new ArgumentException("Unknown bridge method.");
        using var body=new FormUrlEncodedContent(values??[]);
        return await Post<T>(method,body,ct);
    }
    private async Task<T> Post<T>(string method,HttpContent body,CancellationToken ct)
    {
        using var response=await http.PostAsync("api/method/document_scanner_bridge.api."+method,body,ct);
        if(!response.IsSuccessStatusCode)throw new ErpRequestException(response.StatusCode,ErpConnectionErrors.Describe(response.StatusCode));
        using var result=JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if(!result.RootElement.TryGetProperty("message",out var message))throw new InvalidDataException("Invalid bridge response.");
        return message.Deserialize<T>(json)??throw new InvalidDataException("Empty bridge response.");
    }
    public async Task<ErpConnection> Connect(CancellationToken ct)
    {
        var result=await Call<ErpConnection>("ping",ct:ct);
        if(string.IsNullOrWhiteSpace(result.Account)||string.IsNullOrWhiteSpace(result.Version)||result.DocTypes==null)throw new InvalidDataException("Invalid Bridge connection response. Verify the site URL and Bridge version.");
        return result;
    }
    public Task<ErpSearch> Search(string doctype,string query,string field,int start,CancellationToken ct)=>Call<ErpSearch>("search_documents",new(){["doctype"]=doctype,["query"]=query,["field"]=field,["start"]=start.ToString()},ct);
    public Task<ErpDocument> Document(string doctype,string name,CancellationToken ct)=>Call<ErpDocument>("document",new(){["doctype"]=doctype,["name"]=name},ct);
    public Task<ErpReceipt> Receipt(ErpTarget target,UploadItem item,CancellationToken ct)=>Call<ErpReceipt>("receipt",Values(target,item),ct);
    private static Dictionary<string,string> Values(ErpTarget t,UploadItem i)=>new(){["key"]=i.Key,["doctype"]=t.DocType,["name"]=t.Name,["private"]=t.Private?"1":"0",["sha256"]=i.Hash,["revision"]=t.Revision};
    public async Task<ErpReceipt> Upload(ErpTarget target,UploadItem item,CancellationToken ct)
    {
        AttachmentPolicy.Validate(item.File);
        await using(var input=File.OpenRead(item.File))
        {
            string hash=Convert.ToHexString(await SHA256.HashDataAsync(input,ct)).ToLowerInvariant();
            if(hash!=item.Hash)throw new ArtifactChangedException("Retry artifact changed. Create a new save/send operation.");
        }
        var fields=Values(target,item);fields["filename"]=item.Name??Path.GetFileName(item.File);
        using var body=new StreamingFormFileContent(fields,"content",item.File);
        return await Post<ErpReceipt>("upload",body,ct);
    }
    public void Dispose()=>http.Dispose();
}
public static class UploadRunner
{
    public static async Task Run(UploadJournal journal,ErpClient client,Action persist,CancellationToken ct)
    {
        if(client.Site.Id!=journal.Target.SiteId||!string.IsNullOrEmpty(journal.Target.SiteOrigin)&&client.Site.Url!=journal.Target.SiteOrigin)throw new InvalidOperationException("Retry site differs from operation.");
        var candidates=journal.Items.Where(i=>i.State!=UploadState.Succeeded&&(i.State!=UploadState.Failed||i.Retryable)).ToArray();
        if(candidates.Length==0)return;
        var account=await client.Connect(ct);if(account.Account!=journal.Target.Account)throw new InvalidOperationException("Retry account differs from operation.");
        var current=await client.Document(journal.Target.DocType,journal.Target.Name,ct);
        if(current.Revision!=journal.Target.Revision)throw new InvalidOperationException("Document changed. Review and confirm it again before creating a new operation.");
        foreach(var item in candidates)
        {
            ct.ThrowIfCancellationRequested();
            try{
                // Every retry reconciles on the server before sending content. Failed reconciliation never causes a blind re-upload.
                var receipt=await client.Receipt(journal.Target,item,ct);
                if(receipt.FileId==null){item.State=UploadState.Unknown;item.Error=null;persist();receipt=await client.Upload(journal.Target,item,ct);}
                if(string.IsNullOrWhiteSpace(receipt.FileId))throw new InvalidDataException("Missing attachment receipt.");
                item.Receipt=receipt.FileId;item.State=UploadState.Succeeded;item.Error=null;persist();
            }catch(OperationCanceledException){item.State=UploadState.Unknown;item.Error="Interrupted. Verify server receipt before retry.";persist();throw;}
            catch(Exception ex) when(ex is AttachmentRejectedException or ArtifactChangedException)
            {item.State=UploadState.Failed;item.Retryable=false;item.Error=ex.Message;persist();}
            catch(ErpRequestException ex)
            {item.State=UploadState.Failed;item.Retryable=true;item.Error=ex.Message;persist();}
            catch(Exception ex) when(ex is not OutOfMemoryException){item.State=UploadState.Unknown;item.Retryable=true;item.Error="Not confirmed. Retry will verify the server receipt first.";persist();}
        }
    }
}

public static class AttachmentPolicy
{
    public const long MaxBytes=20L*1024*1024;
    public static void Validate(string file)
    {
        long bytes=new FileInfo(file).Length;
        if(bytes>MaxBytes)throw new AttachmentRejectedException($"Attachment is {Format(bytes)}; the ERPNext Bridge limit is 20 MiB. Choose a smaller PDF or send fewer pages.");
    }
    private static string Format(long bytes)=>$"{bytes/1024d/1024d:0.0} MiB";
}
public sealed class AttachmentRejectedException(string message) : IOException(message);
public sealed class ArtifactChangedException(string message) : IOException(message);
public sealed class ErpRequestException(HttpStatusCode statusCode,string message) : InvalidOperationException(message)
{public HttpStatusCode StatusCode{get;}=statusCode;}

internal sealed class StreamingFormFileContent : HttpContent
{
    private readonly IReadOnlyDictionary<string,string> fields;private readonly string fileField,file;
    public StreamingFormFileContent(IReadOnlyDictionary<string,string> fields,string fileField,string file)
    {this.fields=fields;this.fileField=fileField;this.file=file;Headers.ContentType=new("application/x-www-form-urlencoded");}
    protected override bool TryComputeLength(out long length){length=0;return false;}
    protected override async Task SerializeToStreamAsync(Stream stream,TransportContext? context)
    {
        bool first=true;
        foreach(var field in fields)
        {
            await WriteAscii(stream,$"{(first?"":"&")}{Uri.EscapeDataString(field.Key)}={Uri.EscapeDataString(field.Value)}");first=false;
        }
        await WriteAscii(stream,$"{(first?"":"&")}{Uri.EscapeDataString(fileField)}=");
        await using var input=File.OpenRead(file);byte[] buffer=new byte[49155];int carry=0;
        while(true)
        {
            int read=await input.ReadAsync(buffer.AsMemory(carry,buffer.Length-carry));if(read==0)break;
            int total=carry+read,complete=total-total%3;
            if(complete>0)await WriteBase64(stream,Convert.ToBase64String(buffer,0,complete));
            carry=total-complete;if(carry>0)Buffer.BlockCopy(buffer,complete,buffer,0,carry);
        }
        if(carry>0)await WriteBase64(stream,Convert.ToBase64String(buffer,0,carry));
    }
    private static Task WriteAscii(Stream stream,string value)=>stream.WriteAsync(Encoding.ASCII.GetBytes(value)).AsTask();
    private static Task WriteBase64(Stream stream,string value)=>WriteAscii(stream,value.Replace("+","%2B",StringComparison.Ordinal).Replace("/","%2F",StringComparison.Ordinal).Replace("=","%3D",StringComparison.Ordinal));
}


public static class ErpConnectionErrors
{
    public static string Describe(HttpStatusCode status)=>$"ERPNext HTTP {(int)status}. "+(status switch
    {
        HttpStatusCode.Unauthorized=>"Authentication failed. Check the API key and secret in Manage sites.",
        HttpStatusCode.Forbidden=>"Access denied. Check this API user's permissions and Bridge access.",
        HttpStatusCode.NotFound=>"Endpoint not found. Check the site URL and that Document Scanner Bridge is installed on this site.",
        _=>"The request could not be completed. Check Bridge installation, permissions, document changes and the server connection."
    })+" No scan data was discarded.";
}
