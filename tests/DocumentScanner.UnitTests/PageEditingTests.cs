using DocumentScanner.Core;
using DocumentScanner.Infrastructure;
using Xunit;
namespace DocumentScanner.UnitTests;
public sealed class PageEditingTests
{
    [Theory][InlineData(0,.1,.2,.3,.4)][InlineData(90,.2,.6,.4,.3)][InlineData(180,.6,.4,.3,.4)][InlineData(270,.4,.1,.4,.3)]
    public void CropCoordinatesMapFromOrientedPreview(int angle,double x,double y,double w,double h)
    {
        var c=NormalizedCrop.FromView(new(.1,.2,.3,.4),angle);
        Assert.Equal(x,c.X,8);Assert.Equal(y,c.Y,8);Assert.Equal(w,c.Width,8);Assert.Equal(h,c.Height,8);
    }
    [Fact]public void InvalidCropIsRejected()
    {
        Assert.Throws<ArgumentException>(()=>new NormalizedCrop(double.NaN,0,.5,.5).Validate());
        Assert.Throws<ArgumentException>(()=>new NormalizedCrop(0,0,0,.5).Validate());
        Assert.Throws<ArgumentException>(()=>new NormalizedCrop(.9,0,.2,.5).Validate());
    }
    [Fact]public void CropAndColorEditsPersistAndUndoWithoutChangingOriginal()
    {
        var session=new DocumentSession();var page=new PageData{OriginalFile="original.png",ThumbnailFile="thumb.png",AutoCrop=true};session.Append(page);
        session.SetManualCrop(page.Id,new(.1,.2,.3,.4));session.SetAutoColor(new[]{page.Id},true);session.SetAutoColor(new[]{page.Id},true);
        var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json");
        try{JsonFiles.Write(path,session.Pages[0]);var restored=JsonFiles.Read<PageData>(path);Assert.Equal(new(.1,.2,.3,.4),restored.ManualCrop);Assert.True(restored.AutoColorCorrection);Assert.False(restored.AutoCrop);Assert.Equal(page.OriginalFile,restored.OriginalFile);}
        finally{File.Delete(path);}
        session.Undo();Assert.False(session.Pages[0].AutoColorCorrection);Assert.NotNull(session.Pages[0].ManualCrop);
        session.SetCrop(new[]{page.Id},false);Assert.Null(session.Pages[0].ManualCrop);session.Undo();Assert.NotNull(session.Pages[0].ManualCrop);
        session.Undo();Assert.Equal(page,session.Pages[0]);
    }
    [Fact]public void ColorBalancesTintedPaperRetainsInkAndAlpha()
    {
        byte[] pixels=new byte[400];for(int i=0;i<100;i++){pixels[i*4]=190;pixels[i*4+1]=210;pixels[i*4+2]=230;pixels[i*4+3]=255;}
        pixels[0]=pixels[1]=pixels[2]=20;pixels[3]=128;
        DocumentColorCorrection.Apply(pixels);Assert.Equal(new byte[]{0,0,0,128},pixels[..4]);Assert.Equal(new byte[]{255,255,255,255},pixels[4..8]);
    }
    [Fact]public void DarkPagesAndTransparentPixelsArePreserved()
    {
        byte[] pixels={30,40,50,255,190,220,230,0};var expected=pixels.ToArray();DocumentColorCorrection.Apply(pixels);Assert.Equal(expected,pixels);
        DocumentColorCorrection.Apply(Array.Empty<byte>());
    }
}
