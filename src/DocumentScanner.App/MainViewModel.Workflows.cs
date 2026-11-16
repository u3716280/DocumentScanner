using System.IO;
using System.Windows;
using Microsoft.Extensions.Logging;
using DocumentScanner.Core;
using DocumentScanner.Infrastructure;
namespace DocumentScanner.App;
public sealed partial class MainViewModel
{
    private ScanProfile? activeProfile;
    private bool initialized;
    public RelayCommand NewDocumentCommand{get;}
    private bool showOriginal;
    public bool ShowOriginal{get=>showOriginal;set{if(Set(ref showOriginal,value)){Changed(nameof(ShowAdjusted));_=SetSelectionAsync(Pages.Where(p=>selection.Contains(p.Data.Id)).ToArray());}}}
    private void NewDocument()
    {
        if(Pages.Count>0){var answer=MessageBox.Show("Keep this session for recovery before starting a new document? Yes = keep, No = discard.","New document",MessageBoxButton.YesNoCancel);if(answer==MessageBoxResult.Cancel)return;if(answer==MessageBoxResult.Yes)store.Save(session);else{if(!ConfirmDiscardSession(session.Id))return;store.Delete(session.Id);}}
        else if(Directory.Exists(store.Folder(session.Id))){if(!ConfirmDiscardSession(session.Id))return;store.Delete(session.Id);}
        session=new();selection=[];Pages.Clear();Preview=null;PreviewInfo="";ApplyDestination(null);RefreshPendingDelivery();Changed(nameof(PageSummary));RaiseCommands();
    }
    public RelayCommand SendCommand{get;}public RelayCommand SaveAndSendCommand{get;}public RelayCommand ProfileManagerCommand{get;}public RelayCommand RetryUploadCommand{get;}public RelayCommand ApplyPageToneCommand{get;}
    private int pageBrightness,pageContrast;
    public int PageBrightness{get=>pageBrightness;set=>Set(ref pageBrightness,Math.Clamp(value,-1000,1000));}
    public int PageContrast{get=>pageContrast;set=>Set(ref pageContrast,Math.Clamp(value,-1000,1000));}
    public string ProfileState=>SelectedProfile==null?"No active profile":CurrentSettings()==SelectedProfile.Settings&&settings.Output==SelectedProfile.Output&&SelectedDevice?.Key==SelectedProfile.Device.Key?"Saved settings":"Modified · save changes in Profile Manager";
    public ExportFormat[] OutputFormats=>Enum.GetValues<ExportFormat>();
    public ExportFormat OutputFormat{get=>settings.Output.Format;set{settings=settings with{Output=settings.Output with{Format=value}};Changed();Changed(nameof(FilenamePreview));}}
    public string FilenamePrefix{get=>settings.Output.Prefix;set{settings=settings with{Output=settings.Output with{Prefix=value}};Changed();Changed(nameof(FilenamePreview));}}
    public string FilenamePreview{get{try{return OutputNaming.Resolve(settings.Output,DateTime.Now,1)+"."+OutputFormat.ToString().ToLowerInvariant();}catch(ArgumentException){return "Invalid filename prefix/pattern — review output settings";}}}
    private const string JournalFile="delivery.json",LegacyJournalFile="upload-job.json",DestinationFile="destination.json";
    private string JournalPath=>JournalPathFor(session.Id);
    private string JournalPathFor(Guid id)=>Path.Combine(store.Folder(id),JournalFile);
    private string DestinationPathFor(Guid id)=>Path.Combine(store.Folder(id),DestinationFile);

    /// <summary>The unfinished or finished delivery of a session, or null. A job left by the removed Bridge integration is discarded.</summary>
    private DeliveryJournal? ReadJournal(Guid id)
    {
        string folder=store.Folder(id),legacy=Path.Combine(folder,LegacyJournalFile),path=JournalPathFor(id);
        if(File.Exists(legacy)&&!File.Exists(path))
        {
            // Bridge-era jobs cannot be retried through the standard API. The scanned pages stay in the session.
            File.Delete(legacy);string old=Path.Combine(folder,"outbox");if(Directory.Exists(old))Directory.Delete(old,true);
        }
        if(!File.Exists(path))return null;
        var journal=JsonFiles.Read<DeliveryJournal>(path);
        if(journal.SchemaVersion!=DeliveryJournal.CurrentSchema||journal.SessionId!=id||journal.Items==null)throw new InvalidDataException("The delivery record of this session cannot be read.");
        return journal;
    }
    private bool ConfirmDiscardSession(Guid id)
    {
        try{var journal=ReadJournal(id);if(journal==null||DeliveryStatus.IsTerminal(journal))return true;}
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException){}
        return MessageBox.Show("This session has an unfinished delivery. Discarding it removes the prepared files. A file whose result is unknown may already be in ERPNext.\n\nDiscard this session?","Unfinished delivery",MessageBoxButton.YesNo,MessageBoxImage.Warning)==MessageBoxResult.Yes;
    }
    private void CleanupDeliveryFiles(Guid id)
    {
        string folder=store.Folder(id),outbox=Path.Combine(folder,"outbox"),journal=JournalPathFor(id);
        if(Directory.Exists(outbox))Directory.Delete(outbox,true);if(File.Exists(journal))File.Delete(journal);
    }
    private bool ConfirmAbandon(DeliveryJournal journal)
    {
        string warning=DeliveryStatus.HasUnknown(journal)?" At least one file has an unknown result and may already be in ERPNext.":"";
        return MessageBox.Show("Abandon the unfinished delivery and remove its prepared files?"+warning,"Unfinished delivery",MessageBoxButton.YesNo,MessageBoxImage.Warning)==MessageBoxResult.Yes;
    }
    private async Task ManageProfiles()
    {
        var current=SelectedDevice==null?null:new ScanProfile("New profile",SelectedDevice,CurrentSettings()){Output=settings.Output};
        var manager=new ProfileManagerWindow(settings with{Profiles=Profiles.ToList()},Devices.ToList(),SelectedProfile,current,value=>settingsStore.Save(value)){Owner=Application.Current.MainWindow};manager.ShowDialog();
        settings=manager.ResultSettings;activeProfile=settings.Profiles.FirstOrDefault(p=>p.Id==activeProfile?.Id);Profiles.Clear();foreach(var p in settings.Profiles)Profiles.Add(p);selectedProfile=Profiles.FirstOrDefault(p=>p.Id==SelectedProfile?.Id);Changed(nameof(SelectedProfile));
        if(manager.LoadRequested is {} chosen){SelectedProfile=Profiles.First(p=>p.Id==chosen.Id);await LoadProfile();}
        foreach(var property in new[]{nameof(ProfileState),nameof(OutputFormat),nameof(FilenamePrefix),nameof(FilenamePreview)})Changed(property);
    }

    // ---- ERPNext destination ------------------------------------------------------------------

    private FrappeClient NewClient(Guid siteId)
    {
        var site=settings.Sites.SingleOrDefault(s=>s.Id==siteId)??throw new InvalidOperationException("The ERPNext site of this document is no longer in Settings. Add it again, then click Scan in ERPNext.");
        return new(site,WindowsCredentialStore.Load(site.Id)??throw new InvalidOperationException("No API key is stored for this ERPNext site and Windows account. Enter it in Settings."));
    }

    /// <summary>Reads the document with the user's own ERPNext permissions. Used at every point where the destination is relied on.</summary>
    private async Task<ErpDestination> ReadDestination(Guid siteId,string doctype,string docname)
    {
        using var client=NewClient(siteId);
        string account=await client.GetLoggedUser(Token);
        return await client.GetDocument(doctype,docname,account,Token);
    }

    /// <summary>Acts on a command-line argument or a message forwarded from a second copy of the application.</summary>
    public async Task HandleLaunchAsync(LaunchRequest request)
    {
        switch(request.Kind)
        {
            case LaunchKind.None:return;
            case LaunchKind.Test:MessageBox.Show("The scan link works. ERPNext can open this copy of DocumentScanner.","Scan link",MessageBoxButton.OK,MessageBoxImage.Information);return;
            case LaunchKind.Invalid:log.LogWarning("Scan link refused");MessageBox.Show("This scan link was refused.\n\n"+request.Error,"Scan link",MessageBoxButton.OK,MessageBoxImage.Warning);return;
        }
        await InitializeAsync();
        if(Busy){MessageBox.Show("DocumentScanner is busy. Finish or cancel the current step, then click Scan in ERPNext again.","Scan link",MessageBoxButton.OK,MessageBoxImage.Information);return;}
        await Operation(()=>AcceptScanLink(request.Link!));
    }

    private async Task AcceptScanLink(ScanLink link)
    {
        // The link only names a site, a DocType and a document. Everything else is read from ERPNext with the user's own account.
        var site=SiteOrigins.Match(link,settings.Sites)??throw new InvalidOperationException($"{link.SiteOrigin} is not one of your allowed ERPNext sites. Add it in Settings, then click Scan in ERPNext again.");
        var pending=ReadJournal(session.Id);
        if(pending is {Destination:{} busy}&&DeliveryStatus.HasRetryableWork(pending)&&!(busy.SiteId==site.Id&&busy.DocType==link.DocType&&busy.DocName==link.DocName))
            throw new InvalidOperationException($"A delivery to {busy.DocType} {busy.DocName} is not finished. Retry or abandon it first, then click Scan in ERPNext again.");
        Status="Checking the document in ERPNext…";
        var fresh=await ReadDestination(site.Id,link.DocType,link.DocName);
        log.LogInformation("Scan link accepted host={Host} doctype={DocType} docname={DocName} docstatus={DocStatus}",new Uri(fresh.SiteOrigin).Host,fresh.DocType,fresh.DocName,fresh.DocStatus);
        if(Pages.Count>0&&destination?.SameTarget(fresh)!=true)
        {
            var answer=MessageBox.Show($"{fresh.DocType} {fresh.DocName}\n\nYou already have {Pages.Count} scanned {(Pages.Count==1?"page":"pages")}.\n\nYes = use these pages for this document\nNo = keep them for recovery and start a new scan\nCancel = ignore the link","Scan link",MessageBoxButton.YesNoCancel,MessageBoxImage.Question);
            if(answer==MessageBoxResult.Cancel){Status=Text.Get("Ready");return;}
            if(answer==MessageBoxResult.No){store.Save(session);session=new();selection=[];Pages.Clear();Preview=null;PreviewInfo="";RefreshPendingDelivery();}
        }
        ApplyDestination(fresh);
        if(CanUpload)Status=$"Destination: {fresh.DocType} {fresh.DocName}";
        else{Status="Destination cancelled";MessageBox.Show(DocStatusRules.CancelledMessage,"ERPNext destination",MessageBoxButton.OK,MessageBoxImage.Warning);}
    }

    /// <summary>Sets or clears the locked destination and stores it with the session, so a recovered session keeps it.</summary>
    public void ApplyDestination(ErpDestination? value)
    {
        destination=value;
        string path=DestinationPathFor(session.Id);
        if(value!=null)JsonFiles.Write(path,value);else if(File.Exists(path))File.Delete(path);
        NotifyDestination();
    }
    private void LoadDestination()
    {
        destination=null;
        try
        {
            string path=DestinationPathFor(session.Id);
            if(File.Exists(path))
            {
                var saved=JsonFiles.Read<ErpDestination>(path);
                // A recovered destination is honoured only while its site is still allowed and it names a supported DocType.
                if(ErpDocTypes.Find(saved.DocType)!=null&&settings.Sites.Any(s=>s.Id==saved.SiteId&&SiteOrigins.Same(s.Url,saved.SiteOrigin)))destination=saved;
            }
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException or ArgumentException){}
        NotifyDestination();
    }
    private void NotifyDestination()
    {
        foreach(string property in new[]{nameof(HasDestination),nameof(NoDestination),nameof(CanUpload),nameof(DestinationType),nameof(DestinationNumber),nameof(DestinationParty),nameof(DestinationDetails),nameof(DestinationAccount),nameof(DestinationWarning),nameof(SendAppearance),nameof(SaveAppearance),nameof(FilenamePreview)})Changed(property);
        RaiseCommands();
    }

    // ---- Save and send ------------------------------------------------------------------------

    private async Task SaveActionAsync(SaveAction action)
    {
        try{await SaveActionCoreAsync(action);}
        finally{RefreshPendingDelivery();}
    }
    private async Task SaveActionCoreAsync(SaveAction action)
    {
        // Never fall back to "all pages": the user decides what is saved or sent.
        var chosen=session.Pages.Where(p=>selection.Contains(p.Id)).ToArray();if(chosen.Length==0)throw new ArgumentException("Select the pages to save or send. Tick them in the page list first.");
        if(destination==null)
        {
            if(action!=SaveAction.Local)throw new InvalidOperationException(SaveActionRules.DirectLaunchMessage);
            await SaveLocalDirect(chosen);return;
        }
        var target=destination;
        if(action!=SaveAction.Local&&!CanUpload)throw new DestinationBlockedException(DocStatusRules.CancelledMessage);
        var previous=ReadJournal(session.Id);
        if(previous!=null)
        {
            if(DeliveryStatus.IsTerminal(previous))CleanupDeliveryFiles(session.Id);
            else
            {
                var choice=MessageBox.Show("An earlier delivery of this session is not finished.\n\nYes = retry it\nNo = abandon it and continue with this new one\nCancel = stop","Unfinished delivery",MessageBoxButton.YesNoCancel,MessageBoxImage.Warning);
                if(choice==MessageBoxResult.Yes){await RetryDelivery();return;}
                if(choice==MessageBoxResult.Cancel||!ConfirmAbandon(previous))return;
                CleanupDeliveryFiles(session.Id);
            }
        }
        string outbox=Path.Combine(store.Folder(session.Id),"outbox",Guid.NewGuid().ToString("N"));
        var legacy=settings with{OutputFolder=action==SaveAction.Erp?outbox:settings.Output.Folder,Format=settings.Output.Format,JpegQuality=settings.Output.JpegQuality};
        var dialog=new ExportWindow(legacy,chosen.Length,action==SaveAction.Erp,target,settings.AttachmentDescriptions){Owner=Application.Current?.MainWindow};if(dialog.ShowDialog()!=true)return;var options=dialog.Options!;
        settings=settings with{Output=settings.Output with{Folder=action==SaveAction.Erp?settings.Output.Folder:options.Folder,Format=options.Format,JpegQuality=options.Quality}};PersistSettings();

        IReadOnlyCollection<string> serverNames=[];long limit=long.MaxValue;
        if(action!=SaveAction.Local)
        {
            // Read the document again: it may have been cancelled, or its attachments changed, since the link arrived.
            Status="Checking the document in ERPNext…";
            using var client=NewClient(target.SiteId);
            string account=await client.GetLoggedUser(Token);
            target=await client.GetDocument(target.DocType,target.DocName,account,Token);ApplyDestination(target);
            if(!CanUpload)throw new DestinationBlockedException(DocStatusRules.CancelledMessage);
            serverNames=(await client.ListAttachments(target.DocType,target.DocName,Token)).Select(a=>a.FileName).ToArray();
            limit=await client.GetMaxFileSize(Token);
        }

        // Render once. The folder copy and the upload both use these exact files.
        Status="Processing";
        var rendered=await StaTask.Run(()=>exporter.ExportAsync(chosen,store.Folder(session.Id),options with{Folder=outbox,BaseName="page"},null,Token));
        if(rendered.Error!=null||rendered.Cancelled||rendered.Saved.Count==0){if(Directory.Exists(outbox))Directory.Delete(outbox,true);throw new InvalidOperationException("The files could not be prepared. Your pages are kept and nothing was saved or sent.");}
        var journal=new DeliveryJournal
        {
            SessionId=session.Id,Action=action,Format=options.Format,Destination=target,BaseName=options.BaseName,LocalFolder=action==SaveAction.Erp?null:options.Folder,
            Revision=DeliveryRevision.Compute(chosen,options.Format,options.Paper,options.Fit,options.Quality),PageIds=chosen.Select(p=>p.Id).ToList(),
            Items=rendered.Saved.Select((file,index)=>DeliveryPlanner.Item(file,options.Format==ExportFormat.Pdf?null:index+1)).ToList(),
        };
        var renamed=DeliveryPlanner.AssignNames(journal,journal.BaseName,serverNames);
        bool proceed;
        if(action==SaveAction.Local)proceed=renamed.Count==0||MessageBox.Show("A file with this name already exists in the folder. Nothing will be overwritten; the new file will be saved as:\n\n"+string.Join("\n",renamed.Select(i=>i.Name))+"\n\nContinue?","Save to folder",MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes;
        else{Status="Waiting for confirmation";proceed=new ConfirmSendWindow(journal,chosen.Length,limit,renamed){Owner=Application.Current?.MainWindow}.ShowDialog()==true;}
        if(!proceed){if(Directory.Exists(outbox))Directory.Delete(outbox,true);Status=Text.Get("Ready");return;}
        JsonFiles.Write(JournalPath,journal);await Deliver(journal);
    }
    private async Task SaveLocalDirect(PageData[] chosen)
    {
        var legacy=settings with{OutputFolder=settings.Output.Folder,Format=settings.Output.Format,JpegQuality=settings.Output.JpegQuality};
        var dialog=new ExportWindow(legacy,chosen.Length){Owner=Application.Current?.MainWindow};if(dialog.ShowDialog()!=true)return;var options=dialog.Options!;
        settings=settings with{Output=settings.Output with{Folder=options.Folder,Format=options.Format,JpegQuality=options.Quality}};PersistSettings();
        Status="Saving to folder…";
        var result=await StaTask.Run(()=>exporter.ExportAsync(chosen,store.Folder(session.Id),options,null,Token));
        Status=result.Error==null&&!result.Cancelled?"Completed":"Not completed";
        new SaveResultsWindow(result.Saved,result.Cancelled?"Cancelled.":result.Error){Owner=Application.Current?.MainWindow}.ShowDialog();
    }
    private async Task Deliver(DeliveryJournal journal)
    {
        string path=JournalPath;void Persist()=>JsonFiles.Write(path,journal);
        string? error=null;
        try
        {
            bool localDone=true;
            if(journal.NeedsLocal){Status="Saving to folder…";localDone=await Task.Run(()=>LocalDelivery.Complete(journal,Persist));}
            // Nothing is uploaded until every local file is saved.
            if(localDone&&journal.NeedsUpload)
            {
                using var client=NewClient(journal.Destination!.SiteId);
                await UploadRunner.Run(journal,client,Persist,Token,(number,total)=>Status=$"Uploading {number} of {total}");
            }
        }
        catch(OperationCanceledException){error="Cancelled. Retry checks ERPNext before sending anything again.";}
        catch(Exception e) when(e is ErpException or DestinationBlockedException or InvalidOperationException or IOException or UnauthorizedAccessException){error=e.Message;log.LogWarning("Delivery stopped: {Reason}",e.GetType().Name);}
        journal.LastError=error;Persist();
        if(journal.Destination is {} refreshed&&destination?.SameTarget(refreshed)==true)ApplyDestination(refreshed);
        var outcome=DeliveryStatus.Outcome(journal);
        Status=!CanUpload&&journal.NeedsUpload&&outcome!=DeliveryOutcome.Completed?"Destination cancelled":DeliveryStatus.Text(outcome);
        var results=new SaveResultsWindow(journal,error){Owner=Application.Current?.MainWindow};results.ShowDialog();
        if(results.RetryRequested&&!Token.IsCancellationRequested){await RetryDelivery();return;}
        // Prepared files are removed once nothing is left to retry: all confirmed, or the rest can never succeed.
        if(DeliveryStatus.IsTerminal(journal))CleanupDeliveryFiles(session.Id);
        RefreshPendingDelivery();RaiseCommands();
    }
    private async Task RetryDelivery()
    {
        var journal=ReadJournal(session.Id)??throw new InvalidOperationException("There is no delivery to retry.");
        if(!DeliveryStatus.HasRetryableWork(journal)){CleanupDeliveryFiles(session.Id);MessageBox.Show("Nothing is left to retry. Start a new save or send.","Delivery",MessageBoxButton.OK,MessageBoxImage.Information);return;}
        // Prepared files must stay inside this session, even if the record was edited by hand.
        string root=Path.GetFullPath(store.Folder(session.Id))+Path.DirectorySeparatorChar;
        if(journal.Items.Any(i=>!Path.GetFullPath(i.File).StartsWith(root,StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("The delivery record points outside this session.");
        if(journal.BaseName.Length>0)
        {
            // Files that have not reached any destination are named again against what exists now.
            IReadOnlyCollection<string>? serverNames=[];
            if(journal.NeedsUpload)
            {
                var target=journal.Destination!;
                try{using var client=NewClient(target.SiteId);serverNames=(await client.ListAttachments(target.DocType,target.DocName,Token)).Select(a=>a.FileName).ToArray();}
                catch(Exception e) when(e is ErpException or InvalidOperationException){serverNames=null;} // The run below reports the reason; keep the current names.
            }
            if(serverNames!=null)DeliveryPlanner.AssignNames(journal,journal.BaseName,serverNames);
        }
        journal.RetryCount++;JsonFiles.Write(JournalPath,journal);await Deliver(journal);
    }
}
