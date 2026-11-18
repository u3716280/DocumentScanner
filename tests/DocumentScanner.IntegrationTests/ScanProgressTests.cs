using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using DocumentScanner.App;
using DocumentScanner.Core;
using DocumentScanner.Export;
using DocumentScanner.ImagingSupport;
using DocumentScanner.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
namespace DocumentScanner.IntegrationTests;
public sealed class ScanProgressTests
{
    private static readonly DeviceInfo Device=new("Wia|False|p","Scanner","Wia","p");

    /// <summary>Reports progress one step at a time, each step released by the test.</summary>
    private sealed class SteppedScanner : IScannerService
    {
        public readonly SemaphoreSlim Step=new(0),Reached=new(0);
        public Task<(IReadOnlyList<DeviceInfo>,IReadOnlyList<string>)> DiscoverAsync(CancellationToken ct)=>Task.FromResult(((IReadOnlyList<DeviceInfo>)[Device],(IReadOnlyList<string>)[]));
        public Task<DeviceInfo?> FindDeviceAsync(DeviceInfo saved,CancellationToken ct)=>Task.FromResult<DeviceInfo?>(saved);
        public Task<ScannerCapabilities> GetCapabilitiesAsync(DeviceInfo device,CancellationToken ct)=>Task.FromResult(new ScannerCapabilities([new(ScanSource.Flatbed,[150,300],[ScanColor.Color],["A4"])],true));
        public Task<DeviceInfo> ConnectEndpointAsync(string endpoint,CancellationToken ct)=>throw new NotSupportedException();
        public Task ScanAsync(DeviceInfo device,ScanSettings settings,string folder,Func<PageData,Task> acquired,nint handle,CancellationToken ct)=>throw new NotSupportedException("The progress overload must be used.");
        public async Task ScanAsync(DeviceInfo device,ScanSettings settings,string folder,Func<PageData,Task> acquired,nint handle,IProgress<ScanProgress>? progress,CancellationToken ct)
        {
            Assert.NotNull(progress);
            async Task Then(Func<Task> action){await Step.WaitAsync(ct);await action();Reached.Release();}
            await Then(()=>{progress.Report(new(1,null));return Task.CompletedTask;});
            await Then(()=>{progress.Report(new(1,0.5));return Task.CompletedTask;});
            await Then(()=>acquired(Page(folder,1)));
            await Then(()=>{progress.Report(new(2,null));return Task.CompletedTask;});
            await Then(()=>acquired(Page(folder,2)));
        }
        public void Dispose(){Step.Dispose();Reached.Dispose();}
    }

    private static PageData Page(string folder,int number)
    {
        var bitmap=BitmapSource.Create(40,60,96,96,PixelFormats.Gray8,null,Enumerable.Repeat((byte)255,40*60).ToArray(),40);bitmap.Freeze();
        string file=$"page-{number}.png";Imaging.SavePng(bitmap,Path.Combine(folder,file));
        return new(){Id=Guid.NewGuid(),OriginalFile=file,ThumbnailFile=file,Width=40,Height=60,DpiX=96,DpiY=96};
    }

    private static async Task Settle(){for(int i=0;i<3;i++)await Dispatcher.Yield(DispatcherPriority.ContextIdle);}

    [Fact]public Task ProgressFollowsTheDriverPageByPageAndClearsAfterTheScan()=>ExportTests.OnSta(async()=>{
        string root=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));
        try
        {
            var paths=new AppPaths(root);var store=new SettingsStore(paths);var profile=new ScanProfile("Default",Device,new());store.Save(new(){Profiles=[profile],DefaultProfileId=profile.Id});
            using var scanner=new SteppedScanner();using var vm=new MainViewModel(scanner,new DocumentExporter(),store,new SessionStore(paths),NullLogger<MainViewModel>.Instance);
            await vm.InitializeAsync();Assert.True(vm.ScanCommand.CanExecute(null));
            Assert.False(vm.IsScanning);Assert.Equal(TaskbarItemProgressState.None,vm.TaskbarProgressState);

            vm.ScanCommand.Execute(null);
            var end=DateTime.UtcNow.AddSeconds(10);while(!vm.IsScanning&&DateTime.UtcNow<end)await Dispatcher.Yield(DispatcherPriority.ContextIdle);
            Assert.True(vm.IsScanning);Assert.Equal("Starting the scanner…",vm.ScanProgressMessage);Assert.True(vm.ScanProgressIndeterminate);
            Assert.Equal(TaskbarItemProgressState.Indeterminate,vm.TaskbarProgressState);

            async Task Next(){scanner.Step.Release();Assert.True(await scanner.Reached.WaitAsync(TimeSpan.FromSeconds(10)));await Settle();}
            await Next();Assert.Equal("Scanning page 1…",vm.ScanProgressMessage);Assert.True(vm.ScanProgressIndeterminate);
            await Next();Assert.Equal("Scanning page 1 · 50 %",vm.ScanProgressMessage);Assert.False(vm.ScanProgressIndeterminate);Assert.Equal(50,vm.ScanProgressValue);
            Assert.Equal(TaskbarItemProgressState.Normal,vm.TaskbarProgressState);Assert.Equal(0.5,vm.TaskbarProgressValue,3);
            await Next();Assert.Single(vm.Pages);Assert.Equal("Page 1 received · waiting for the next page…",vm.ScanProgressMessage);Assert.True(vm.ScanProgressIndeterminate);
            await Next();Assert.Equal("Scanning page 2…",vm.ScanProgressMessage);
            scanner.Step.Release();
            end=DateTime.UtcNow.AddSeconds(10);while(vm.Busy&&DateTime.UtcNow<end)await Dispatcher.Yield(DispatcherPriority.ContextIdle);
            Assert.False(vm.Busy);Assert.Equal(2,vm.Pages.Count);
            Assert.False(vm.IsScanning);Assert.Equal("",vm.ScanProgressMessage);Assert.Equal(0,vm.ScanProgressValue);Assert.Equal(TaskbarItemProgressState.None,vm.TaskbarProgressState);
        }
        finally{try{Directory.Delete(root,true);}catch(IOException){}}
    });

    private static IEnumerable<Button> Buttons(DependencyObject root)
    {
        foreach(object child in LogicalTreeHelper.GetChildren(root))
        {
            if(child is Button button)yield return button;
            if(child is DependencyObject element)foreach(var inner in Buttons(element))yield return inner;
        }
    }

    private static void AssertAccent(Window window,string text)
    {
        var button=Buttons(window).Single(b=>Equals(b.Content,text));
        // A WPF UI accent button keeps the teal background and white text on hover; a hand-coloured plain Button does not.
        var accent=Assert.IsType<Wpf.Ui.Controls.Button>(button);
        Assert.Equal(Wpf.Ui.Controls.ControlAppearance.Primary,accent.Appearance);
        Assert.Equal(DependencyProperty.UnsetValue,button.ReadLocalValue(Control.ForegroundProperty));
        Assert.Equal(DependencyProperty.UnsetValue,button.ReadLocalValue(Control.BackgroundProperty));
    }

    [Fact]public Task PrimaryDialogButtonsAreAccentButtons()=>ExportTests.OnSta(()=>{
        string folder=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try
        {
            var settings=new SettingsWindow([],[],folder);
            AssertAccent(settings,"Save site");AssertAccent(settings,"Register ERPNext scan link");
            AssertAccent(new ProfileManagerWindow(new(),[Device],null,null,_=>{}),"Save profile");
            AssertAccent(new ExportWindow(new(),1),Text.Get("Export"));
            // Secondary buttons keep the default look.
            Assert.IsNotType<Wpf.Ui.Controls.Button>(Buttons(settings).First(b=>Equals(b.Content,Text.Get("OpenFolder"))));
        }
        finally{Directory.Delete(folder,true);}
        return Task.CompletedTask;
    });
}
