using System.ComponentModel;
using DocumentScanner.Core;
using DocumentScanner.Infrastructure;
using Xunit;
namespace DocumentScanner.UnitTests;
public sealed class ImageToneTests
{
    [Fact]public void NeutralToneIsIdentityForEveryPossibleChannelValue()
    {Assert.Equal(Enumerable.Range(0,256).Select(i=>(byte)i),ImageTone.Lookup(0,0));}
    [Theory][InlineData(1000,255)][InlineData(-1000,0)]
    public void BrightnessEndpointsAreClamped(int brightness,byte expected)=>Assert.All(ImageTone.Lookup(brightness,0),v=>Assert.Equal(expected,v));
    [Fact]public void BrightnessUsesTheDisplayedIntegerValue()
    {
        var plus=ImageTone.Lookup(200,0);Assert.Equal(51,plus[0]);Assert.Equal(179,plus[128]);Assert.Equal(255,plus[255]);
        var minus=ImageTone.Lookup(-200,0);Assert.Equal(0,minus[0]);Assert.Equal(77,minus[128]);Assert.Equal(204,minus[255]);
    }
    [Fact]public void ContrastIncreasesOrDecreasesChannelSeparation()
    {
        var higher=ImageTone.Lookup(0,500);var lower=ImageTone.Lookup(0,-500);
        Assert.True(higher[64]<64);Assert.True(higher[192]>192);Assert.True(lower[64]>64);Assert.True(lower[192]<192);
    }
    [Theory][InlineData(-1000,-1000)][InlineData(0,0)][InlineData(1000,1000)][InlineData(-150,250)]
    public void ToneLookupIsMonotonicAndStateless(int brightness,int contrast)
    {
        var first=ImageTone.Lookup(brightness,contrast);for(int i=1;i<256;i++)Assert.True(first[i]>=first[i-1]);
        Assert.Equal(first,ImageTone.Lookup(brightness,contrast));
    }
    [Fact]public void ControlsNotifyExactIntegerChangesAndResetBothValues()
    {
        var value=new ImageAdjustments();var names=new List<string?>();value.PropertyChanged+=(_,e)=>names.Add(e.PropertyName);
        value.Brightness=137;value.Contrast=-246;Assert.Equal(137,value.Brightness);Assert.Equal(-246,value.Contrast);
        value.Reset();Assert.Equal(0,value.Brightness);Assert.Equal(0,value.Contrast);
        Assert.Equal(new[]{"Brightness","Contrast","Brightness","Contrast"},names);
    }
    [Fact]public void OutOfRangeUiValuesCannotReachTheScanRequest()
    {var value=new ImageAdjustments{Brightness=-1500,Contrast=1500};Assert.Equal(-1000,value.Brightness);Assert.Equal(1000,value.Contrast);}
    [Fact]public void InvalidProgrammaticToneValuesAreRejected()=>Assert.Throws<ArgumentOutOfRangeException>(()=>ImageTone.Lookup(1001,0));
    [Fact]public void OldPageMetadataDefaultsToNeutralAndCropEditsKeepItsTone()
    {
        var old=System.Text.Json.JsonSerializer.Deserialize<PageData>("{\"OriginalFile\":\"old.png\",\"ThumbnailFile\":\"thumb.png\"}",JsonFiles.Options)!;
        Assert.Equal(0,old.Brightness);Assert.Equal(0,old.Contrast);
        var page=old with{Brightness=137,Contrast=-246};var session=new DocumentSession();session.Append(page);
        session.SetCrop(new[]{page.Id},true);session.SetCrop(new[]{page.Id},false);
        Assert.Equal(137,session.Pages[0].Brightness);Assert.Equal(-246,session.Pages[0].Contrast);
        session.Undo();Assert.Equal(137,session.Pages[0].Brightness);Assert.Equal(-246,session.Pages[0].Contrast);
    }
}
