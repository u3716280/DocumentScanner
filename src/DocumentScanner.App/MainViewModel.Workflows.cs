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
    public bool ShowOriginal{get=>showOriginal;set{if(Set(ref showOriginal,value)){Changed(nameof(ShowAdjusted));_=SetSelectionAsync(Pages.Where(p=>selection.Contains(p.Data.Id)).ToArray());}}}
    private void NewDocument()
    {
        if(Pages.Count>0){var answer=MessageBox.Show("Keep this session for recovery before starting a new document? Yes = keep, No = discard.","New document",MessageBoxButton.YesNoCancel);if(answer==MessageBoxResult.Cancel)return;if(answer==MessageBoxResult.Yes)store.Save(session);else{if(!ConfirmDiscardSession(session.Id))return;store.Delete(session.Id);}}
        else if(Directory.Exists(store.Folder(session.Id))){if(!ConfirmDiscardSession(session.Id))return;store.Delete(session.Id);}
        session=new();selection=[];Pages.Clear();Preview=null;PreviewInfo="";confirmation.Invalidate();RefreshPendingDelivery();Changed(nameof(PageSummary));RaiseCommands();
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
    private string JournalPathFor(Guid id)=>Path.Combine(store.Folder(id),"upload-job.json");
    private bool ConfirmDiscardSession(Guid id)
    {
        string path=JournalPathFor(id);if(!File.Exists(path))return true;
        try{if(DeliveryStatus.IsTerminal(JsonFiles.Read<UploadJournal>(path)))return true;}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException){}
        return MessageBox.Show("This session has an unfinished ERPNext delivery. Discarding it removes the frozen retry files. An Unknown upload may already exist in ERPNext.\n\nDiscard this session?","Pending ERPNext delivery",MessageBoxButton.YesNo,MessageBoxImage.Warning)==MessageBoxResult.Yes;
    }
    private void CleanupDeliveryFiles(Guid id)
    {
        string folder=store.Folder(id),outbox=Path.Combine(folder,"outbox"),journal=JournalPathFor(id);
        if(Directory.Exists(outbox))Directory.Delete(outbox,true);if(File.Exists(journal))File.Delete(journal);
    }
    private bool ConfirmAbandon(UploadJournal journal)
    {
        string warning=journal.Items.Any(i=>i.State==UploadState.Unknown)?" At least one result is Unknown and may already exist in ERPNext; sending again can create a duplicate.":"";
        return MessageBox.Show("Abandon this pending delivery and remove its retry files?"+warning,"Abandon pending delivery",MessageBoxButton.YesNo,MessageBoxImage.Warning)==MessageBoxResult.Yes;
    }
    private async Task ManageProfiles()
    {
        confirmation.Invalidate();var current=SelectedDevice==null?null:new ScanProfile("New profile",SelectedDevice,CurrentSettings()){Output=settings.Output,Erp=settings.Erp};
        var manager=new ProfileManagerWindow(settings with{Profiles=Profiles.ToList()},Devices.ToList(),SelectedProfile,current,value=>settingsStore.Save(value)){Owner=Application.Current.MainWindow};manager.ShowDialog();
        settings=manager.ResultSettings;ResetBridgeStatus();activeProfile=settings.Profiles.FirstOrDefault(p=>p.Id==activeProfile?.Id);Profiles.Clear();foreach(var p in settings.Profiles)Profiles.Add(p);selectedProfile=Profiles.FirstOrDefault(p=>p.Id==SelectedProfile?.Id);Changed(nameof(SelectedProfile));
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
        try{await SaveActionCoreAsync(action);}
        finally{confirmation.Invalidate();RefreshPendingDelivery();}
    }
    private async Task SaveActionCoreAsync(SaveAction action)
    {
        var chosen=session.Pages.Where(p=>selection.Contains(p.Id)).ToArray();if(chosen.Length==0)throw new ArgumentException("Select pages to save/send.");
        if(action!=SaveAction.Local&&File.Exists(JournalPath))
        {
            var previous=JsonFiles.Read<UploadJournal>(JournalPath);
            if(DeliveryStatus.IsTerminal(previous))CleanupDeliveryFiles(session.Id);
            else
            {
                var choice=MessageBox.Show("An unfinished job exists for this session.\n\nYes = retry/reconcile its frozen files\nNo = abandon it and continue this new action\nCancel = keep it and stop","Pending delivery",MessageBoxButton.YesNoCancel,MessageBoxImage.Warning);
                if(choice==MessageBoxResult.Yes){await RetryUpload();return;}
                if(choice==MessageBoxResult.Cancel||!ConfirmAbandon(previous))return;
                CleanupDeliveryFiles(session.Id);
            }
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
        if(rendered.Error!=null||rendered.Cancelled){if(Directory.Exists(outbox))Directory.Delete(outbox,true);throw new InvalidOperationException("Output preparation did not complete. Original pages are preserved; no upload started.");}
        var items=rendered.Saved.Select(path=>new UploadItem{File=path,Hash=LocalDelivery.Hash(path)}).ToList();
        foreach(var item in items)try{AttachmentPolicy.Validate(item.File);}catch(AttachmentRejectedException ex){item.State=UploadState.Failed;item.Retryable=false;item.Error=ex.Message;}
        var journal=new UploadJournal{Target=target,NeedsLocal=action==SaveAction.LocalAndErp,LocalFolder=action==SaveAction.LocalAndErp?options.Folder:null,LocalComplete=false,Items=items};
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
        var results=new SaveResultsWindow(journal.LocalFiles,journal.LocalError,journal,error){Owner=Application.Current.MainWindow};results.ShowDialog();RefreshPendingDelivery();RaiseCommands();
        if(results.RetryRequested&&!Token.IsCancellationRequested)await RetryUpload();
        else if(DeliveryStatus.IsTerminal(journal))CleanupDeliveryFiles(session.Id);
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
        if(!DeliveryStatus.HasRetryableWork(journal)){MessageBox.Show("This delivery has no retryable files. Start a new send operation.","Pending delivery",MessageBoxButton.OK,MessageBoxImage.Information);return;}
        // Journal paths must stay under this session, even if a manifest is edited externally.
        string root=Path.GetFullPath(store.Folder(session.Id))+Path.DirectorySeparatorChar;
        if(journal.Items.Any(i=>!Path.GetFullPath(i.File).StartsWith(root,StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("Unsafe retry artifact path.");
        var target=ChooseDestination(new(){SiteId=journal.Target.SiteId,DocType=journal.Target.DocType,Private=journal.Target.Private},$"Retry frozen job: {journal.Items.Count} files. Confirm the same destination.");if(target==null)return;
        var old=journal.Target;if(target.SiteId!=old.SiteId||target.SiteOrigin!=old.SiteOrigin||target.Account!=old.Account||target.DocType!=old.DocType||target.Name!=old.Name||target.Private!=old.Private)throw new InvalidOperationException("Retry destination/account/privacy differs. No upload started.");
        if(!ReviewSend(target,$"Retry {journal.Items.Count(i=>i.State!=UploadState.Succeeded)} unresolved files. Server receipts will be checked before sending.",journal.NeedsLocal&&!journal.LocalComplete))return;
        journal=journal with{Target=target};JsonFiles.Write(JournalPath,journal);await Deliver(journal);
    }
}

