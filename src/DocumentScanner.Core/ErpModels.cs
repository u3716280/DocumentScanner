namespace DocumentScanner.Core;
public sealed record ErpSite
{
    public Guid Id{get;init;}=Guid.NewGuid();
    public string Name{get;init;}="ERPNext";
    public string Url{get;init;}="";
    public void Validate(){if(Id==Guid.Empty||string.IsNullOrWhiteSpace(Name)||!Uri.TryCreate(Url,UriKind.Absolute,out var uri)||uri.Scheme!="https"||!string.IsNullOrEmpty(uri.UserInfo)||!string.IsNullOrEmpty(uri.Query)||!string.IsNullOrEmpty(uri.Fragment)||uri.AbsolutePath!="/")throw new ArgumentException("Use an HTTPS ERPNext origin, e.g. https://erp.example.com, without a path, query or credentials.");}
}
public sealed record ErpDocument(string DocType,string Name,string Party,string Description,string Company,string Date,string Status,string Total,string Revision);
public sealed record ErpTarget(Guid SessionId,Guid SiteId,string Account,string DocType,string Name,bool Private,string Revision,string SiteOrigin="");
public sealed class DestinationConfirmation
{
    public ErpTarget? Target{get;private set;}
    public void Confirm(ErpTarget target){if(string.IsNullOrWhiteSpace(target.Name)||string.IsNullOrWhiteSpace(target.Account)||string.IsNullOrWhiteSpace(target.Revision))throw new ArgumentException("A verified document is required.");Target=target;}
    public void Invalidate()=>Target=null;
    public bool Matches(ErpTarget target)=>Target==target;
}
public enum SaveAction{Local,Erp,LocalAndErp}
public enum UploadState{Pending,Unknown,Succeeded,Failed}
public sealed record UploadItem
{
    public string Key{get;init;}=Guid.NewGuid().ToString("N");
    public required string File{get;init;}
    public required string Hash{get;init;}
    public string? Name{get;set;}
    public string? LocalDestination{get;set;}
    public UploadState State{get;set;}
    public bool Retryable{get;set;}=true;
    public string? Receipt{get;set;}
    public string? Error{get;set;}
}
public sealed record UploadJournal
{
    public int SchemaVersion{get;init;}=1;
    public required ErpTarget Target{get;init;}
    public required List<UploadItem> Items{get;init;}
    public List<string> LocalFiles{get;init;}=[];
    public bool NeedsLocal{get;init;}
    public string? LocalFolder{get;init;}
    public bool LocalComplete{get;set;}
    public string? LocalError{get;set;}
}

public static class DeliveryStatus
{
    public static bool HasRetryableWork(UploadJournal journal)=>
        journal.NeedsLocal&&!journal.LocalComplete||
        journal.Items.Any(item=>item.State is UploadState.Pending or UploadState.Unknown||item.State==UploadState.Failed&&item.Retryable);

    public static bool IsTerminal(UploadJournal journal)=>
        (!journal.NeedsLocal||journal.LocalComplete)&&
        journal.Items.All(item=>item.State==UploadState.Succeeded||item.State==UploadState.Failed&&!item.Retryable);
}
