using DocumentScanner.Core;
using Xunit;
namespace DocumentScanner.UnitTests;
public sealed class ContentCropperTests
{
    private const int W=200,H=280;
    private static byte[] Background(byte value)=>Enumerable.Repeat(value,W*H).ToArray();
    private static void Rectangle(byte[] p,int x,int y,int w,int h,byte value)
    {for(int row=y;row<y+h;row++)for(int col=x;col<x+w;col++)p[row*W+col]=value;}
    [Theory][InlineData(255)][InlineData(232)][InlineData(200)]
    public void BlankWhiteAndGrayPagesPreserveOriginal(byte background)
    {
        var result=ContentCropper.Analyze(Background(background),W,H);
        Assert.True(result.Blank);Assert.Equal(new PixelBounds(0,0,W,H),result.Bounds);
    }
    [Theory][InlineData(255,40)][InlineData(232,150)][InlineData(200,80)]
    public void ContentBoundsAdaptToPaperBrightness(byte background,byte foreground)
    {
        var pixels=Background(background);Rectangle(pixels,40,60,100,120,foreground);
        var result=ContentCropper.Analyze(pixels,W,H);
        Assert.False(result.Blank);Assert.Equal(new PixelBounds(40,60,100,120),result.Bounds);
    }
    [Fact]public void IsolatedNoiseDoesNotForceFullImageBounds()
    {
        var pixels=Background(240);Rectangle(pixels,40,60,100,120,40);
        pixels[0]=0;pixels[W-1]=0;pixels[(H-1)*W]=0;pixels[^1]=0;
        Assert.Equal(new PixelBounds(40,60,100,120),ContentCropper.Analyze(pixels,W,H).Bounds);
    }
    [Fact]public void DocumentOnDarkPlatenUsesPaperBounds()
    {
        var pixels=Background(15);Rectangle(pixels,30,40,140,200,245);Rectangle(pixels,65,90,50,80,20);
        var result=ContentCropper.Analyze(pixels,W,H);
        Assert.True(result.PaperDetected);Assert.Equal(new PixelBounds(30,40,140,200),result.Bounds);
    }
    [Fact]public void ScannerEdgeStripDoesNotCountAsDocumentContent()
    {
        var pixels=Background(240);Rectangle(pixels,0,0,2,H,60);Rectangle(pixels,40,60,100,120,30);
        Assert.Equal(new PixelBounds(40,60,100,120),ContentCropper.Analyze(pixels,W,H).Bounds);
    }
    [Fact]public void FineBorderFrameIsIgnoredButContentIsRetained()
    {
        var pixels=Background(240);Rectangle(pixels,0,0,W,1,50);Rectangle(pixels,0,H-1,W,1,50);
        Rectangle(pixels,0,0,1,H,50);Rectangle(pixels,W-1,0,1,H,50);Rectangle(pixels,40,60,100,120,20);
        Assert.Equal(new PixelBounds(40,60,100,120),ContentCropper.Analyze(pixels,W,H).Bounds);
    }
    [Fact]public void SeparateTextBlocksAllRemainInsideBounds()
    {
        var pixels=Background(245);Rectangle(pixels,20,30,40,10,60);Rectangle(pixels,100,210,60,15,100);
        Assert.Equal(new PixelBounds(20,30,140,195),ContentCropper.Analyze(pixels,W,H).Bounds);
    }
    [Fact]public void SolidDarkImageIsNotDeletedOrCropped()
    {
        var result=ContentCropper.Analyze(Background(0),W,H);
        Assert.False(result.Blank);Assert.Equal(new PixelBounds(0,0,W,H),result.Bounds);
    }
    [Fact]public void CropIsIdempotentForMultiplePagesAndUndoRestoresOriginalFlags()
    {
        var session=new DocumentSession();var a=new PageData{OriginalFile="a.png",ThumbnailFile="a-thumb.png"};var b=new PageData{OriginalFile="b.png",ThumbnailFile="b-thumb.png"};session.Append(a);session.Append(b);
        session.SetCrop(new[]{a.Id,b.Id},true);session.SetCrop(new[]{a.Id,b.Id},true);
        Assert.All(session.Pages,p=>Assert.True(p.AutoCrop));session.Undo();Assert.All(session.Pages,p=>Assert.False(p.AutoCrop));
        session.SetCrop(new[]{a.Id,b.Id},true);session.SetCrop(new[]{a.Id,b.Id},false);Assert.All(session.Pages,p=>Assert.False(p.AutoCrop));session.Undo();Assert.All(session.Pages,p=>Assert.True(p.AutoCrop));
    }
    [Fact]public void BlankPageWithSmallNoiseClusterKeepsFullBounds()
    {
        var pixels=Background(245);Rectangle(pixels,15,20,3,3,50);
        var result=ContentCropper.Analyze(pixels,W,H);
        Assert.True(result.Blank);Assert.Equal(new PixelBounds(0,0,W,H),result.Bounds);
    }
    [Fact]public void ContentNearPageEdgeIsRetained()
    {
        var pixels=Background(245);Rectangle(pixels,0,40,40,70,40);
        Assert.Equal(new PixelBounds(0,40,40,70),ContentCropper.Analyze(pixels,W,H).Bounds);
    }
    [Fact]public void SparseSmallTextStillCrops()
    {
        var pixels=Background(245);Rectangle(pixels,40,60,5,10,30);
        var result=ContentCropper.Analyze(pixels,W,H);
        Assert.False(result.Blank);Assert.Equal(new PixelBounds(40,60,5,10),result.Bounds);
    }
    [Fact]public void InvalidSampleDimensionsAreRejected()=>Assert.Throws<ArgumentException>(()=>ContentCropper.Analyze(new byte[1],W,H));
}
