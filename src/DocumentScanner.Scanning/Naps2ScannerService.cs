using System.IO;
using DocumentScanner.ImagingSupport;
using DocumentScanner.Core;
using DocumentScanner.Infrastructure;
using NAPS2.Images;
using NAPS2.Images.Wpf;
using NAPS2.Scan;
using NAPS2.Scan.Exceptions;
using System.Windows.Media.Imaging;
namespace DocumentScanner.Scanning;
public sealed class Naps2ScannerService : IScannerService
{
    private readonly SemaphoreSlim gate=new(1,1);
    private readonly WpfImageContext images=new();
    public async Task<(IReadOnlyList<DeviceInfo> Devices,IReadOnlyList<string> Warnings)> DiscoverAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try {
            using var context=CreateContext(); var controller=new ScanController(context);
            var found=new List<DeviceInfo>(); var warnings=new List<string>();
            var drivers=new List<(Driver Driver,bool X64)>{(Driver.Wia,false),(Driver.Twain,false),(Driver.Escl,false)};
            if(Environment.Is64BitProcess)drivers.Add((Driver.Twain,true));
            foreach(var (driver,x64) in drivers)
            {
                ct.ThrowIfCancellationRequested();
                try {
                    var options=new ScanOptions{ Driver=driver, TwainOptions=new(){ Dsm=x64?TwainDsm.NewX64:TwainDsm.New } };
                    await foreach(var device in controller.GetDevices(options,ct))
                        found.Add(new($"{driver}|{x64}|{device.ID}",device.Name,driver.ToString(),device.ID,x64));
                } catch(OperationCanceledException){throw;}
                catch(Exception ex){warnings.Add($"{driver}{(x64?" 64-bit":"")}: {ScannerErrors.Describe(ex)}");}
            }
            return(found.DistinctBy(d=>d.Key).ToList(),warnings);
        } finally {gate.Release();}
    }
    private ScanningContext CreateContext(string? work=null)
    {
        var context=new ScanningContext(images);
        context.SetUpWin32Worker();
        if(work!=null){Directory.CreateDirectory(work);context.FileStorageManager=new FileStorageManager(work);context.TempFolderPath=work;}
        return context;
    }
    private static ScanDevice Native(DeviceInfo device) => new(Enum.Parse<Driver>(device.Driver),device.Id,device.Name);
    private static ScanOptions Options(DeviceInfo device) => new(){Device=Native(device),TwainOptions=new(){Dsm=device.Twain64?TwainDsm.NewX64:TwainDsm.New}};
    public async Task<ScannerCapabilities> GetCapabilitiesAsync(DeviceInfo device,CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try {
            using var context=CreateContext();var caps=await new ScanController(context).GetCaps(Options(device),ct);
            var sources=new List<SourceCapabilities>();
            var paper=caps.PaperSourceCaps;
            foreach(var (source,allowed,values) in new[]{
                (ScanSource.Flatbed,paper?.SupportsFlatbed??false,caps.FlatbedCaps),
                (ScanSource.Feeder,paper?.SupportsFeeder??false,caps.FeederCaps),
                (ScanSource.Duplex,paper?.SupportsDuplex??false,caps.DuplexCaps)})
            {
                if(!allowed)continue;
                ScanColor[]? colors=null;
                if(values?.BitDepthCaps is {} bit)colors=new[]{
                    (ScanColor.Color,bit.SupportsColor),(ScanColor.Grayscale,bit.SupportsGrayscale),(ScanColor.BlackAndWhite,bit.SupportsBlackAndWhite)}
                    .Where(x=>x.Item2).Select(x=>x.Item1).ToArray();
                string[]? sizes=values?.PageSizeCaps?.ScanArea==null?null:new[]{"A4","A5","Letter","Legal"}
                    .Where(p=>values.PageSizeCaps.Fits(PageSize.Parse(p)!)).ToArray();
                sources.Add(new(source,values?.DpiCaps?.Values?.ToArray(),colors,sizes));
            }
            return new(sources.ToArray(),paper!=null,paper==null?"Capabilities unavailable. Use the scanner's own settings dialog; duplex is not assumed.":null);
        } finally {gate.Release();}
    }
    public async Task<DeviceInfo> ConnectEndpointAsync(string endpoint,CancellationToken ct)
    {
        endpoint=endpoint.Trim();if(!endpoint.Contains("://"))endpoint="http://"+endpoint+"/eSCL/";
        if(!Uri.TryCreate(endpoint,UriKind.Absolute,out var uri)||uri.Scheme is not("http" or "https")||!string.IsNullOrEmpty(uri.UserInfo)||!string.IsNullOrEmpty(uri.Query)||!string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("Enter an HTTP/HTTPS scanner endpoint, for example http://192.168.1.10/eSCL/.");
        var normalized=uri.AbsoluteUri.TrimEnd('/')+"/";
        var device=new DeviceInfo("Escl|False|"+normalized,uri.Host,"Escl",normalized);
        var caps=await GetCapabilitiesAsync(device,ct);
        if(!caps.Known || caps.Sources.Length==0)throw new InvalidOperationException("The endpoint did not provide scanner capabilities.");
        return device;
    }
    public async Task ScanAsync(DeviceInfo device,ScanSettings settings,string folder,Func<PageData,Task> acquired,nint handle,CancellationToken ct)
    {
        settings.Validate();await gate.WaitAsync(ct);
        try {
            Directory.CreateDirectory(folder);using var context=CreateContext(Path.Combine(folder,"worker-temp"));
            var options=ScanOptionsFactory.Create(device,settings,handle);
            // Auto means maximum driver-reported scan area; it is not automatic document-edge detection.
            options.PageSize=settings.Paper=="Auto"?(await new ScanController(context).GetCaps(options,ct)).FlatbedCaps?.PageSizeCaps?.ScanArea??PageSize.A4:PageSize.Parse(settings.Paper);
            if(settings.Paper=="Auto"&&settings.Source!=ScanSource.Flatbed)
            {
                var caps=await new ScanController(context).GetCaps(options,ct);
                options.PageSize=(settings.Source==ScanSource.Duplex?caps.DuplexCaps:caps.FeederCaps)?.PageSizeCaps?.ScanArea??options.PageSize;
            }
            int count=0;
            try {
                await foreach(var image in new ScanController(context).Scan(options,ct))
                {
                    using(image)
                    {
                        var id=Guid.NewGuid();var original=$"{id:N}.png";var thumb=$"{id:N}-thumb.png";
                        // Finish and persist any page already yielded, even if cancellation arrives now.
                        string destination=Path.Combine(folder,original),temp=destination+".tmp";
                        image.Save(temp,ImageFileFormat.Png);File.Move(temp,destination,false);
                        var bitmap=images.RenderToBitmapSource(image);bitmap.Freeze();
                        Imaging.SavePng(Imaging.Thumbnail(bitmap,180),Path.Combine(folder,thumb));
                        var page=new PageData{Id=id,OriginalFile=original,ThumbnailFile=thumb,Width=bitmap.PixelWidth,Height=bitmap.PixelHeight,
                            DpiX=bitmap.DpiX>0?bitmap.DpiX:settings.Dpi,DpiY=bitmap.DpiY>0?bitmap.DpiY:settings.Dpi,
                            Rotation=settings.Landscape&&bitmap.PixelHeight>bitmap.PixelWidth?90:0,AutoCrop=settings.AutoCrop,
                            Brightness=settings.Brightness,Contrast=settings.Contrast,
                            BlankCandidate=(settings.DetectBlank||settings.RemoveBlank)&&Imaging.Analyze(bitmap).Blank};
                        await acquired(page);count++;
                    }
                }
            } catch(DeviceFeederEmptyException) when(count>0){}
            ct.ThrowIfCancellationRequested();
            if(count==0)throw new InvalidOperationException("No pages were acquired. Check the feeder and scanner settings.");
        } finally {gate.Release();}
    }
    public void Dispose()=>gate.Dispose();
}
public static class ScannerErrors
{
    public static string Describe(Exception error) => error switch {
        DeviceFeederEmptyException=>"The feeder is empty. Load paper and try again.",
        DevicePaperJamException=>"Paper jam. Clear the feeder before continuing; acquired pages are preserved.",
        DeviceBusyException=>"The scanner is busy. Close other scanning applications and retry.",
        DeviceOfflineException=>"The scanner is offline or disconnected.",
        DeviceCommunicationException=>"The scanner could not be reached. Check its cable or network connection.",
        NoDuplexSupportException=>"This driver does not support duplex scanning.",
        NoFeederSupportException=>"This driver does not support the document feeder.",
        _=>"Scanner operation failed. Check the connection, driver, and selected scan options. Try the native settings dialog or another driver."
    };
}
