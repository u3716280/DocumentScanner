using System.IO;
using System.Windows.Threading;
using DocumentScanner.App;
using DocumentScanner.Core;
using DocumentScanner.Export;
using DocumentScanner.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
namespace DocumentScanner.IntegrationTests;
public sealed class StartupTests
{
    private static readonly DeviceInfo Device=new("Wia|False|d","Scanner","Wia","d");
    private static ScanProfile Profile()=>new("Default",Device,new());
    private sealed class Scanner : IScannerService
    {
        public int All,Find,Caps;
        public Task<(IReadOnlyList<DeviceInfo>,IReadOnlyList<string>)> DiscoverAsync(CancellationToken ct){All++;return Task.FromResult(((IReadOnlyList<DeviceInfo>)new[]{Device},(IReadOnlyList<string>)Array.Empty<string>()));}
        public Task<DeviceInfo?> FindDeviceAsync(DeviceInfo saved,CancellationToken ct){Find++;return Task.FromResult<DeviceInfo?>(saved);}
        public Task<ScannerCapabilities> GetCapabilitiesAsync(DeviceInfo device,CancellationToken ct){Caps++;return Task.FromResult(new ScannerCapabilities([new(ScanSource.Flatbed,[150,300,600],[ScanColor.Color,ScanColor.Grayscale],["A4","Letter"])],true));}
        public Task<DeviceInfo> ConnectEndpointAsync(string endpoint,CancellationToken ct)=>throw new NotSupportedException();
        public Task ScanAsync(DeviceInfo device,ScanSettings settings,string folder,Func<PageData,Task> acquired,nint handle,CancellationToken ct)=>throw new NotSupportedException();public void Dispose(){}
    }
    private static async Task Wait(MainViewModel vm){var end=DateTime.UtcNow.AddSeconds(10);while(vm.Busy&&DateTime.UtcNow<end)await Dispatcher.Yield(DispatcherPriority.ContextIdle);Assert.False(vm.Busy);}
    [Fact]public Task FirstUseDiscoversAllAndInitializationRunsOnlyOnce()=>ExportTests.OnSta(async()=>{
        string root=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));try{var paths=new AppPaths(root);using var scanner=new Scanner();using var vm=new MainViewModel(scanner,new DocumentExporter(),new SettingsStore(paths),new SessionStore(paths),NullLogger<MainViewModel>.Instance);
        await Task.WhenAll(vm.InitializeAsync(),vm.InitializeAsync());Assert.Equal(1,scanner.All);Assert.Equal(0,scanner.Find);Assert.Equal(1,scanner.Caps);Assert.Null(vm.SelectedProfile);Assert.Equal(Device,vm.SelectedDevice);
        }finally{Directory.Delete(root,true);}
    });
    [Fact]public Task DefaultProfileIsPreparedWithoutFullDiscoveryAndResetKeepsSavedProfiles()=>ExportTests.OnSta(async()=>{
        string root=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));try{var paths=new AppPaths(root);var store=new SettingsStore(paths);var profile=Profile();store.Save(new(){Profiles=[profile],DefaultProfileId=profile.Id});using var scanner=new Scanner();using var vm=new MainViewModel(scanner,new DocumentExporter(),store,new SessionStore(paths),NullLogger<MainViewModel>.Instance);
        var window=new MainWindow(vm,paths);window.Measure(new System.Windows.Size(1440,960));window.Arrange(new System.Windows.Rect(0,0,1440,960));
        await vm.InitializeAsync();await Dispatcher.Yield(DispatcherPriority.ContextIdle);Assert.Equal(0,scanner.All);Assert.Equal(1,scanner.Find);Assert.Equal(1,scanner.Caps);Assert.Equal(profile,vm.SelectedProfile);Assert.Equal(Device,vm.SelectedDevice);
        vm.SelectedDevice=null;await Dispatcher.Yield(DispatcherPriority.ContextIdle);Assert.Equal(Device,vm.SelectedDevice);Assert.Equal(1,scanner.Caps);
        vm.ResetProfileCommand.Execute(null);await Wait(vm);Assert.Equal(1,scanner.All);Assert.Equal(1,scanner.Find);Assert.Equal(2,scanner.Caps);Assert.Null(vm.SelectedProfile);Assert.Single(vm.Profiles);Assert.Equal(profile.Id,store.Load().DefaultProfileId);
        }finally{Directory.Delete(root,true);}
    });
    [Theory][InlineData(false)][InlineData(true)]public Task ProfilesWithoutUsableDefaultWaitForSelectionThenProbeOnlyChosenDevice(bool staleDefault)=>ExportTests.OnSta(async()=>{
        string root=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));try{var paths=new AppPaths(root);var store=new SettingsStore(paths);var profile=Profile();store.Save(new(){Profiles=[profile],DefaultProfileId=staleDefault?Guid.NewGuid():null});using var scanner=new Scanner();using var vm=new MainViewModel(scanner,new DocumentExporter(),store,new SessionStore(paths),NullLogger<MainViewModel>.Instance);
        await vm.InitializeAsync();Assert.Equal(0,scanner.All);Assert.Equal(0,scanner.Find);Assert.Equal(0,scanner.Caps);Assert.Null(vm.SelectedDevice);Assert.False(vm.ScanCommand.CanExecute(null));
        vm.SelectedProfile=vm.Profiles[0];await Wait(vm);Assert.Equal(0,scanner.All);Assert.Equal(1,scanner.Find);Assert.Equal(1,scanner.Caps);Assert.Equal(Device,vm.SelectedDevice);Assert.True(vm.ScanCommand.CanExecute(null));
        }finally{Directory.Delete(root,true);}
    });
    [Fact]public Task LoadingDifferentProfileWorksWithoutPreviouslyEnumeratedDevice()=>ExportTests.OnSta(async()=>{
        string root=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));try{var paths=new AppPaths(root);var store=new SettingsStore(paths);var first=Profile();var second=first with{Id=Guid.NewGuid(),Name="Other device",Device=new("Twain|False|other","Other","Twain","other"),Settings=new(){Dpi=600,Color=ScanColor.Grayscale,Paper="Letter"}};store.Save(new(){Profiles=[first,second],DefaultProfileId=first.Id});using var scanner=new Scanner();using var vm=new MainViewModel(scanner,new DocumentExporter(),store,new SessionStore(paths),NullLogger<MainViewModel>.Instance);
        await vm.InitializeAsync();Assert.DoesNotContain(vm.Devices,d=>d.Id=="other");vm.SelectedProfile=vm.Profiles[1];await Wait(vm);Assert.Equal(0,scanner.All);Assert.Equal(2,scanner.Find);Assert.Equal(2,scanner.Caps);Assert.Equal(second.Device,vm.SelectedDevice);Assert.Equal(600,vm.Dpi);Assert.Equal(ScanColor.Grayscale,vm.Color);Assert.Equal("Letter",vm.Paper);
        }finally{Directory.Delete(root,true);}
    });
}
