using System.IO;
using System.Diagnostics;
using DocumentScanner.Core;
using DocumentScanner.Infrastructure;
namespace DocumentScanner.App;
public sealed partial class MainViewModel
{
    public const string BridgeSetupUrl="https://github.com/u3716280/document_scanner_bridge/blob/main/docs/INSTALL.md";
    public RelayCommand BridgeGuideCommand{get;private set;}=null!;
    public RelayCommand TestErpConnectionCommand{get;private set;}=null!;
    public RelayCommand ManageErpSitesCommand{get;private set;}=null!;
    public RelayCommand ResetPageToneCommand{get;private set;}=null!;
    private string bridgeStatus="Bridge not checked";
    public string BridgeStatus{get=>bridgeStatus;private set=>Set(ref bridgeStatus,value);}
    public bool ScannerReady=>NotBusy&&SelectedDevice!=null&&scannerAvailable;
    public string ScannerStatus=>Busy?"Working…":ScannerReady?"Ready":"Not ready";
    public bool HasSelection=>selection.Count>0;
    public string ScanButtonText=>Pages.Count==0?"Scan":"Scan more";
    public string PagesHeading=>$"Pages · {Pages.Count}";
    public string SelectionSummary=>$"{selection.Count} {(selection.Count==1?"page":"pages")} selected";
    public string SessionSummary=>$"{Pages.Count} {(Pages.Count==1?"page":"pages")} in session · Originals preserved";
    public string PreviewTitle
    {
        get{int index=session.Pages.ToList().FindIndex(p=>p.Id==selection.LastOrDefault());return index<0?"Document preview":$"Page {index+1} of {Pages.Count}";}
    }
    public string PreviewEmptyTitle=>Pages.Count==0?"Ready to scan":"Select a page to review";
    public string PreviewEmptyHint=>Pages.Count==0?"Choose a scanner or saved profile, then Scan. You can also import images.":"Select pages in the list. Checked pages are included when you save or send.";
    public bool ShowAdjusted{get=>!ShowOriginal;set=>ShowOriginal=!value;}
    private bool hasPendingDelivery;
    public bool HasPendingDelivery{get=>hasPendingDelivery;private set=>Set(ref hasPendingDelivery,value);}
    private void InitializeWorkspace()
    {
        BridgeGuideCommand=Command(()=>Process.Start(new ProcessStartInfo(BridgeSetupUrl){UseShellExecute=true}));
        ManageErpSitesCommand=Command(()=>_=Operation(()=>{ManageErpSites();return Task.CompletedTask;}));
        TestErpConnectionCommand=Command(()=>_=Operation(TestErpConnection));
        ResetPageToneCommand=Command(()=>{PageBrightness=0;PageContrast=0;Edit(()=>session.SetTone(selection,0,0));},()=>NotBusy&&HasSelection);
        Pages.CollectionChanged+=(_,_)=>NotifyWorkspace();
        PropertyChanged+=(_,e)=>{
            if(e.PropertyName==nameof(PageSummary))NotifyWorkspace();
            if(e.PropertyName is nameof(Busy) or nameof(SelectedDevice)){Changed(nameof(ScannerReady));Changed(nameof(ScannerStatus));}
        };
    }
    private void NotifyWorkspace()
    {
        foreach(string property in new[]{nameof(HasSelection),nameof(ScanButtonText),nameof(PagesHeading),nameof(SelectionSummary),nameof(SessionSummary),nameof(PreviewTitle),nameof(PreviewEmptyTitle),nameof(PreviewEmptyHint)})Changed(property);
    }
    private void ResetBridgeStatus()=>BridgeStatus="Bridge not checked";
    private void ManageErpSites()
    {
        confirmation.Invalidate();
        var window=new SitesWindow(settings.Sites,settings.Erp.SiteId){Owner=System.Windows.Application.Current?.MainWindow};window.ShowDialog();
        settings=settings with{Sites=window.Sites,Erp=settings.Erp with{SiteId=window.SelectedSite?.Id??settings.Erp.SiteId}};
        ResetBridgeStatus();PersistSettings();Changed(nameof(ProfileState));
    }
    private async Task TestErpConnection()
    {
        var site=settings.Sites.FirstOrDefault(s=>s.Id==settings.Erp.SiteId);
        if(site==null){ManageErpSites();site=settings.Sites.FirstOrDefault(s=>s.Id==settings.Erp.SiteId);}
        if(site==null){BridgeStatus="No site selected · Open Manage sites";return;}
        BridgeStatus="Checking Bridge…";
        try
        {
            var credentials=WindowsCredentialStore.Load(site.Id)??throw new InvalidOperationException("No API credentials for this Windows user. Open Manage sites / credentials.");
            using var client=new ErpClient(site,credentials);var result=await client.Connect(Token);
            BridgeStatus=$"Bridge {result.Version} · Connected as {result.Account}";
        }
        catch(OperationCanceledException){BridgeStatus="Bridge check interrupted · Not verified";throw;}
        catch(Exception){BridgeStatus="Connection not verified · Check site, Bridge and credentials";throw;}
    }
    private void RefreshPendingDelivery()
    {
        try
        {
            if(!File.Exists(JournalPath)){HasPendingDelivery=false;return;}
            var job=JsonFiles.Read<UploadJournal>(JournalPath);
            HasPendingDelivery=job.NeedsLocal&&!job.LocalComplete||job.Items.Any(i=>i.State!=UploadState.Succeeded);
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException){HasPendingDelivery=true;}
        RetryUploadCommand.Raise();
    }
}
