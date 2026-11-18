using System.IO;
using System.Windows;
using System.Windows.Threading;
using DocumentScanner.App;
using DocumentScanner.Core;
using DocumentScanner.Export;
using DocumentScanner.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
namespace DocumentScanner.IntegrationTests;
public sealed class TrayTests
{
    private sealed class NoScanner : IScannerService
    {
        public Task<(IReadOnlyList<DeviceInfo>,IReadOnlyList<string>)> DiscoverAsync(CancellationToken ct)=>Task.FromResult(((IReadOnlyList<DeviceInfo>)[],(IReadOnlyList<string>)[]));
        public Task<DeviceInfo?> FindDeviceAsync(DeviceInfo saved,CancellationToken ct)=>Task.FromResult<DeviceInfo?>(null);
        public Task<ScannerCapabilities> GetCapabilitiesAsync(DeviceInfo device,CancellationToken ct)=>throw new NotSupportedException();
        public Task<DeviceInfo> ConnectEndpointAsync(string endpoint,CancellationToken ct)=>throw new NotSupportedException();
        public Task ScanAsync(DeviceInfo device,ScanSettings settings,string folder,Func<PageData,Task> acquired,nint handle,CancellationToken ct)=>throw new NotSupportedException();
        public void Dispose(){}
    }

    [Fact]public Task ClosingHidesTheWindowToTheTrayAndShowFromTrayBringsItBack()=>ExportTests.OnSta(async()=>{
        string root=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));
        try
        {
            var paths=new AppPaths(root);using var vm=new MainViewModel(new NoScanner(),new DocumentExporter(),new SettingsStore(paths),new SessionStore(paths),NullLogger<MainViewModel>.Instance);
            var window=new MainWindow(vm,paths){ShowActivated=false,WindowStartupLocation=WindowStartupLocation.Manual,Left=-20000,Top=-20000,CloseToTray=true};
            int hidden=0;window.HiddenToTray+=(_,_)=>hidden++;
            window.Show();await vm.InitializeAsync();await Dispatcher.Yield(DispatcherPriority.ContextIdle);
            // Close (the X button, Alt+F4) hides the window; it does not ask about the session or end the application.
            window.Close();await Dispatcher.Yield(DispatcherPriority.ContextIdle);
            Assert.Equal(1,hidden);Assert.False(window.IsVisible);Assert.True(PresentationSource.FromVisual(window)!=null,"The window must stay alive, only hidden.");
            window.ShowFromTray();await Dispatcher.Yield(DispatcherPriority.ContextIdle);
            Assert.True(window.IsVisible);Assert.Equal(WindowState.Normal,window.WindowState);
            window.WindowState=WindowState.Minimized;window.Close();window.ShowFromTray();
            Assert.Equal(2,hidden);Assert.True(window.IsVisible);Assert.Equal(WindowState.Normal,window.WindowState);
        }
        finally{try{Directory.Delete(root,true);}catch(IOException){}}
    });
}
