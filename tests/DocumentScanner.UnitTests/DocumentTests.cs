using DocumentScanner.Core;
using DocumentScanner.Infrastructure;
using Xunit;
namespace DocumentScanner.UnitTests;
public sealed class DocumentTests
{
    private static PageData Page()=>new(){OriginalFile=$"{Guid.NewGuid():N}.png",ThumbnailFile="thumb.png"};
    [Theory]
    [InlineData("CON")][InlineData("NUL.pdf")][InlineData("COM1")][InlineData("LPT9")][InlineData("COM¹")][InlineData("../escape")]
    [InlineData("C:\\bad")][InlineData("trailing.")][InlineData("trailing ")][InlineData("a?b")][InlineData("")][InlineData("CONIN$")]
    public void WindowsReservedAndUnsafeNamesRejected(string name)=>Assert.ThrowsAny<ArgumentException>(()=>FilenameRules.Validate(name));
    [Theory][InlineData("เอกสารสแกน")][InlineData("SCAN-20261114-0001")][InlineData("COM10")]
    public void ValidNamesAccepted(string name)=>FilenameRules.Validate(name);
    [Fact]public void NamesFollowExpectedPattern()
    {
        Assert.Equal("SCAN-20261114-0001",FilenameRules.Automatic(new DateTime(2026,11,14),1));
        Assert.Equal("SCAN-001.jpg",FilenameRules.PageName("SCAN",1,ExportFormat.Jpg));
    }
    [Fact]public void DeleteAndUndoRestoreOrder()
    {
        var a=Page();var b=Page();var c=Page();var s=new DocumentSession();s.Append(a);s.Append(b);s.Append(c);
        s.Delete(new[]{b.Id});Assert.Equal(new[]{a.Id,c.Id},s.Pages.Select(x=>x.Id));
        s.Undo();Assert.Equal(new[]{a.Id,b.Id,c.Id},s.Pages.Select(x=>x.Id));
    }
    [Fact]public void UndoAfterScanMorePreservesNewlyAcquiredPages()
    {
        var a=Page();var b=Page();var s=new DocumentSession();s.Append(a);s.Rotate(new[]{a.Id},90);s.Append(b);s.Undo();
        Assert.Equal(2,s.Pages.Count);Assert.Equal(0,s.Pages[0].Rotation);Assert.Equal(b.Id,s.Pages[1].Id);
    }
    [Fact]public void RotateDoesNotChangeOriginalAndNormalizesNegativeAngles()
    {
        var p=Page();var s=new DocumentSession();s.Append(p);s.Rotate(new[]{p.Id},-90);
        Assert.Equal(270,s.Pages[0].Rotation);Assert.Equal(p.OriginalFile,s.Pages[0].OriginalFile);s.Undo();Assert.Equal(0,s.Pages[0].Rotation);
    }
    [Fact]public void MultiSelectionMovePreservesRelativeOrder()
    {
        var p=Enumerable.Range(0,5).Select(_=>Page()).ToArray();var s=new DocumentSession();foreach(var page in p)s.Append(page);
        s.Move(new[]{p[1].Id,p[3].Id},5);Assert.Equal(new[]{p[0].Id,p[2].Id,p[4].Id,p[1].Id,p[3].Id},s.Pages.Select(x=>x.Id));
        s.Undo();Assert.Equal(p.Select(x=>x.Id),s.Pages.Select(x=>x.Id));
    }
    [Fact]public void ClearIsUndoable(){var s=new DocumentSession();s.Append(Page());s.Clear();Assert.Empty(s.Pages);s.Undo();Assert.Single(s.Pages);}
    [Fact]public void DuplicatePageRejected(){var p=Page();var s=new DocumentSession();s.Append(p);Assert.Throws<ArgumentException>(()=>s.Append(p));}
    [Fact]public void CropIsUndoable(){var p=Page();var s=new DocumentSession();s.Append(p);s.SetCrop(new[]{p.Id},true);Assert.True(s.Pages[0].AutoCrop);s.Undo();Assert.False(s.Pages[0].AutoCrop);}
    [Fact]public void IllegalStateTransitionsRejected()
    {
        var s=new SessionStateMachine();Assert.Throws<InvalidOperationException>(()=>s.MoveTo(SessionState.Completed));
        s.MoveTo(SessionState.Initializing);s.MoveTo(SessionState.Scanning);s.MoveTo(SessionState.Processing);s.MoveTo(SessionState.Scanning);s.MoveTo(SessionState.Cancelled);
        s.MoveTo(SessionState.Initializing);s.MoveTo(SessionState.Error);Assert.Equal(SessionState.Error,s.State);
    }
    [Fact]public void NullConfigurationSectionsRejected()=>Assert.Throws<ArgumentException>(()=>(new AppSettings{ManualDevices=null!}).Validate());
    [Fact]public void SettingsRejectInvalidValues()
    {
        Assert.Throws<ArgumentException>(()=>(new AppSettings{JpegQuality=59}).Validate());
        Assert.Throws<ArgumentException>(()=>(new ScanSettings{Dpi=0}).Validate());
        Assert.Throws<ArgumentException>(()=>(new ScanSettings{Brightness=1001}).Validate());
        Assert.Throws<ArgumentException>(()=>(new ExportOptions("relative","scan",ExportFormat.Pdf)).Validate());
    }
}
public sealed class StorageTests : IDisposable
{
    private readonly string folder=Path.Combine(Path.GetTempPath(),"scanner-tests-"+Guid.NewGuid().ToString("N"));
    [Fact]public void CorruptedSettingsFallBackAndPreserveOriginal()
    {
        var paths=new AppPaths(folder);Directory.CreateDirectory(Path.GetDirectoryName(paths.Config)!);File.WriteAllText(paths.Config,"broken json");
        var store=new SettingsStore(paths);Assert.Equal(300,store.Load().Scan.Dpi);Assert.NotNull(store.LoadWarning);Assert.Equal("broken json",File.ReadAllText(paths.Config));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(paths.Config)!,"*.invalid-*"));
    }
    [Fact]public void ConfigurationRoundTripIncludesProfiles()
    {
        var store=new SettingsStore(new AppPaths(folder));var expected=new AppSettings{JpegQuality=95,Profiles=[new("Office",new("wia-id","Office","Wia","id"),new(){Dpi=150})]};
        store.Save(expected);var actual=store.Load();Assert.Equal(95,actual.JpegQuality);Assert.Equal(150,actual.Profiles[0].Settings.Dpi);
    }
    [Fact]public void AtomicFinalizationNeverOverwritesExistingFiles()
    {
        Directory.CreateDirectory(folder);string destination=Path.Combine(folder,"test.pdf"),temp=Path.Combine(folder,"work.tmp");File.WriteAllText(destination,"original");File.WriteAllText(temp,"new");
        var saved=SafeOutput.Finalize(temp,destination);Assert.Equal("original",File.ReadAllText(destination));Assert.Equal("new",File.ReadAllText(saved));Assert.EndsWith("test (1).pdf",saved);
    }
    [Fact]public void RecoveryIgnoresMissingPagesAndRejectsPathTraversal()
    {
        var store=new SessionStore(new AppPaths(folder));var session=new DocumentSession();session.Append(new(){OriginalFile="missing.png",ThumbnailFile="thumb.png"});store.Save(session);Assert.Empty(store.Recoverable());
        Assert.Throws<InvalidDataException>(()=>SessionStore.Resolve(folder,"../outside.png"));
    }
    [Fact]public void PersistedSessionRecoversPageOrderAndRotation()
    {
        var store=new SessionStore(new AppPaths(folder));var session=new DocumentSession();Directory.CreateDirectory(store.Folder(session.Id));
        foreach(var name in new[]{"b.png","a.png"}){File.WriteAllText(Path.Combine(store.Folder(session.Id),name),"data");session.Append(new(){OriginalFile=name,ThumbnailFile="thumb.png",Rotation=90});}
        store.Save(session);var recovered=Assert.Single(store.Recoverable());Assert.Equal(new[]{"b.png","a.png"},recovered.Pages.Select(p=>p.OriginalFile));Assert.All(recovered.Pages,p=>Assert.Equal(90,p.Rotation));
    }
    public void Dispose(){if(Directory.Exists(folder))Directory.Delete(folder,true);}
}
