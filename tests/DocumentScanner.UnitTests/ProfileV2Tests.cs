using System.Text.Json;
using DocumentScanner.Core;
using DocumentScanner.Infrastructure;
using Xunit;
namespace DocumentScanner.UnitTests;
public sealed class ProfileV2Tests
{
    [Fact]public void CompleteProfileJsonRoundTripsWithoutCredentialOrDestinationState()
    {
        var p=new ScanProfile("Duplex invoices",new("device","Scanner","Twain","id",true),new(){Source=ScanSource.Duplex,Dpi=600,Color=ScanColor.Grayscale,Paper="Legal",Landscape=true,Brightness=123,Contrast=-456,AutoCrop=true,Deskew=false,DetectBlank=true,RemoveBlank=true,NativeUi=true})
        {Output=new(){Format=ExportFormat.Jpg,JpegQuality=96,Folder=Path.GetTempPath(),Prefix="POD",Pattern="{prefix}-{date:yyyyMMdd}-{sequence:0000}"},Erp=new(){SiteId=Guid.NewGuid(),DocType="Delivery Note",Private=false}};
        var package=new ProfilePackage(2,[p]);package.Validate();string text=JsonSerializer.Serialize(package,JsonFiles.Options);var restored=JsonSerializer.Deserialize<ProfilePackage>(text,JsonFiles.Options)!;Assert.Equal(p,restored.Profiles[0]);
        Assert.DoesNotContain("Secret",text);Assert.DoesNotContain("Confirmed",text);Assert.DoesNotContain("DocumentNumber",text);
    }
    [Fact]public void UnsupportedAndUnknownCapabilitiesDoNotMutateProfile()
    {
        var saved=new ScanSettings{Source=ScanSource.Duplex,Dpi=600,Color=ScanColor.BlackAndWhite};
        var issues=ProfileValidation.CapabilityIssues(saved,new([new(ScanSource.Flatbed,[300],[ScanColor.Color],["A4"])],true));Assert.Contains(issues,x=>x.Contains("not supported"));Assert.Equal(600,saved.Dpi);Assert.False(ProfileValidation.OnlyUnknown(issues));
        Assert.True(ProfileValidation.OnlyUnknown(ProfileValidation.CapabilityIssues(saved,new([],false))));
    }
    [Theory][InlineData(3)][InlineData(0)]public void UnknownProfileSchemaIsRejected(int schema)
    {Assert.Throws<ArgumentException>(()=>ProfileValidation.Validate(new("P",new("d","Scanner","Wia","id"),new()){SchemaVersion=schema}));}
    [Fact]public void NamingAppliesPrefixOnceAndRejectsUnsupportedTokens()
    {var o=new OutputDefaults{Prefix="DN"};Assert.Equal("DN-20261114-0007",OutputNaming.Resolve(o,new(2026,11,14),7));Assert.Equal("20261114-0007",OutputNaming.Resolve(o with{Prefix=""},new(2026,11,14),7));Assert.Throws<ArgumentException>(()=>OutputNaming.Resolve(o with{Prefix="../bad"},DateTime.Today,1));Assert.Throws<ArgumentException>(()=>OutputNaming.Resolve(o with{Pattern="{secret}"},DateTime.Today,1));}
    [Fact]public void DestinationConfirmationRequiresExactSessionAndRevisionAndCanBeCleared()
    {var c=new DestinationConfirmation();var target=new ErpTarget(Guid.NewGuid(),Guid.NewGuid(),"u","Sales Order","SO-1",true,"rev1");c.Confirm(target);Assert.True(c.Matches(target));Assert.False(c.Matches(target with{SessionId=Guid.NewGuid()}));Assert.False(c.Matches(target with{DocType="Purchase Order"}));c.Invalidate();Assert.Null(c.Target);}
    [Fact]public void LegacySettingsMigrateWithBackupAndDetectionDoesNotBecomeRemoval()
    {
        string folder=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));try{var paths=new AppPaths(folder);Directory.CreateDirectory(Path.GetDirectoryName(paths.Config)!);File.WriteAllText(paths.Config,"{\"SchemaVersion\":1,\"OutputFolder\":"+JsonSerializer.Serialize(folder)+",\"Profiles\":[{\"Name\":\"Old\",\"Device\":{\"Key\":\"d\",\"Name\":\"Scanner\",\"Driver\":\"Wia\",\"Id\":\"id\"},\"Settings\":{\"Dpi\":600,\"Color\":\"Grayscale\",\"Source\":\"Feeder\",\"DetectBlank\":true}}]}");
        var store=new SettingsStore(paths);var value=store.Load();Assert.NotNull(store.LoadWarning);var profile=Assert.Single(value.Profiles);Assert.Equal(2,profile.SchemaVersion);Assert.Equal(600,profile.Settings.Dpi);Assert.Equal(ScanSource.Feeder,profile.Settings.Source);Assert.Equal(ScanColor.Grayscale,profile.Settings.Color);Assert.False(profile.Settings.RemoveBlank);Assert.Equal(folder,profile.Output.Folder);Assert.Single(Directory.GetFiles(Path.GetDirectoryName(paths.Config)!,"*.before-profile-v2-*"));}
        finally{Directory.Delete(folder,true);}
    }
    [Fact]public void SelectedPageToneEditsAreReversibleAndLeaveOtherPagesAlone()
    {var a=new PageData{OriginalFile="a",ThumbnailFile="a"};var b=new PageData{OriginalFile="b",ThumbnailFile="b"};var s=new DocumentSession();s.Append(a);s.Append(b);s.SetTone([a.Id],120,-50);Assert.Equal(120,s.Pages[0].Brightness);Assert.Equal(b,s.Pages[1]);s.Undo();Assert.Equal(a,s.Pages[0]);}
    [Fact]public void FutureSchemaPreservesOriginalAndBlocksSettingsWrites()
    {
        string root=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));try{var paths=new AppPaths(root);Directory.CreateDirectory(Path.GetDirectoryName(paths.Config)!);string original="{\"SchemaVersion\":99}";File.WriteAllText(paths.Config,original);var store=new SettingsStore(paths);store.Load();Assert.Throws<InvalidOperationException>(()=>store.Save(new()));Assert.Equal(original,File.ReadAllText(paths.Config));}finally{Directory.Delete(root,true);}
    }
    [Fact]public void ExplicitPreservationKeepsUnsupportedUiValuesUntilUserChoice()
    {
        var model=new ScanSettingsSelection();var requested=new ScanSettings{Dpi=600,Color=ScanColor.Grayscale};var caps=new ScannerCapabilities([new(ScanSource.Flatbed,[150,300],[ScanColor.Color],["A4"])],true);
        Assert.Equal(requested,model.ApplyCapabilities(caps,requested,true));Assert.Equal(600,model.SelectedDpi);Assert.Equal(ScanColor.Grayscale,model.SelectedColor);Assert.NotEmpty(ProfileValidation.CapabilityIssues(requested,caps));
    }
    [Fact]public void PrefixIsEnforcedForCustomPatternsAndCannotBeDuplicatedOrMoved()
    {
        var output=new OutputDefaults{Prefix="POD",Pattern="{date:yyyyMMdd}-{sequence:0000}"};Assert.Equal("POD-20261114-0001",OutputNaming.Resolve(output,new(2026,11,14),1));
        Assert.Throws<ArgumentException>(()=>OutputNaming.Resolve(output with{Pattern="{date:yyyyMMdd}-{prefix}"},DateTime.Today,1));
        Assert.Throws<ArgumentException>(()=>OutputNaming.Resolve(output with{Pattern="{prefix}-{prefix}"},DateTime.Today,1));
    }
}
