using DocumentScanner.Core;
using NAPS2.Images;
using NAPS2.Scan;
namespace DocumentScanner.Scanning;

public static class ScanOptionsFactory
{
    public static ScanOptions Create(DeviceInfo device,ScanSettings settings,nint parent=0)
    {
        settings.Validate();var driver=Enum.Parse<Driver>(device.Driver);
        return new ScanOptions
        {
            Device=new ScanDevice(driver,device.Id,device.Name),Driver=driver,
            TwainOptions=new(){Dsm=device.Twain64?TwainDsm.NewX64:TwainDsm.New},
            PaperSource=settings.Source switch{ScanSource.Feeder=>PaperSource.Feeder,ScanSource.Duplex=>PaperSource.Duplex,_=>PaperSource.Flatbed},
            Dpi=settings.Dpi,BitDepth=settings.Color switch{ScanColor.Grayscale=>BitDepth.Grayscale,ScanColor.BlackAndWhite=>BitDepth.BlackAndWhite,_=>BitDepth.Color},
            PageSize=settings.Paper=="Auto"?null:PageSize.Parse(settings.Paper),
            AutoDeskew=settings.Deskew,MaxQuality=true,ExcludeBlankPages=false,
            // Explicitly request neutral driver settings on EVERY scan. App tone is page metadata,
            // rendered once from the original; Auto Crop never changes acquisition/tone settings.
            Brightness=0,Contrast=0,BrightnessContrastAfterScan=false,
            UseNativeUI=settings.NativeUi,DialogParent=parent
        };
    }
}
