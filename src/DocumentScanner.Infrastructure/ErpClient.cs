using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
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
        using var response=await http.PostAsync("api/method/document_scanner_bridge.api."+method,body,ct);
        if(!response.IsSuccessStatusCode)throw new InvalidOperationException($"ERPNext HTTP {(int)response.StatusCode}. Check bridge installation, account permissions, document changes and connection. No scan data was discarded.");
        using var result=JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if(!result.RootElement.TryGetProperty("message",out var message))throw new InvalidDataException("Invalid bridge response.");
        return message.Deserialize<T>(json)??throw new InvalidDataException("Empty bridge response.");
    }
    public Task<ErpConnection> Connect(CancellationToken ct)=>Call<ErpConnection>("ping",ct:ct);
    public Task<ErpSearch> Search(string doctype,string query,string field,int start,CancellationToken ct)=>Call<ErpSearch>("search_documents",new(){["doctype"]=doctype,["query"]=query,["field"]=field,["start"]=start.ToString()},ct);
    public Task<ErpDocument> Document(string doctype,string name,CancellationToken ct)=>Call<ErpDocument>("document",new(){["doctype"]=doctype,["name"]=name},ct);
    public Task<ErpReceipt> Receipt(ErpTarget target,UploadItem item,CancellationToken ct)=>Call<ErpReceipt>("receipt",Values(target,item),ct);
    private static Dictionary<string,string> Values(ErpTarget t,UploadItem i)=>new(){["key"]=i.Key,["doctype"]=t.DocType,["name"]=t.Name,["private"]=t.Private?"1":"0",["sha256"]=i.Hash,["revision"]=t.Revision};
    public async Task<ErpReceipt> Upload(ErpTarget target,UploadItem item,CancellationToken ct)
    {
        var bytes=await File.ReadAllBytesAsync(item.File,ct);if(bytes.Length>20*1024*1024)throw new ArgumentException("Attachment exceeds the 20 MB bridge limit.");
        string hash=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();if(hash!=item.Hash)throw new InvalidDataException("Retry artifact changed. Create a new save/send operation.");
        var fields=Values(target,item);fields["filename"]=item.Name??Path.GetFileName(item.File);fields["content"]=Convert.ToBase64String(bytes);
        return await Call<ErpReceipt>("upload",fields,ct);
    }
    public void Dispose()=>http.Dispose();
}
public static class UploadRunner
{
    public static async Task Run(UploadJournal journal,ErpClient client,Action persist,CancellationToken ct)
    {
        if(client.Site.Id!=journal.Target.SiteId||!string.IsNullOrEmpty(journal.Target.SiteOrigin)&&client.Site.Url!=journal.Target.SiteOrigin)throw new InvalidOperationException("Retry site differs from operation.");
        var account=await client.Connect(ct);if(account.Account!=journal.Target.Account)throw new InvalidOperationException("Retry account differs from operation.");
        var current=await client.Document(journal.Target.DocType,journal.Target.Name,ct);
        if(current.Revision!=journal.Target.Revision)throw new InvalidOperationException("Document changed. Review and confirm it again before creating a new operation.");
        foreach(var item in journal.Items.Where(i=>i.State!=UploadState.Succeeded))
        {
            ct.ThrowIfCancellationRequested();
            try{
                // Every retry reconciles on the server before sending content. Failed reconciliation never causes a blind re-upload.
                var receipt=await client.Receipt(journal.Target,item,ct);
                if(receipt.FileId==null){item.State=UploadState.Unknown;item.Error=null;persist();receipt=await client.Upload(journal.Target,item,ct);}
                if(string.IsNullOrWhiteSpace(receipt.FileId))throw new InvalidDataException("Missing attachment receipt.");
                item.Receipt=receipt.FileId;item.State=UploadState.Succeeded;item.Error=null;persist();
            }catch(OperationCanceledException){item.State=UploadState.Unknown;item.Error="Interrupted. Verify server receipt before retry.";persist();throw;}
            catch(Exception ex) when(ex is not OutOfMemoryException){item.State=UploadState.Unknown;item.Error="Not confirmed. Retry will verify the server receipt first.";persist();}
        }
    }
}
