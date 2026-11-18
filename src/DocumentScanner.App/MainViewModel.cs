using System.IO;
using System.Diagnostics;
using System.Windows.Threading;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Microsoft.Extensions.Logging;
using DocumentScanner.Core;
using DocumentScanner.Infrastructure;
using DocumentScanner.Scanning;
using DocumentScanner.ImagingSupport;
namespace DocumentScanner.App;
public sealed partial class MainViewModel : Bindable,IDisposable
{
    private readonly IScannerService scanner; private readonly IDocumentExporter exporter;
    private readonly SettingsStore settingsStore; private readonly SessionStore store;private readonly ILogger log;
    private AppSettings settings;private DocumentSession session=new();private readonly SessionStateMachine state=new();
    private CancellationTokenSource? cancellation;
    private readonly List<RelayCommand> commands=[];
    private List<Guid> selection=[];
    public ObservableCollection<DeviceInfo> Devices {get;}=[];
    public ObservableCollection<PageViewModel> Pages {get;}=[];
    public ObservableCollection<ScanProfile> Profiles {get;}=[];
    private readonly ScanSettingsSelection scanSelection=new();
    public ObservableCollection<ScanSource> Sources=>scanSelection.Sources;
    public ObservableCollection<int> Dpis=>scanSelection.Dpis;
    public ObservableCollection<ScanColor> Colors=>scanSelection.Colors;
    public ObservableCollection<string> Papers=>scanSelection.Papers;
    private ScanProfile? selectedProfile;
    public ScanProfile? SelectedProfile
    {
        get=>selectedProfile;
        set{if(Set(ref selectedProfile,value)){if(value!=null)ProfileName=value.Name;RaiseCommands();if(initialized&&NotBusy&&value!=null)_=Operation(LoadProfile);}}
    }
    private string profileName="";public string ProfileName{get=>profileName;set=>Set(ref profileName,value);}
    public string Endpoint{get;set;}="";
    private DeviceInfo? selectedDevice;
    private bool scannerAvailable;
    public DeviceInfo? SelectedDevice
    {
        get=>selectedDevice;
        set
        {
            if(changingDevices)return;
            // A deferred ComboBox null during list refresh must not undo a verified selection.
            // Explicit Reset clears the backing field directly; the user has no clear-selection action here.
            if(value==null&&selectedDevice!=null&&Devices.Contains(selectedDevice)){Changed(nameof(SelectedDevice));return;}
            if(Set(ref selectedDevice,value)){scannerAvailable=false;Changed(nameof(DeviceDetails));if(!Busy&&value!=null)_=Operation(LoadCapabilities);}
        }
    }
    public string DeviceDetails=>SelectedDevice==null?"":$"{SelectedDevice.Driver}\n{SelectedDevice.Id}";
    private bool busy;
    public bool Busy{get=>busy;private set{Set(ref busy,value);Changed(nameof(NotBusy));RaiseCommands();}}
    public bool NotBusy=>!Busy;
    private string status=Text.Get("Ready");public string Status{get=>status;private set=>Set(ref status,value);}
    private string warning="";public string CapabilityWarning{get=>warning;private set=>Set(ref warning,value);}
    private BitmapSource? preview;public BitmapSource? Preview{get=>preview;private set=>Set(ref preview,value);}
    private string previewInfo="";public string PreviewInfo{get=>previewInfo;private set=>Set(ref previewInfo,value);}
    public string PageSummary=>$"{Pages.Count} {Text.Get("Pages")} · {selection.Count} {Text.Get("Selected")}";
    public ScanSource? SelectedSource{get=>scanSelection.SelectedSource;set=>scanSelection.SelectedSource=value;}
    public int? SelectedDpi{get=>scanSelection.SelectedDpi;set=>scanSelection.SelectedDpi=value;}
    public ScanColor? SelectedColor{get=>scanSelection.SelectedColor;set=>scanSelection.SelectedColor=value;}
    public string? SelectedPaper{get=>scanSelection.SelectedPaper;set=>scanSelection.SelectedPaper=value;}
    public ScanSource Source{get=>scanSelection.Source;set=>scanSelection.Source=value;}
    public int Dpi{get=>scanSelection.Dpi;set=>scanSelection.Dpi=value;}
    public ScanColor Color{get=>scanSelection.Color;set=>scanSelection.Color=value;}
    public string Paper{get=>scanSelection.Paper;set=>scanSelection.Paper=value;}
    private bool landscape,deskew=true,detectBlank,removeBlank,nativeUi;
    public bool Landscape{get=>landscape;set=>Set(ref landscape,value);}public bool Deskew{get=>deskew;set=>Set(ref deskew,value);}private bool autoCrop;
    public bool AutoCrop
    {
        get=>autoCrop;
        set{Set(ref autoCrop,value);}
    }public bool DetectBlank{get=>detectBlank;set=>Set(ref detectBlank,value);}public bool RemoveBlank{get=>removeBlank;set=>Set(ref removeBlank,value);}public bool NativeUi{get=>nativeUi;set=>Set(ref nativeUi,value);}
    private readonly ImageAdjustments adjustments=new();
    public int Brightness{get=>adjustments.Brightness;set=>adjustments.Brightness=value;}
    public int Contrast{get=>adjustments.Contrast;set=>adjustments.Contrast=value;}
    private ScannerCapabilities? capabilities;
    public Func<nint> WindowHandle{get;set;}=()=>0;
    public Action<IReadOnlyList<Guid>>? RestoreSelection{get;set;}
    public RelayCommand ManualCropCommand{get;}public RelayCommand AutoColorCommand{get;}public RelayCommand ResetColorCommand{get;}
    public RelayCommand ResetProfileCommand{get;}
    public RelayCommand RefreshCommand{get;}public RelayCommand ScanCommand{get;}public RelayCommand CancelCommand{get;}
    public RelayCommand SaveCommand{get;}public RelayCommand DeleteCommand{get;}public RelayCommand ClearCommand{get;}
    public RelayCommand UndoCommand{get;}public RelayCommand RotateLeftCommand{get;}public RelayCommand RotateRightCommand{get;}public RelayCommand Rotate180Command{get;}
    public RelayCommand MoveUpCommand{get;}public RelayCommand MoveDownCommand{get;}public RelayCommand CropCommand{get;}public RelayCommand ResetCropCommand{get;}
    public RelayCommand ConnectCommand{get;}public RelayCommand SaveProfileCommand{get;}public RelayCommand LoadProfileCommand{get;}public RelayCommand DeleteProfileCommand{get;}public RelayCommand DefaultCommand{get;}public RelayCommand ImportCommand{get;}public RelayCommand ResetAdjustmentsCommand{get;}
    public MainViewModel(IScannerService scanner,IDocumentExporter exporter,SettingsStore settingsStore,SessionStore store,ILogger<MainViewModel> logger)
    {
        this.scanner=scanner;this.exporter=exporter;this.settingsStore=settingsStore;this.store=store;log=logger;
        adjustments.PropertyChanged+=(_,e)=>Changed(e.PropertyName);
        scanSelection.PropertyChanged+=(_,e)=>{
            Changed(e.PropertyName);
            if(e.PropertyName==nameof(ScanSettingsSelection.Warnings))UpdateCapabilityWarning();
        };
        settings=settingsStore.Load();ApplySettings(settings.Scan);
        // The 2.0 upgrade drops the sites of the removed Bridge integration; their stored API keys go with them.
        foreach(Guid removed in settingsStore.RemovedSiteIds)WindowsCredentialStore.Delete(removed);
        PropertyChanged+=(_,e)=>{if(e.PropertyName!=nameof(ProfileState))Changed(nameof(ProfileState));};foreach(var p in settings.Profiles)Profiles.Add(p);
        ResetProfileCommand=Command(()=>_=Operation(ResetProfileAndDiscover));
        RefreshCommand=ResetProfileCommand;
        ScanCommand=Command(()=>_=Operation(Scan),()=>NotBusy&&SelectedDevice!=null&&scannerAvailable);
        CancelCommand=Command(()=>{cancellation?.Cancel();Status=Text.Get("Working");},()=>Busy&&cancellation is {IsCancellationRequested:false});
        SaveCommand=Command(()=>_=Operation(()=>SaveActionAsync(SaveAction.Local)),()=>NotBusy&&selection.Count>0);
        SendCommand=Command(()=>_=Operation(()=>SaveActionAsync(SaveAction.Erp)),()=>SaveActionRules.Enabled(SaveAction.Erp,CanUpload,selection.Count>0,Busy));
        SaveAndSendCommand=Command(()=>_=Operation(()=>SaveActionAsync(SaveAction.LocalAndErp)),()=>SaveActionRules.Enabled(SaveAction.LocalAndErp,CanUpload,selection.Count>0,Busy));
        NewDocumentCommand=Command(NewDocument);
        ProfileManagerCommand=Command(()=>_=Operation(ManageProfiles));
        RetryUploadCommand=Command(()=>_=Operation(RetryDelivery),()=>NotBusy&&HasPendingDelivery);
        ApplyPageToneCommand=Command(()=>Edit(()=>session.SetTone(selection,PageBrightness,PageContrast)),()=>NotBusy&&selection.Count>0);
        DeleteCommand=Command(()=>Edit(()=>{if(Confirm("ConfirmDelete"))session.Delete(selection);}),()=>NotBusy&&selection.Count>0);
        ClearCommand=Command(()=>Edit(()=>{if(Confirm("ConfirmClear"))session.Clear();}),()=>NotBusy&&Pages.Count>0);
        UndoCommand=Command(()=>Edit(session.Undo),()=>NotBusy&&session.CanUndo);
        RotateLeftCommand=Command(()=>Edit(()=>session.Rotate(selection,-90)),()=>NotBusy&&selection.Count>0);
        RotateRightCommand=Command(()=>Edit(()=>session.Rotate(selection,90)),()=>NotBusy&&selection.Count>0);
        Rotate180Command=Command(()=>Edit(()=>session.Rotate(selection,180)),()=>NotBusy&&selection.Count>0);
        MoveUpCommand=Command(()=>Move(-1),()=>NotBusy&&selection.Count>0);
        MoveDownCommand=Command(()=>Move(1),()=>NotBusy&&selection.Count>0);
        ManualCropCommand=Command(()=>_=Operation(ManualCrop),()=>NotBusy&&selection.Count==1);
        AutoColorCommand=Command(()=>Edit(()=>session.SetAutoColor(selection,true)),()=>NotBusy&&selection.Count>0);
        ResetColorCommand=Command(()=>Edit(()=>session.SetAutoColor(selection,false)),()=>NotBusy&&selection.Count>0);
        CropCommand=Command(()=>Edit(()=>session.SetCrop(selection,true)),()=>NotBusy&&selection.Count>0);
        ResetCropCommand=Command(()=>Edit(()=>session.SetCrop(selection,false)),()=>NotBusy&&selection.Count>0);
        ConnectCommand=Command(()=>_=Operation(Connect));
        SaveProfileCommand=Command(()=>Edit(SaveProfile),()=>NotBusy&&SelectedDevice!=null);
        LoadProfileCommand=Command(()=>_=Operation(LoadProfile),()=>NotBusy&&SelectedProfile!=null);
        DeleteProfileCommand=Command(()=>Edit(DeleteProfile),()=>NotBusy&&SelectedProfile!=null);
        DefaultCommand=Command(()=>Edit(()=>{settings=settings with{DefaultDeviceKey=SelectedDevice!.Key};PersistSettings();Status=Text.Get("DefaultSaved");}),()=>NotBusy&&SelectedDevice!=null);
        ResetAdjustmentsCommand=Command(adjustments.Reset);
        ImportCommand=Command(()=>_=Operation(ImportImages));
        InitializeWorkspace();
    }
    private RelayCommand Command(Action action,Func<bool>? can=null){var result=new RelayCommand(action,can??(()=>NotBusy));commands.Add(result);return result;}
    private void RaiseCommands(){foreach(var cmd in commands)cmd.Raise();}
    public void NotifyProfileChanged()=>RaiseCommands();
    private bool Confirm(string key)=>MessageBox.Show(Text.Get(key),Text.Get("Title"),MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes;
    private Task? initializationTask;
    private bool changingDevices;
    public Task InitializeAsync()=>initializationTask??=InitializeCoreAsync();
    private async Task InitializeCoreAsync()
    {
        long started=Stopwatch.GetTimestamp();
        try
        {
            await Operation(async()=>
            {
                // Mark startup busy before yielding, but let WPF paint before file/driver work.
                await Dispatcher.Yield(DispatcherPriority.Background);
                if(settingsStore.LoadWarning!=null)MessageBox.Show(settingsStore.LoadWarning,Text.Get("ConfigWarning"));
                long recoveryStarted=Stopwatch.GetTimestamp();
                var recoverable=await Task.Run(()=>store.Recoverable(),Token);
                log.LogInformation("Startup recovery inspection elapsed_ms={Elapsed}",Stopwatch.GetElapsedTime(recoveryStarted).TotalMilliseconds);
                foreach(var recovered in recoverable)
                {
                    var answer=MessageBox.Show($"{Text.Get("RecoveryPrompt")}\n\n{recovered.Pages.Count} {Text.Get("Pages")} · {recovered.Updated.LocalDateTime:g}",Text.Get("Recover"),MessageBoxButton.YesNoCancel,MessageBoxImage.Question);
                    if(answer==MessageBoxResult.Yes){session=new(recovered.Id);session.Restore(recovered.Pages);RefreshPages();LoadDestination();break;}
                    if(answer==MessageBoxResult.No){if(ConfirmDiscardSession(recovered.Id))await Task.Run(()=>store.Delete(recovered.Id),Token);else break;}else break;
                }
                var plan=StartupDiscoveryPlan.Create(Profiles.ToList(),settings.DefaultProfileId);
                log.LogInformation("Startup discovery mode={Mode}",plan.Mode);
                switch(plan.Mode)
                {
                    case StartupDiscoveryMode.DiscoverAll:await RefreshDevices();break;
                    case StartupDiscoveryMode.LoadDefaultProfile:
                        selectedProfile=plan.Profile;Changed(nameof(SelectedProfile));await LoadProfile();break;
                    default:Status="Choose a saved profile, or open Scanner connections → Find all scanners.";break;
                }
            });
        }
        finally{RefreshPendingDelivery();initialized=true;log.LogInformation("Startup completed elapsed_ms={Elapsed}",Stopwatch.GetElapsedTime(started).TotalMilliseconds);}
    }
    private async Task ResetProfileAndDiscover()
    {
        activeProfile=null;selectedProfile=null;ProfileName="";
        Changed(nameof(SelectedProfile));Changed(nameof(ProfileState));
        await RefreshDevices();
    }
    private async Task Operation(Func<Task> action)
    {
        if(Busy)return;Busy=true;cancellation=new();
        try {await action();}
        catch(OperationCanceledException){Status=Text.Get("Cancelled");log.LogInformation("Operation cancelled");}
        catch(Exception ex)
        {
            log.LogError(ex,"Operation failed");
            Status=ex is ArgumentException or InvalidOperationException or ErpException or DestinationBlockedException or ScanLinkException?ex.Message:
                ex is UnauthorizedAccessException?"Access denied. Choose a writable folder.":ex is IOException?"File operation failed. Check free space and folder access.":ScannerErrors.Describe(ex);
            MessageBox.Show(Status,Text.Get("Error"),MessageBoxButton.OK,MessageBoxImage.Warning);
        }
        finally {cancellation.Dispose();cancellation=null;Busy=false;Changed(nameof(PageSummary));}
    }
    private CancellationToken Token=>cancellation?.Token??CancellationToken.None;
    private async Task RefreshDevices()
    {
        // Only first use and explicit Reset/Refresh use all discovery backends.
        Status="Discovering all scanner interfaces…";long started=Stopwatch.GetTimestamp();
        string? prior=SelectedDevice?.Key??settings.LastDeviceKey??settings.DefaultDeviceKey;
        changingDevices=true;
        try{selectedDevice=null;scannerAvailable=false;capabilities=null;Devices.Clear();Changed(nameof(SelectedDevice));Changed(nameof(DeviceDetails));}
        finally{changingDevices=false;}
        var result=await StaTask.Run(()=>scanner.DiscoverAsync(Token));
        changingDevices=true;
        try
        {
            foreach(var d in result.Devices.Concat(settings.ManualDevices).DistinctBy(d=>d.Key))Devices.Add(d);
            selectedDevice=Devices.FirstOrDefault(d=>d.Key==prior)??Devices.FirstOrDefault(d=>d.Key==settings.DefaultDeviceKey)??Devices.FirstOrDefault();
            Changed(nameof(SelectedDevice));Changed(nameof(DeviceDetails));
        }
        finally{changingDevices=false;}
        if(SelectedDevice!=null)await LoadCapabilities();
        if(Devices.Count==0)Status=Text.Get("NoScanner");
        if(result.Warnings.Count>0){CapabilityWarning=string.Join("\n",result.Warnings);log.LogWarning("One or more scanner backends unavailable");}
        log.LogInformation("Full discovery completed backends_result_count={Count} elapsed_ms={Elapsed}",Devices.Count,Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }
    private Task LoadCapabilities()=>LoadCapabilities(CurrentSettings());
    private async Task LoadCapabilities(ScanSettings requested)
    {
        if(SelectedDevice==null)return;
        // Capture the saved values before awaiting the driver or changing any ItemsSource.
        requested.Validate();var device=SelectedDevice;Status="Checking selected scanner capabilities…";long started=Stopwatch.GetTimestamp();
        try{scannerAvailable=false;capabilities=await StaTask.Run(()=>scanner.GetCapabilitiesAsync(device,Token));Token.ThrowIfCancellationRequested();scannerAvailable=true;}
        catch(OperationCanceledException){throw;}
        catch(Exception ex){log.LogWarning(ex,"Capability query failed");capabilities=new([],false,Text.Get("NativeNotice"));}
        ApplySettings(requested);
        UpdateCapabilityWarning();
        settings=settings with{LastDeviceKey=device.Key};PersistSettings();Status=!scannerAvailable?"Selected scanner could not be verified. Check the connection or select another device.":capabilities?.Known==true?Text.Get("Ready"):"Scanner responded; capabilities are unverified. Explicit native-dialog handling is required.";
        log.LogInformation("Selected scanner capability check elapsed_ms={Elapsed}",Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }
    private void UpdateCapabilityWarning()=>CapabilityWarning=string.Join("\n",new[]{capabilities?.Warning}.Where(w=>!string.IsNullOrWhiteSpace(w)).Concat(scanSelection.Warnings));
    private ScanSettings CurrentSettings()=>new(){Source=Source,Dpi=Dpi,Color=Color,Paper=Paper,Landscape=Landscape,Deskew=Deskew,AutoCrop=AutoCrop,DetectBlank=DetectBlank,RemoveBlank=RemoveBlank,NativeUi=NativeUi,Brightness=Brightness,Contrast=Contrast};
    private void ApplySettings(ScanSettings scan)
    {
        {
        scan=scanSelection.ApplyCapabilities(capabilities,scan,true);
        Landscape=scan.Landscape;Deskew=scan.Deskew;AutoCrop=scan.AutoCrop;DetectBlank=scan.DetectBlank;RemoveBlank=scan.RemoveBlank;NativeUi=scan.NativeUi;Brightness=scan.Brightness;Contrast=scan.Contrast;
        foreach(var name in new[]{nameof(Source),nameof(Dpi),nameof(Color),nameof(Paper),nameof(Landscape),nameof(Deskew),nameof(AutoCrop),nameof(DetectBlank),nameof(RemoveBlank),nameof(NativeUi),nameof(Brightness),nameof(Contrast)})Changed(name);
        }
    }
    private void PersistSettings(){settings=settings with{Scan=CurrentSettings(),Profiles=Profiles.ToList()};settingsStore.Save(settings);}
    private async Task Scan()
    {
        if(SelectedDevice==null)throw new InvalidOperationException(Text.Get("NoScanner"));
        if(capabilities?.Known==true&&!Sources.Contains(Source))throw new InvalidOperationException("The selected source is not supported.");
        try{capabilities=await StaTask.Run(()=>scanner.GetCapabilitiesAsync(SelectedDevice,Token));}
        catch(Exception ex) when(NativeUi&&ex is not OperationCanceledException){log.LogWarning(ex,"Capability preflight unavailable; continuing with native scanner dialog");capabilities=new([],false,Text.Get("NativeNotice"));}
        var issues=ProfileValidation.CapabilityIssues(CurrentSettings(),capabilities);
        if(issues.Count>0&&!(NativeUi&&ProfileValidation.OnlyUnknown(issues)))throw new InvalidOperationException(string.Join("\n",issues)+"\nReview scan settings or explicitly enable native UI for unknown capabilities.");
        if(!NativeUi&&(Dpis.Count==0||Colors.Count==0))throw new InvalidOperationException("No supported settings. Try the scanner's native dialog.");
        var selected=SelectedDevice;var scanSettings=CurrentSettings();scanSettings.Validate();PersistSettings();
        store.Save(session);state.MoveTo(SessionState.Initializing);Status=Text.Get("Initializing");
        string folder=store.Folder(session.Id);nint handle=WindowHandle();
        try {
            state.MoveTo(SessionState.Scanning);Status=$"{Text.Get("Scanning")} · B {scanSettings.Brightness:+0;-0;0}, C {scanSettings.Contrast:+0;-0;0}";log.LogInformation("Scan started: brightness={Brightness}, contrast={Contrast}, crop={Crop}",scanSettings.Brightness,scanSettings.Contrast,scanSettings.AutoCrop);
            await StaTask.Run(()=>scanner.ScanAsync(selected,scanSettings,folder,async page=>{
                await Application.Current.Dispatcher.InvokeAsync(()=>{
                    state.MoveTo(SessionState.Processing);session.Append(page);store.Save(session);
                    Pages.Add(new(page,Pages.Count+1,folder));Status=$"{Text.Get("Scanning")} · {Pages.Count} {Text.Get("Pages")}";
                    Changed(nameof(PageSummary));state.MoveTo(SessionState.Scanning);
                });
            },handle,Token));
            state.MoveTo(SessionState.Completed);Status=Text.Get("Completed");log.LogInformation("Scan completed: {Pages}",Pages.Count);
        }
        catch(OperationCanceledException){state.MoveTo(SessionState.Cancelled);throw;}
        catch{state.MoveTo(SessionState.Error);throw;}
        finally {store.Save(session);try{SessionStore.CleanupWorkFiles(folder);}catch(IOException){}catch(UnauthorizedAccessException){}RaiseCommands();}
        if(Pages.Count>0)RestoreSelection?.Invoke(new[]{Pages[^1].Data.Id});
        if(RemoveBlank){var blanks=session.Pages.Where(p=>p.BlankCandidate).Select(p=>p.Id).ToArray();if(blanks.Length>0&&MessageBox.Show($"Review: remove {blanks.Length} possible blank pages? Originals remain on disk and Undo can restore them.","Blank page review",MessageBoxButton.YesNo)==MessageBoxResult.Yes){session.Delete(blanks);store.Save(session);RefreshPages();}}
    }
    private void RefreshPages()
    {
        var selected=selection.ToArray();Pages.Clear();string folder=store.Folder(session.Id);
        foreach(var p in session.Pages)Pages.Add(new(p,Pages.Count+1,folder));
        RestoreSelection?.Invoke(selected);Changed(nameof(PageSummary));RaiseCommands();
    }
    private void Edit(Action action)
    {
        if(Busy)return;
        try{action();store.Save(session);RefreshPages();}
        catch(Exception ex){log.LogError(ex,"Document edit or persistence failed");MessageBox.Show(ex is ArgumentException?ex.Message:"The change could not be saved. Check disk space and folder access.",Text.Get("Error"));}
    }
    public void MoveSelectionTo(int index)=>Edit(()=>session.Move(selection,index));
    private void Move(int direction)
    {
        var indices=session.Pages.Select((p,i)=>(p,i)).Where(x=>selection.Contains(x.p.Id)).Select(x=>x.i).ToArray();
        if(indices.Length==0)return;
        MoveSelectionTo(direction<0?indices.Min()-1:indices.Max()+2);
    }
    private int previewVersion;
    private readonly SemaphoreSlim previewGate=new(1,1);
    public async Task SetSelectionAsync(IEnumerable<PageViewModel> selected)
    {
        selection=selected.Select(p=>p.Data.Id).ToList();Changed(nameof(PageSummary));RaiseCommands();
        int version=++previewVersion;var id=selection.LastOrDefault();Preview=null;PreviewInfo="";
        var data=session.Pages.FirstOrDefault(p=>p.Id==id);if(data==null)return;
        PageBrightness=data.Brightness;PageContrast=data.Contrast;
        await previewGate.WaitAsync();
        try {
            if(version!=previewVersion)return;
            string folder=store.Folder(session.Id);var rendered=await StaTask.Run(()=>Task.FromResult(Imaging.RenderDetails(ShowOriginal?data with{ManualCrop=null,AutoCrop=false,AutoColorCorrection=false,Brightness=0,Contrast=0}:data,folder,1600)));
            if(version==previewVersion)
            {
                Preview=rendered.Image;
                PreviewInfo=$"{rendered.Width} × {rendered.Height} px · "+(data.ManualCrop!=null?Text.Get("ManualCrop"):data.AutoCrop?Text.Get(rendered.CropChanged?"CropApplied":"CropUnchanged"):Text.Get("OriginalImage"))+(data.AutoColorCorrection?" · "+Text.Get("AutoColor"):"")+$" · B {data.Brightness:+0;-0;0}, C {data.Contrast:+0;-0;0}";
            }
        }catch(Exception ex){log.LogWarning(ex,"Preview failed");Status="Unable to preview this page; the original is preserved.";}
        finally{previewGate.Release();}
    }
    private async Task ManualCrop()
    {
        var page=session.Pages.SingleOrDefault(p=>selection.Contains(p.Id));if(page==null)return;
        // Display the full oriented original so recropping can restore previously trimmed edges.
        var image=await StaTask.Run(()=>Task.FromResult(Imaging.Render(page with{AutoCrop=false,ManualCrop=null},store.Folder(session.Id),1600)));
        var dialog=new CropWindow(image){Owner=Application.Current.MainWindow};
        if(dialog.ShowDialog()==true&&dialog.Selection is {} bounds)
        {
            session.SetManualCrop(page.Id,NormalizedCrop.FromView(bounds,page.Rotation));
            store.Save(session);RefreshPages();
        }
    }
    private async Task Connect()
    {
        var endpoint=Endpoint;var device=await StaTask.Run(()=>scanner.ConnectEndpointAsync(endpoint,Token));
        if(!Devices.Any(d=>d.Key==device.Key))Devices.Add(device);
        settings=settings with{ManualDevices=settings.ManualDevices.Where(d=>d.Key!=device.Key).Append(device).ToList()};
        SelectedDevice=device;await LoadCapabilities();PersistSettings();Status=Text.Get("ConnectSuccess");
    }
    private void SaveProfile()
    {
        string name=ProfileName.Trim();if(name.Length==0||name.Length>80)throw new ArgumentException("Enter a profile name (1–80 characters).");
        var existing=Profiles.FirstOrDefault(p=>p.Name.Equals(name,StringComparison.OrdinalIgnoreCase));
        var snapshot=CurrentSettings();snapshot.Validate();
        if(existing!=null)Profiles.Remove(existing);var profile=new ScanProfile(name,SelectedDevice!,snapshot){Id=existing?.Id??Guid.NewGuid(),Output=settings.Output};Profiles.Add(profile);SelectedProfile=profile;Changed(nameof(SelectedProfile));PersistSettings();Status=Text.Get("ProfileSaved");
    }
    private async Task LoadProfile()
    {
        try{await LoadProfileCore();}catch{selectedProfile=activeProfile;Changed(nameof(SelectedProfile));throw;}
    }
    private async Task LoadProfileCore()
    {
        if(SelectedProfile is not {} profile)return;
        ProfileValidation.Validate(profile);
        Status="Finding the profile scanner only…";
        var prepared=await StaTask.Run(()=>new ScannerPreparation(scanner).PrepareProfileAsync(profile,Token));
        var device=prepared.Device;var caps=prepared.Capabilities;
        log.LogInformation("Profile scanner preparation find_ms={Find} capabilities_ms={Caps}",prepared.FindMilliseconds,prepared.CapabilitiesMilliseconds);
        var issues=ProfileValidation.CapabilityIssues(profile.Settings,caps);var requested=profile.Settings;
        if(issues.Count>0){
            if(!ProfileValidation.OnlyUnknown(issues))throw new InvalidOperationException(string.Join("\n",issues)+"\nNo settings were applied. Edit the profile to select supported values.");
            if(MessageBox.Show(string.Join("\n",issues)+"\nUse the native scanner dialog explicitly? Profile values are not changed.","Unverified scanner capabilities",MessageBoxButton.YesNo)!=MessageBoxResult.Yes){selectedProfile=activeProfile;Changed(nameof(SelectedProfile));return;}
            requested=requested with{NativeUi=true};
        }
        // Commit UI + engine snapshot only after capability validation. Unknown capability native workflow preserves requested values.
        changingDevices=true;
        try
        {
            var prior=Devices.FirstOrDefault(d=>d.Key==device.Key);if(prior!=null&&prior!=device)Devices.Remove(prior);if(!Devices.Contains(device))Devices.Add(device);
            selectedDevice=device;Changed(nameof(SelectedDevice));Changed(nameof(DeviceDetails));
        }
        finally{changingDevices=false;}
        capabilities=caps;
        if(issues.Count>0)capabilities=null;
        ApplySettings(requested);capabilities=caps;UpdateCapabilityWarning();settings=settings with{Output=profile.Output};foreach(string property in new[]{nameof(OutputFormat),nameof(FilenamePrefix),nameof(FilenamePreview)})Changed(property);
        activeProfile=profile;ProfileName=profile.Name;settings=settings with{LastDeviceKey=device.Key};PersistSettings();scannerAvailable=true;Status=issues.Count==0?"Profile loaded and validated":"Profile loaded — native settings require explicit verification";
    }
    private void DeleteProfile(){if(SelectedProfile is {} p&&Confirm("ProfileConfirm")){Profiles.Remove(p);SelectedProfile=null;Changed(nameof(SelectedProfile));PersistSettings();}}
    private async Task ImportImages()
    {
        var dialog=new OpenFileDialog{Multiselect=true,Filter="Images|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff"};if(dialog.ShowDialog()!=true)return;
        string folder=store.Folder(session.Id);Directory.CreateDirectory(folder);
        foreach(var file in dialog.FileNames)
        {
            Token.ThrowIfCancellationRequested();
            var page=await StaTask.Run(()=>{
                var bitmap=Imaging.Load(file);var id=Guid.NewGuid();string original=$"{id:N}.png",thumb=$"{id:N}-thumb.png";
                string temporary=Path.Combine(folder,original+".tmp");Imaging.SavePng(bitmap,temporary);File.Move(temporary,Path.Combine(folder,original),false);
                Imaging.SavePng(Imaging.Thumbnail(bitmap,180),Path.Combine(folder,thumb));
                return Task.FromResult(new PageData{Id=id,OriginalFile=original,ThumbnailFile=thumb,Width=bitmap.PixelWidth,Height=bitmap.PixelHeight,DpiX=bitmap.DpiX,DpiY=bitmap.DpiY});
            });session.Append(page);store.Save(session);Pages.Add(new(page,Pages.Count+1,folder));Changed(nameof(PageSummary));
        }
        Status=Text.Get("Ready");if(Pages.Count>0)RestoreSelection?.Invoke(new[]{Pages[^1].Data.Id});
    }
    public bool CanClose()
    {
        if(Busy){MessageBox.Show(Text.Get("BusyClose"),Text.Get("Title"));return false;}
        try {
            PersistSettings();
            if(session.Pages.Count>0)
            {
                var answer=MessageBox.Show(Text.Get("ClosePrompt"),Text.Get("Title"),MessageBoxButton.YesNoCancel,MessageBoxImage.Question);
                if(answer==MessageBoxResult.Cancel)return false;
                if(answer==MessageBoxResult.Yes)store.Save(session);else{if(!ConfirmDiscardSession(session.Id))return false;store.Delete(session.Id);}
            }
            else{if(!ConfirmDiscardSession(session.Id))return false;store.Delete(session.Id);}
            return true;
        }catch(Exception ex){log.LogError(ex,"Close persistence failed");MessageBox.Show("Unable to save the session. Check disk space before closing.",Text.Get("Error"));return false;}
    }
    /// <summary>Windows is signing out or shutting down: keep settings and any pages for recovery, without asking.</summary>
    public void SaveForShutdown()
    {
        try{PersistSettings();if(session.Pages.Count>0)store.Save(session);log.LogInformation("Session kept at Windows sign-out pages={Pages}",session.Pages.Count);}
        catch(Exception ex){log.LogError(ex,"Session save at Windows sign-out failed");}
    }
    public void Dispose(){cancellation?.Dispose();}
}

