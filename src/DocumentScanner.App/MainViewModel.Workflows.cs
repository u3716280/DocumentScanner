using System.IO;
using System.Windows;
using DocumentScanner.Core;
using DocumentScanner.Infrastructure;
namespace DocumentScanner.App;
public sealed partial class MainViewModel
{
    private ScanProfile? activeProfile;
    private bool initialized;private readonly DestinationConfirmation confirmation=new();
    public RelayCommand NewDocumentCommand{get;}
    private bool showOriginal;
    public bool ShowOriginal{get=>showOriginal;set{if(Set(ref showOriginal,value))_=SetSelectionAsync(Pages.Where(p=>selection.Contains(p.Data.Id)).ToArray());}}
    private void NewDocument()
    {
        if(Pages.Count>0){var answer=MessageBox.Show("Keep this session for recovery before starting a new document? Yes = keep, No = discard.","New document",MessageBoxButton.YesNoCancel);if(answer==MessageBoxResult.Cancel)return;if(answer==MessageBoxResult.Yes)store.Save(session);else store.Delete(session.Id);}
        session=new();selection=[];Pages.Clear();Preview=null;PreviewInfo="";confirmation.Invalidate();Changed(nameof(PageSummary));RaiseCommands();
    }
    public RelayCommand SendCommand{get;}public RelayCommand SaveAndSendCommand{get;}public RelayCommand ProfileManagerCommand{get;}public RelayCommand RetryUploadCommand{get;}public RelayCommand ApplyPageToneCommand{get;}
    private int pageBrightness,pageContrast;
    public int PageBrightness{get=>pageBrightness;set=>Set(ref pageBrightness,Math.Clamp(value,-1000,1000));}
    public int PageContrast{get=>pageContrast;set=>Set(ref pageContrast,Math.Clamp(value,-1000,1000));}
    public string ProfileState=>SelectedProfile==null?"No active profile":CurrentSettings()==SelectedProfile.Settings&&settings.Output==SelectedProfile.Output&&settings.Erp==SelectedProfile.Erp&&SelectedDevice?.Key==SelectedProfile.Device.Key?"Saved settings":"Modified · save changes in Profile Manager";
    public ExportFormat[] OutputFormats=>Enum.GetValues<ExportFormat>();
    public ExportFormat OutputFormat{get=>settings.Output.Format;set{settings=settings with{Output=settings.Output with{Format=value}};Changed();Changed(nameof(FilenamePreview));}}
    public string FilenamePrefix{get=>settings.Output.Prefix;set{settings=settings with{Output=settings.Output with{Prefix=value}};Changed();Changed(nameof(FilenamePreview));}}
    public string FilenamePreview{get{try{return OutputNaming.Resolve(settings.Output,DateTime.Now,1)+"."+OutputFormat.ToString().ToLowerInvariant();}catch(ArgumentException){return "Invalid filename prefix/pattern — review output settings";}}}
    private string JournalPath=>Path.Combine(store.Folder(session.Id),"upload-job.json");
    private async Task ManageProfiles()
    {
        confirmation.Invalidate();var current=SelectedDevice==null?null:new ScanProfile("New profile",SelectedDevice,CurrentSettings()){Output=settings.Output,Erp=settings.Erp};
        var manager=new ProfileManagerWindow(settings with{Profiles=Profiles.ToList()},Devices.ToList(),SelectedProfile,current,value=>settingsStore.Save(value)){Owner=Application.Current.MainWindow};manager.ShowDialog();
        settings=manager.ResultSettings;activeProfile=settings.Profiles.FirstOrDefault(p=>p.Id==activeProfile?.Id);Profiles.Clear();foreach(var p in settings.Profiles)Profiles.Add(p);selectedProfile=Profiles.FirstOrDefault(p=>p.Id==SelectedProfile?.Id);Changed(nameof(SelectedProfile));
        if(manager.LoadRequested is {} chosen){SelectedProfile=Profiles.First(p=>p.Id==chosen.Id);await LoadProfile();}
        foreach(var property in new[]{nameof(ProfileState),nameof(OutputFormat),nameof(FilenamePrefix),nameof(FilenamePreview)})Changed(property);
    }
    private ErpClient Client(ErpTarget target)
    {
        var site=settings.Sites.SingleOrDefault(s=>s.Id==target.SiteId)??throw new InvalidOperationException("ERPNext site reference is unresolved. Open Profile Manager → ERPNext → Manage sites.");
        if(!string.IsNullOrEmpty(target.SiteOrigin)&&site.Url!=target.SiteOrigin)throw new InvalidOperationException("Site URL changed since this operation. Review its destination before creating a new operation.");
        return new(site,WindowsCredentialStore.Load(site.Id)??throw new InvalidOperationException("Missing credentials for this Windows user. Configure the site in Profile Manager."));
    }
    private ErpTarget? ChooseDestination(ErpDefaults defaults,string summary)
    {
        confirmation.Invalidate();
        if(settings.Sites.Count==0){var sites=new SitesWindow(settings.Sites){Owner=Application.Current.MainWindow};sites.ShowDialog();settings=settings with{Sites=sites.Sites};PersistSettings();}
        if(settings.Sites.Count==0)throw new InvalidOperationException("Create an ERPNext site profile first.");
        var window=new ErpDestinationWindow(settings.Sites,defaults,session.Id,summary){Owner=Application.Current.MainWindow};if(window.ShowDialog()!=true||window.Target==null)return null;
        confirmation.Confirm(window.Target);return window.Target;
    }
    private async Task SaveActionAsync(SaveAction action)
    {
        var chosen=session.Pages.Where(p=>selection.Contains(p.Id)).ToArray();if(chosen.Length==0)throw new ArgumentException("Select pages to save/send.");
        if(action!=SaveAction.Local&&File.Exists(JournalPath))
        {
            var previous=JsonFiles.Read<UploadJournal>(JournalPath);
            if(previous.Items.Any(i=>i.State!=UploadState.Succeeded)||previous.NeedsLocal&&!previous.LocalComplete){if(MessageBox.Show("An unfinished job exists for this session. Yes: retry its frozen files. No: keep that job and cancel this new action.","Pending delivery",MessageBoxButton.YesNo)==MessageBoxResult.Yes)await RetryUpload();return;}
        }
        string outbox=Path.Combine(store.Folder(session.Id),"outbox",Guid.NewGuid().ToString("N"));
        var legacy=settings with{OutputFolder=action==SaveAction.Erp?outbox:settings.Output.Folder,Format=settings.Output.Format,JpegQuality=settings.Output.JpegQuality};
        var dialog=new ExportWindow(legacy,chosen.Length,action==SaveAction.Erp){Owner=Application.Current.MainWindow};if(dialog.ShowDialog()!=true)return;var options=dialog.Options!;
        settings=settings with{Output=settings.Output with{Folder=action==SaveAction.Erp?settings.Output.Folder:options.Folder,Format=options.Format,JpegQuality=options.Quality}};PersistSettings();
        if(action==SaveAction.Local){var result=await StaTask.Run(()=>exporter.ExportAsync(chosen,store.Folder(session.Id),options,null,Token));new SaveResultsWindow(result.Saved,result.Error,null){Owner=Application.Current.MainWindow}.ShowDialog();return;}
        var target=ChooseDestination(settings.Erp,$"Selected: {chosen.Length} pages · {options.Format} · {options.BaseName}");if(target==null)return;
        using(var client=Client(target)){var fresh=await client.Document(target.DocType,target.Name,Token);if(fresh.Revision!=target.Revision){confirmation.Invalidate();throw new InvalidOperationException("Destination changed. Review and confirm again.");}}
        if(!ReviewSend(target,$"{chosen.Length} selected pages · {options.Format} · {options.BaseName}",action==SaveAction.LocalAndErp))return;
        // Render once into the recoverable session; both destinations use these frozen artifact bytes.
        var rendered=await StaTask.Run(()=>exporter.ExportAsync(chosen,store.Folder(session.Id),options with{Folder=outbox},null,Token));
        if(rendered.Error!=null||rendered.Cancelled)throw new InvalidOperationException("Output preparation did not complete. Original pages are preserved; no upload started.");
        var journal=new UploadJournal{Target=target,NeedsLocal=action==SaveAction.LocalAndErp,LocalFolder=action==SaveAction.LocalAndErp?options.Folder:null,LocalComplete=false,Items=rendered.Saved.Select(path=>new UploadItem{File=path,Hash=LocalDelivery.Hash(path)}).ToList()};
        JsonFiles.Write(JournalPath,journal);await Deliver(journal);
    }
    private async Task Deliver(UploadJournal journal)
    {
        string path=JournalPath;void Persist()=>JsonFiles.Write(path,journal);
        string? error=null;
        try{
            if(!journal.NeedsLocal||LocalDelivery.Complete(journal,Persist))
            {
                using var client=Client(journal.Target);Status="Verifying destination and attachment receipts…";await UploadRunner.Run(journal,client,Persist,Token);
            }
        }catch(Exception e) when(e is not OutOfMemoryException){error=e is OperationCanceledException?"Cancelled. Retry will verify server results first.":e.Message;}
        var results=new SaveResultsWindow(journal.LocalFiles,journal.LocalError,journal,error){Owner=Application.Current.MainWindow};results.ShowDialog();RaiseCommands();
        if(results.RetryRequested&&!Token.IsCancellationRequested)await RetryUpload();
    }
    private bool ReviewSend(ErpTarget target,string summary,bool localFirst)
    {
        var w=new Window{Title="Review confirmed delivery",Width=620,Height=400,Owner=Application.Current.MainWindow,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var p=Ui.Panel();p.Children.Add(new System.Windows.Controls.TextBlock{Text="Destination confirmed",FontSize=22,FontWeight=FontWeights.SemiBold});
        p.Children.Add(Ui.Note($"{target.SiteOrigin}\nAccount: {target.Account}\n{target.DocType} · {target.Name}\nAttachment: {(target.Private?"Private":"Public")}\n\n{summary}"));
        p.Children.Add(Ui.Button("Cancel",()=>w.DialogResult=false));p.Children.Add(Ui.Button(localFirst?"Save locally, then send":"Send now",()=>w.DialogResult=true,true));w.Content=new System.Windows.Controls.ScrollViewer{Content=p,VerticalScrollBarVisibility=System.Windows.Controls.ScrollBarVisibility.Auto};return w.ShowDialog()==true;
    }
    private async Task RetryUpload()
    {
        var journal=JsonFiles.Read<UploadJournal>(JournalPath);if(journal.SchemaVersion!=1||journal.Target.SessionId!=session.Id)throw new InvalidDataException("Invalid delivery journal.");
        // Journal paths must stay under this session, even if a manifest is edited externally.
        string root=Path.GetFullPath(store.Folder(session.Id))+Path.DirectorySeparatorChar;
        if(journal.Items.Any(i=>!Path.GetFullPath(i.File).StartsWith(root,StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("Unsafe retry artifact path.");
        var target=ChooseDestination(new(){SiteId=journal.Target.SiteId,DocType=journal.Target.DocType,Private=journal.Target.Private},$"Retry frozen job: {journal.Items.Count} files. Confirm the same destination.");if(target==null)return;
        var old=journal.Target;if(target.SiteId!=old.SiteId||target.SiteOrigin!=old.SiteOrigin||target.Account!=old.Account||target.DocType!=old.DocType||target.Name!=old.Name||target.Private!=old.Private)throw new InvalidOperationException("Retry destination/account/privacy differs. No upload started.");
        if(!ReviewSend(target,$"Retry {journal.Items.Count(i=>i.State!=UploadState.Succeeded)} unresolved files. Server receipts will be checked before sending.",journal.NeedsLocal&&!journal.LocalComplete))return;
        journal=journal with{Target=target};JsonFiles.Write(JournalPath,journal);await Deliver(journal);
    }
}
