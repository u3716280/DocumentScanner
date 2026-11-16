using System.Security.Cryptography;
using System.Text;
namespace DocumentScanner.Core;

/// <summary>An ERPNext site the user has allowed. Scan links for any other site are refused.</summary>
public sealed record ErpSite
{
    public Guid Id{get;init;}=Guid.NewGuid();
    public string Name{get;init;}="ERPNext";
    public string Url{get;init;}="";
    public void Validate(){if(Id==Guid.Empty||string.IsNullOrWhiteSpace(Name)||!Uri.TryCreate(Url,UriKind.Absolute,out var uri)||uri.Scheme!="https"||!string.IsNullOrEmpty(uri.UserInfo)||!string.IsNullOrEmpty(uri.Query)||!string.IsNullOrEmpty(uri.Fragment)||uri.AbsolutePath!="/")throw new ArgumentException("Use an HTTPS ERPNext origin, e.g. https://erp.example.com, without a path, query or credentials.");}
}

/// <summary>
/// The document a scan link named, as ERPNext last reported it. It is fixed for the session: the user can
/// neither edit it nor pick another document from inside the application.
/// </summary>
public sealed record ErpDestination
{
    public required Guid SiteId{get;init;}
    public required string SiteOrigin{get;init;}
    public required string Account{get;init;}
    public required string DocType{get;init;}
    public required string DocName{get;init;}
    public string Party{get;init;}="";
    public string Date{get;init;}="";
    public string Reference{get;init;}="";
    public string Total{get;init;}="";
    public string Currency{get;init;}="";
    public string Status{get;init;}="";
    public int DocStatus{get;init;}
    /// <summary>ERPNext's "modified" stamp when the document was last read.</summary>
    public string Modified{get;init;}="";
    public string DocumentUrl=>$"{SiteOrigin}/app/{DocType.ToLowerInvariant().Replace(' ','-')}/{Uri.EscapeDataString(DocName)}";
    public bool SameTarget(ErpDestination? other)=>other!=null&&SiteId==other.SiteId&&SiteOrigins.Same(SiteOrigin,other.SiteOrigin)&&DocType==other.DocType&&DocName==other.DocName;
}

public static class DocStatusRules
{
    /// <summary>Draft and submitted documents accept attachments from this application; cancelled ones never do.</summary>
    public static bool AllowsUpload(int docstatus)=>docstatus is 0 or 1;
    public static string Label(int docstatus)=>docstatus switch{0=>"Draft",1=>"Submitted",2=>"Cancelled",_=>"Unknown"};
    public const string CancelledMessage="This document is cancelled in ERPNext, so nothing was sent. Your scanned pages are kept and can still be saved to a folder.";
}

public enum SaveAction{Local,Erp,LocalAndErp}
public enum UploadState{Pending,Unknown,Succeeded,Failed}

/// <summary>Which of the three save buttons are available, and which one leads.</summary>
public static class SaveActionRules
{
    public const string DirectLaunchMessage="Sending to ERPNext starts from the Scan button on a Sales Invoice, Purchase Receipt or Purchase Invoice.";
    public static bool Enabled(SaveAction action,bool hasDestination,bool hasSelection,bool busy)=>
        !busy&&hasSelection&&(action==SaveAction.Local||hasDestination);
    public static SaveAction Primary(bool hasDestination)=>hasDestination?SaveAction.Erp:SaveAction.Local;
}

/// <summary>One frozen output file of a delivery and what is known about each place it has to reach.</summary>
public sealed record DeliveryItem
{
    public string Key{get;init;}=Guid.NewGuid().ToString("N");
    /// <summary>The rendered file inside the session's outbox. Never rewritten after it is created.</summary>
    public required string File{get;init;}
    public required string Sha256{get;init;}
    /// <summary>Frappe identifies file content by MD5; used only to recognise an attachment that already exists.</summary>
    public required string Md5{get;init;}
    public long Size{get;init;}
    /// <summary>1-based page for an image, null for a PDF.</summary>
    public int? Page{get;init;}
    /// <summary>The name both destinations receive. Chosen before anything is written.</summary>
    public string Name{get;set;}="";
    public string? LocalDestination{get;set;}
    public bool LocalDone{get;set;}
    public UploadState State{get;set;}
    public bool Retryable{get;set;}=true;
    /// <summary>ERPNext File record, once the attachment is confirmed.</summary>
    public string? ServerFile{get;set;}
    public string? ServerFileName{get;set;}
    public string? ServerUrl{get;set;}
    public string? Error{get;set;}
    public int Attempts{get;set;}
}

public sealed record DeliveryJournal
{
    public const int CurrentSchema=2;
    public int SchemaVersion{get;init;}=CurrentSchema;
    public Guid OperationId{get;init;}=Guid.NewGuid();
    public DateTimeOffset Created{get;init;}=DateTimeOffset.UtcNow;
    public required Guid SessionId{get;init;}
    public required SaveAction Action{get;init;}
    public required ExportFormat Format{get;init;}
    /// <summary>Null only for a local save that was not started from ERPNext.</summary>
    public ErpDestination? Destination{get;set;}
    /// <summary>Fingerprint of the selected pages, their order, their edits and the format. A different value is a new job.</summary>
    public required string Revision{get;init;}
    public List<Guid> PageIds{get;init;}=[];
    public required List<DeliveryItem> Items{get;init;}
    public string? LocalFolder{get;init;}
    public bool LocalComplete{get;set;}
    public string? LocalError{get;set;}
    public string? LastError{get;set;}
    public int RetryCount{get;set;}
    public bool NeedsLocal=>Action!=SaveAction.Erp;
    public bool NeedsUpload=>Action!=SaveAction.Local;
}

public enum DeliveryOutcome{Completed,PartiallyCompleted,RetryRequired,Failed}

public static class DeliveryStatus
{
    private static bool UploadOpen(DeliveryItem item)=>item.State is UploadState.Pending or UploadState.Unknown||item.State==UploadState.Failed&&item.Retryable;
    public static bool LocalOpen(DeliveryJournal journal)=>journal.NeedsLocal&&!journal.LocalComplete;
    public static bool HasRetryableWork(DeliveryJournal journal)=>LocalOpen(journal)||journal.NeedsUpload&&journal.Items.Any(UploadOpen);
    public static bool IsTerminal(DeliveryJournal journal)=>!HasRetryableWork(journal);
    public static bool HasUnknown(DeliveryJournal journal)=>journal.NeedsUpload&&journal.Items.Any(i=>i.State==UploadState.Unknown);

    /// <summary>"Completed" only when every file reached every destination it was meant for.</summary>
    public static DeliveryOutcome Outcome(DeliveryJournal journal)
    {
        bool localDone=!journal.NeedsLocal||journal.LocalComplete;
        bool uploadDone=!journal.NeedsUpload||journal.Items.All(i=>i.State==UploadState.Succeeded);
        if(localDone&&uploadDone)return DeliveryOutcome.Completed;
        bool anything=journal.NeedsLocal&&journal.Items.Any(i=>i.LocalDone)||journal.NeedsUpload&&journal.Items.Any(i=>i.State==UploadState.Succeeded);
        if(HasRetryableWork(journal))return anything?DeliveryOutcome.PartiallyCompleted:DeliveryOutcome.RetryRequired;
        return anything?DeliveryOutcome.PartiallyCompleted:DeliveryOutcome.Failed;
    }

    public static string Text(DeliveryOutcome outcome)=>outcome switch
    {
        DeliveryOutcome.Completed=>"Completed",
        DeliveryOutcome.PartiallyCompleted=>"Partially completed",
        DeliveryOutcome.RetryRequired=>"Retry required",
        _=>"Not completed",
    };
}

public static class DeliveryRevision
{
    /// <summary>Changes when the pages, their order, any page edit or the output format changes.</summary>
    public static string Compute(IEnumerable<PageData> pages,ExportFormat format,OutputPaper paper,bool fit,int quality)
    {
        var text=new StringBuilder().Append(format).Append('|').Append(paper).Append('|').Append(fit).Append('|').Append(format==ExportFormat.Jpg?quality:0);
        foreach(var page in pages)text.Append('\n').Append(page);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()))).ToLowerInvariant();
    }
}
