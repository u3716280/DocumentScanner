using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DocumentScanner.App;
using DocumentScanner.Core;
using DocumentScanner.Export;
using DocumentScanner.Infrastructure;
using DocumentScanner.Scanning;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
namespace DocumentScanner.IntegrationTests;
public sealed class RedesignTests
{
    private sealed class Scanner : IScannerService
    {
        public static readonly ScannerCapabilities Caps=new([new(ScanSource.Duplex,[200,600],[ScanColor.Grayscale,ScanColor.BlackAndWhite],["Letter","A4"])],true);
        public Task<ScannerCapabilities> GetCapabilitiesAsync(DeviceInfo device,CancellationToken ct)=>Task.FromResult(Caps);
        public Task<(IReadOnlyList<DeviceInfo>,IReadOnlyList<string>)> DiscoverAsync(CancellationToken ct)=>Task.FromResult(((IReadOnlyList<DeviceInfo>)Array.Empty<DeviceInfo>(),(IReadOnlyList<string>)Array.Empty<string>()));
        public Task<DeviceInfo> ConnectEndpointAsync(string endpoint,CancellationToken ct)=>throw new NotSupportedException();
        public Task ScanAsync(DeviceInfo device,ScanSettings settings,string folder,Func<PageData,Task> acquired,nint handle,CancellationToken ct)=>throw new NotSupportedException();
        public void Dispose(){}
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T:DependencyObject
    {foreach(var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>()){if(child is T t)yield return t;foreach(var nested in Descendants<T>(child))yield return nested;}}
    [Fact]public Task CompleteProfileLoadUpdatesViewModelActualWpfControlsAndEngineOptions()=>ExportTests.OnSta(async()=>{
        string root=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));
        try{
            var paths=new AppPaths(root);var settings=new SettingsStore(paths);var device=new DeviceInfo("test","Scanner","Wia","id");var profile=new ScanProfile("Duplex gray",device,new(){Source=ScanSource.Duplex,Dpi=600,Color=ScanColor.Grayscale,Paper="Letter",Brightness=120,Contrast=-50}){Output=new(){Prefix="POD",Format=ExportFormat.Png},Erp=new(){DocType="Delivery Note"}};settings.Save(new(){Profiles=[profile]});
            using var vm=new MainViewModel(new Scanner(),new DocumentExporter(),settings,new SessionStore(paths),NullLogger<MainViewModel>.Instance);vm.Devices.Add(device);vm.SelectedProfile=vm.Profiles[0];vm.LoadProfileCommand.Execute(null);
            var end=DateTime.UtcNow.AddSeconds(10);while(vm.Busy&&DateTime.UtcNow<end)await Dispatcher.Yield(DispatcherPriority.ContextIdle);Assert.False(vm.Busy);
            var window=new MainWindow(vm,paths);window.Measure(new Size(1440,960));window.Arrange(new Rect(0,0,1440,960));await Dispatcher.Yield(DispatcherPriority.ContextIdle);
            Assert.Equal(ScanSource.Duplex,vm.Source);Assert.Equal(600,vm.Dpi);Assert.Equal(ScanColor.Grayscale,vm.Color);Assert.Equal("POD",vm.FilenamePrefix);Assert.Equal(ExportFormat.Png,vm.OutputFormat);
            var controls=Descendants<ComboBox>(window).ToArray();Assert.Contains(controls,c=>Equals(c.SelectedItem,600));Assert.Contains(controls,c=>Equals(c.SelectedItem,ScanSource.Duplex));Assert.Contains(controls,c=>Equals(c.SelectedItem,ScanColor.Grayscale));
            var buttons=Descendants<Button>(window).ToArray();foreach(string text in new[]{"Save Local","Send to ERPNext","Save Local & Send to ERPNext"}){var b=Assert.Single(buttons,b=>Equals(b.Content,text));Assert.False(b.IsEnabled);}
            var options=ScanOptionsFactory.Create(device,profile.Settings);Assert.Equal(600,options.Dpi);Assert.Equal(0,options.Brightness);Assert.Equal(0,options.Contrast);
        }finally{Directory.Delete(root,true);}
    });
    [Fact]public Task ProfileManagerHasAllFourGroupsAndDoesNotApplyOnOpen()=>ExportTests.OnSta(async()=>{
        var device=new DeviceInfo("test","Scanner","Wia","id");var profile=new ScanProfile("P",device,new());int saves=0;
        var window=new ProfileManagerWindow(new(){Profiles=[profile]},[device],profile,profile,_=>saves++);window.Measure(new Size(1000,820));window.Arrange(new Rect(0,0,1000,820));await Dispatcher.Yield(DispatcherPriority.ContextIdle);
        var tabs=Descendants<TabItem>(window).Select(t=>t.Header?.ToString()).ToArray();foreach(string expected in new[]{"Scanner","Processing","Output","ERPNext"})Assert.Contains(expected,tabs);
        Assert.Equal(0,saves);Assert.Null(window.LoadRequested);
    });
}
