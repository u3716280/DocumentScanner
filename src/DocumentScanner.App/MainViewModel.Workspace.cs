using System.IO;
using System.Diagnostics;
using DocumentScanner.Core;
using DocumentScanner.Infrastructure;
namespace DocumentScanner.App;
public sealed partial class MainViewModel
{
    public RelayCommand OpenDestinationCommand{get;private set;}=null!;
    public RelayCommand ResetPageToneCommand{get;private set;}=null!;
    private ErpDestination? destination;
    /// <summary>The ERPNext document this session scans for. Null when the application was opened directly.</summary>
    public ErpDestination? Destination=>destination;
    public bool HasDestination=>destination!=null;
    public bool NoDestination=>destination==null;
    public bool CanUpload=>destination!=null&&DocStatusRules.AllowsUpload(destination.DocStatus);
    public string DestinationType=>destination?.DocType??"";
    public string DestinationNumber=>destination?.DocName??"";
    public string DestinationParty=>destination?.Party??"";
    public string DestinationDetails=>destination==null?"":string.Join("\n",new[]{destination.DisplayDate,DocStatusRules.Label(destination.DocStatus)+(destination.Status.Length>0&&!string.Equals(destination.Status,DocStatusRules.Label(destination.DocStatus),StringComparison.OrdinalIgnoreCase)?$" ({destination.Status})":""),(destination.Total+" "+destination.Currency).Trim(),destination.Reference}.Where(part=>part.Length>0));
    public string DestinationAccount=>destination==null?"":$"Sending as {destination.Account}\n{destination.SiteOrigin}";
    public string DestinationWarning=>destination!=null&&!CanUpload?DocStatusRules.CancelledMessage:"";
    public string DirectLaunchHint=>SaveActionRules.DirectLaunchMessage;
    // One primary button: Send when the scan was started from ERPNext, otherwise Save to folder.
    public Wpf.Ui.Controls.ControlAppearance SendAppearance=>SaveActionRules.Primary(CanUpload)==SaveAction.Erp?Wpf.Ui.Controls.ControlAppearance.Primary:Wpf.Ui.Controls.ControlAppearance.Secondary;
    public Wpf.Ui.Controls.ControlAppearance SaveAppearance=>SaveActionRules.Primary(CanUpload)==SaveAction.Local?Wpf.Ui.Controls.ControlAppearance.Primary:Wpf.Ui.Controls.ControlAppearance.Secondary;
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
        OpenDestinationCommand=Command(()=>{if(destination!=null)Process.Start(new ProcessStartInfo(destination.DocumentUrl){UseShellExecute=true});},()=>destination!=null);
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
    /// <summary>Opens Settings and stores what was changed there.</summary>
    public void OpenSettings(string dataFolder)
    {
        if(Busy)return;
        var window=new SettingsWindow(settings.Sites,settings.AttachmentDescriptions,dataFolder){Owner=System.Windows.Application.Current?.MainWindow};window.ShowDialog();
        settings=settings with{Sites=window.Sites,AttachmentDescriptions=window.Descriptions};PersistSettings();
    }
    private void RefreshPendingDelivery()
    {
        try
        {
            var job=ReadJournal(session.Id);
            if(job==null)HasPendingDelivery=false;
            else if(DeliveryStatus.IsTerminal(job)){CleanupDeliveryFiles(session.Id);HasPendingDelivery=false;}
            else HasPendingDelivery=true;
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException){HasPendingDelivery=true;}
        RetryUploadCommand.Raise();
    }
}
