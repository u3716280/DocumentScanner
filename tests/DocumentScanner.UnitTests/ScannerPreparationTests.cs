using DocumentScanner.Core;
using Xunit;
namespace DocumentScanner.UnitTests;
public sealed class ScannerPreparationTests
{
    private static DeviceInfo Device()=>new("Wia|False|device","Scanner","Wia","device");
    private static ScanProfile Profile()=>new("P",Device(),new());
    private sealed class Scanner : IScannerService
    {
        public int AllCalls,FindCalls,CapCalls;public DeviceInfo? Found=Device();public DeviceInfo? Requested;public bool CapFails,CancelAfterCaps;public CancellationTokenSource? Source;
        public Task<(IReadOnlyList<DeviceInfo>,IReadOnlyList<string>)> DiscoverAsync(CancellationToken ct){AllCalls++;return Task.FromResult(((IReadOnlyList<DeviceInfo>)new[]{Device()},(IReadOnlyList<string>)Array.Empty<string>()));}
        public Task<DeviceInfo?> FindDeviceAsync(DeviceInfo saved,CancellationToken ct){ct.ThrowIfCancellationRequested();Requested=saved;FindCalls++;return Task.FromResult(Found);}
        public Task<ScannerCapabilities> GetCapabilitiesAsync(DeviceInfo d,CancellationToken ct){CapCalls++;if(CapFails)throw new IOException("Offline");if(CancelAfterCaps)Source!.Cancel();return Task.FromResult(new ScannerCapabilities([new(ScanSource.Flatbed,[300],[ScanColor.Color],["A4"])],true));}
        public Task<DeviceInfo> ConnectEndpointAsync(string endpoint,CancellationToken ct)=>throw new NotSupportedException();
        public Task ScanAsync(DeviceInfo device,ScanSettings settings,string folder,Func<PageData,Task> acquired,nint handle,CancellationToken ct)=>throw new NotSupportedException();
        public void Dispose(){}
    }
    [Fact]public void FirstUseSearchesAllEvenWithStaleDefaultReference()
    {Assert.Equal(StartupDiscoveryMode.DiscoverAll,StartupDiscoveryPlan.Create([],null).Mode);Assert.Equal(StartupDiscoveryMode.DiscoverAll,StartupDiscoveryPlan.Create([],Guid.NewGuid()).Mode);}
    [Fact]public void ExistingDefaultLoadsDirectlyAndMissingDefaultDoesNotSearchAll()
    {var p=Profile();Assert.Equal(p,StartupDiscoveryPlan.Create([p],p.Id).Profile);Assert.Equal(StartupDiscoveryMode.LoadDefaultProfile,StartupDiscoveryPlan.Create([p],p.Id).Mode);Assert.Equal(StartupDiscoveryMode.ChooseProfile,StartupDiscoveryPlan.Create([p],null).Mode);Assert.Equal(StartupDiscoveryMode.ChooseProfile,StartupDiscoveryPlan.Create([p],Guid.NewGuid()).Mode);}
    [Fact]public async Task ProfilePreparationFindsOnlyExactDeviceAndReadsCapabilitiesOnce()
    {using var s=new Scanner();var p=Profile();var result=await new ScannerPreparation(s).PrepareProfileAsync(p,default);Assert.Equal(p.Device,s.Requested);Assert.Equal(0,s.AllCalls);Assert.Equal(1,s.FindCalls);Assert.Equal(1,s.CapCalls);Assert.Equal(p.Device,result.Device);Assert.True(result.FindMilliseconds>=0);Assert.True(result.CapabilitiesMilliseconds>=0);Assert.Equal(p.Settings,new ScanSettings());}
    [Fact]public async Task MissingProfileScannerNeverFallsBackToAllOrAnotherDevice()
    {using var s=new Scanner{Found=null};await Assert.ThrowsAsync<InvalidOperationException>(()=>new ScannerPreparation(s).PrepareProfileAsync(Profile(),default));Assert.Equal(0,s.AllCalls);Assert.Equal(0,s.CapCalls);}
    [Theory][InlineData("driver")][InlineData("id")][InlineData("architecture")][InlineData("key")]
    public async Task ChangedScannerIdentityIsRejectedBeforeCapabilityQuery(string change)
    {using var s=new Scanner();s.Found=change switch{"driver"=>Device() with{Driver="Twain"},"id"=>Device() with{Id="other"},"architecture"=>Device() with{Twain64=true},_=>Device() with{Key="other"}};await Assert.ThrowsAsync<InvalidOperationException>(()=>new ScannerPreparation(s).PrepareProfileAsync(Profile(),default));Assert.Equal(0,s.CapCalls);Assert.Equal(0,s.AllCalls);}
    [Fact]public async Task CapabilityFailureDoesNotTriggerFullDiscoveryOrModifyProfile()
    {using var s=new Scanner{CapFails=true};var p=Profile();await Assert.ThrowsAsync<IOException>(()=>new ScannerPreparation(s).PrepareProfileAsync(p,default));Assert.Equal(0,s.AllCalls);Assert.Equal(new ScanSettings(),p.Settings);}
    [Fact]public async Task CancellationBeforeLookupDoesNotQueryDriver()
    {using var s=new Scanner();using var cancellation=new CancellationTokenSource();cancellation.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>new ScannerPreparation(s).PrepareProfileAsync(Profile(),cancellation.Token));Assert.Equal(0,s.FindCalls);Assert.Equal(0,s.CapCalls);}
    [Fact]public async Task LateDriverCompletionCannotBecomeReadyAfterCancellation()
    {using var cancellation=new CancellationTokenSource();using var s=new Scanner{CancelAfterCaps=true,Source=cancellation};await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>new ScannerPreparation(s).PrepareProfileAsync(Profile(),cancellation.Token));Assert.Equal(1,s.CapCalls);}
}
