using System.Collections.Specialized;
using DocumentScanner.Core;
using DocumentScanner.Infrastructure;
using Xunit;
namespace DocumentScanner.UnitTests;
public sealed class ProfileSelectionTests
{
    private static ScannerCapabilities Caps()=>new([
        new(ScanSource.Flatbed,[100,150,300],[ScanColor.Color,ScanColor.Grayscale],["A4","A5","Letter"]),
        new(ScanSource.Feeder,[200,400],[ScanColor.Grayscale,ScanColor.BlackAndWhite],["A4","Legal"]),
        new(ScanSource.Duplex,[200,600],[ScanColor.Grayscale,ScanColor.BlackAndWhite],["A4","Letter"])
    ],true);
    private static ScanSettings Duplex()=>new(){Source=ScanSource.Duplex,Dpi=600,Color=ScanColor.BlackAndWhite,Paper="Letter",Landscape=true,Deskew=false,AutoCrop=true,DetectBlank=true,Brightness=100,Contrast=-200};
    private static void SimulateComboBoxResets(ScanSettingsSelection model)
    {
        model.Sources.CollectionChanged+=(_,_)=>model.Source=ScanSource.Flatbed;
        model.Dpis.CollectionChanged+=(_,_)=>model.Dpi=0;
        model.Colors.CollectionChanged+=(_,_)=>model.Color=ScanColor.Color;
        model.Papers.CollectionChanged+=(_,_)=>model.Paper="";
    }
    [Fact]public void ProfileRestorationSurvivesReentrantComboBoxResets()
    {
        var model=new ScanSettingsSelection();SimulateComboBoxResets(model);var expected=Duplex();
        var actual=model.ApplyCapabilities(Caps(),expected);
        Assert.Equal(expected,actual);Assert.Equal(expected,model.Value);Assert.Empty(model.Warnings);
        Assert.Equal(ScanSource.Duplex,model.Source);Assert.Equal(600,model.Dpi);Assert.Equal(ScanColor.BlackAndWhite,model.Color);Assert.Equal("Letter",model.Paper);
    }
    [Fact]public void SelectionIsStableDuringEveryChangeNotification()
    {
        var model=new ScanSettingsSelection();var expected=Duplex();int notifications=0;
        model.PropertyChanged+=(_,_)=>{
            notifications++;Assert.Equal(expected,model.Value);
            model.Source=ScanSource.Flatbed;model.Dpi=0;model.Color=ScanColor.Color;model.Paper="";
        };
        model.ApplyCapabilities(Caps(),expected);Assert.True(notifications>0);Assert.Equal(expected,model.Value);
    }
    [Fact]public void RepeatedRefreshKeepsAllFourSelections()
    {
        var model=new ScanSettingsSelection();SimulateComboBoxResets(model);var requested=Duplex();
        for(int i=0;i<5;i++){model.ApplyCapabilities(Caps(),requested);Assert.Equal(requested,model.Value);}
    }
    [Fact]public void LoadingProfileForDifferentDeviceUsesNewDeviceCapabilities()
    {
        var model=new ScanSettingsSelection();SimulateComboBoxResets(model);
        model.ApplyCapabilities(new([new(ScanSource.Flatbed,[150],[ScanColor.Color],["A5"])],true),new(){Dpi=150,Paper="A5"});
        model.ApplyCapabilities(Caps(),Duplex());Assert.Equal(Duplex(),model.Value);
    }
    [Fact]public void SwitchingPaperSourcePreservesSupportedDpiAndColor()
    {
        var model=new ScanSettingsSelection();model.ApplyCapabilities(Caps(),new(){Source=ScanSource.Duplex,Dpi=200,Color=ScanColor.Grayscale});
        SimulateComboBoxResets(model);model.Source=ScanSource.Feeder;
        Assert.Equal(ScanSource.Feeder,model.Source);Assert.Equal(200,model.Dpi);Assert.Equal(ScanColor.Grayscale,model.Color);
    }
    [Fact]public void UnsupportedProfileValuesProduceWarningsWithoutModifyingSavedRecord()
    {
        var saved=Duplex();var model=new ScanSettingsSelection();
        var effective=model.ApplyCapabilities(new([new(ScanSource.Flatbed,[150],[ScanColor.Color],["A5"])],true),saved);
        Assert.Equal(ScanSource.Flatbed,effective.Source);Assert.Equal(150,effective.Dpi);Assert.Equal(ScanColor.Color,effective.Color);Assert.Equal("Auto",effective.Paper);
        Assert.Equal(4,model.Warnings.Count);Assert.Equal(ScanSource.Duplex,saved.Source);Assert.Equal(600,saved.Dpi);Assert.Equal(ScanColor.BlackAndWhite,saved.Color);
    }
    [Fact]public void MissingDetailedDpiCapabilitiesPreserveValidCustomResolution()
    {
        var model=new ScanSettingsSelection();var expected=new ScanSettings{Dpi=400,Color=ScanColor.Grayscale};
        Assert.Equal(expected,model.ApplyCapabilities(new([new(ScanSource.Flatbed,null,null,null)],true),expected));Assert.Contains(400,model.Dpis);
    }
    [Fact]public void EmptyCapabilitiesDoNotCorruptDpiOrInventDuplexSupport()
    {
        var model=new ScanSettingsSelection();SimulateComboBoxResets(model);
        var actual=model.ApplyCapabilities(new([],true),Duplex());
        Assert.Equal(600,actual.Dpi);Assert.Empty(model.Sources);Assert.NotEmpty(model.Warnings);
    }
    [Fact]public void UnknownCapabilitiesRequireNativeDialogAndDoNotAdvertiseDuplex()
    {
        var model=new ScanSettingsSelection();var actual=model.ApplyCapabilities(new([],false),Duplex());
        Assert.True(actual.NativeUi);Assert.DoesNotContain(ScanSource.Duplex,model.Sources);Assert.NotEmpty(model.Warnings);
    }
    [Fact]public void JsonSaveRestartLoadPreservesTwoIndependentCompleteProfiles()
    {
        string folder=Path.Combine(Path.GetTempPath(),"scanner-profiles-"+Guid.NewGuid().ToString("N"));
        try
        {
            var store=new SettingsStore(new AppPaths(folder));var device=new DeviceInfo("test","Test scanner","Wia","device-id");
            var first=Duplex();var second=new ScanSettings{Source=ScanSource.Feeder,Dpi=400,Color=ScanColor.Grayscale,Paper="Legal",NativeUi=true};
            store.Save(new(){Profiles=[new("Duplex BW 600",device,first),new("Feeder Gray 400",device,second)]});
            var restarted=new SettingsStore(new AppPaths(folder)).Load();Assert.Equal(2,restarted.Profiles.Count);
            var model=new ScanSettingsSelection();SimulateComboBoxResets(model);
            Assert.Equal(first,model.ApplyCapabilities(Caps(),restarted.Profiles[0].Settings));
            Assert.Equal(second,model.ApplyCapabilities(Caps(),restarted.Profiles[1].Settings));
            Assert.Equal(first,model.ApplyCapabilities(Caps(),restarted.Profiles[0].Settings));
            Assert.Equal(first,restarted.Profiles[0].Settings);
        }
        finally{if(Directory.Exists(folder))Directory.Delete(folder,true);}
    }
    [Fact]public void DeferredNullSelectionsCannotResetProfileAfterRefreshCompletes()
    {
        var model=new ScanSettingsSelection();model.ApplyCapabilities(Caps(),Duplex());
        int restoredSelections=0;model.PropertyChanged+=(_,e)=>{if(e.PropertyName?.StartsWith("Selected")==true)restoredSelections++;};
        model.SelectedSource=null;model.SelectedDpi=null;model.SelectedColor=null;model.SelectedPaper=null;
        Assert.Equal(4,restoredSelections);
        Assert.Equal(Duplex(),model.Value);
        Assert.Equal(ScanSource.Duplex,model.SelectedSource);Assert.Equal(600,model.SelectedDpi);Assert.Equal(ScanColor.BlackAndWhite,model.SelectedColor);
        // A real user selection of the enum-zero options must still work.
        model.SelectedSource=ScanSource.Flatbed;model.SelectedColor=ScanColor.Color;
        Assert.Equal(ScanSource.Flatbed,model.Source);Assert.Equal(ScanColor.Color,model.Color);
    }
    [Fact]public void SaveAfterUserChangesCapturesDisplayedSourceDpiAndColor()
    {
        var model=new ScanSettingsSelection();model.ApplyCapabilities(Caps(),new());
        model.Source=ScanSource.Feeder;model.Dpi=400;model.Color=ScanColor.BlackAndWhite;model.Paper="Legal";
        var saved=model.Value;model.ApplyCapabilities(Caps(),Duplex());model.ApplyCapabilities(Caps(),saved);
        Assert.Equal(ScanSource.Feeder,model.Source);Assert.Equal(400,model.Dpi);Assert.Equal(ScanColor.BlackAndWhite,model.Color);Assert.Equal("Legal",model.Paper);
    }
}
