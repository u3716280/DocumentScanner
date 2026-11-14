using System.Diagnostics;
namespace DocumentScanner.Core;
public enum StartupDiscoveryMode { DiscoverAll, LoadDefaultProfile, ChooseProfile }
public sealed record StartupDiscoveryPlan(StartupDiscoveryMode Mode,ScanProfile? Profile)
{
    public static StartupDiscoveryPlan Create(IReadOnlyList<ScanProfile> profiles,Guid? defaultId)
    {
        if(profiles.Count==0)return new(StartupDiscoveryMode.DiscoverAll,null);
        var profile=profiles.FirstOrDefault(p=>p.Id==defaultId);
        return profile==null?new(StartupDiscoveryMode.ChooseProfile,null):new(StartupDiscoveryMode.LoadDefaultProfile,profile);
    }
}
public sealed record PreparedScanner(DeviceInfo Device,ScannerCapabilities Capabilities,double FindMilliseconds,double CapabilitiesMilliseconds);
public sealed class ScannerPreparation(IScannerService scanner)
{
    public async Task<PreparedScanner> PrepareProfileAsync(ScanProfile profile,CancellationToken ct)
    {
        ProfileValidation.Validate(profile);
        long started=Stopwatch.GetTimestamp();
        var device=await scanner.FindDeviceAsync(profile.Device,ct);
        double find=Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        ct.ThrowIfCancellationRequested();
        if(device==null)throw new InvalidOperationException("The profile scanner is unavailable. Check its power/connection, choose another profile, or use Reset Profile to search all devices. No automatic full search or substitution was performed.");
        if(device.Driver!=profile.Device.Driver||device.Id!=profile.Device.Id||device.Twain64!=profile.Device.Twain64||device.Key!=profile.Device.Key)
            throw new InvalidOperationException("The scanner identity/driver does not match the profile. No settings were applied.");
        ct.ThrowIfCancellationRequested();started=Stopwatch.GetTimestamp();
        var caps=await scanner.GetCapabilitiesAsync(device,ct);
        ct.ThrowIfCancellationRequested();
        return new(device,caps,find,Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }
}
