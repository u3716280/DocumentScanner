using System.IO;
using DocumentScanner.Core;
using DocumentScanner.Export;
using DocumentScanner.ImagingSupport;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;
namespace DocumentScanner.IntegrationTests;
public sealed class PageEditingTests
{
    [Theory][InlineData(0,80,50)][InlineData(90,50,80)][InlineData(180,80,50)][InlineData(270,50,80)]
    public Task ManualCropUsesOriginalPixelsAndSurvivesRotationAndPngExport(int angle,int width,int height)=>ExportTests.OnSta(async()=>{
        var folder=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try{
            byte[] pixels=new byte[200*100*4];for(int i=0;i<pixels.Length;i+=4){pixels[i]=(byte)(i/4%200);pixels[i+1]=100;pixels[i+2]=200;pixels[i+3]=255;}
            var image=BitmapSource.Create(200,100,96,96,PixelFormats.Bgra32,null,pixels,800);Imaging.SavePng(image,Path.Combine(folder,"original.png"));var bytes=File.ReadAllBytes(Path.Combine(folder,"original.png"));
            var page=new PageData{OriginalFile="original.png",ThumbnailFile="unused",ManualCrop=new(.1,.2,.4,.5),Rotation=angle,AutoCrop=true};
            var expected=(BitmapSource)new CroppedBitmap(Imaging.Load(Path.Combine(folder,"original.png")),new Int32Rect(20,20,80,50));if(angle!=0)expected=new TransformedBitmap(expected,new RotateTransform(angle));
            for(int n=0;n<2;n++){
                var rendered=Imaging.Render(page,folder);Assert.Equal(width,rendered.PixelWidth);Assert.Equal(height,rendered.PixelHeight);AssertPixels(expected,rendered);
                var result=await new DocumentExporter().ExportAsync(new[]{page},folder,new(folder,"crop-"+n,ExportFormat.Png),null,default);Assert.Null(result.Error);AssertPixels(expected,Imaging.Load(Assert.Single(result.Saved)));
            }
            Assert.Equal(bytes,File.ReadAllBytes(Path.Combine(folder,"original.png")));
        }finally{Directory.Delete(folder,true);}
    });
    [Fact]public Task AutoColorPreviewAndExportsAreIdenticalAndResetRestoresOriginal()=>ExportTests.OnSta(async()=>{
        var folder=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try{
            byte[] pixels=new byte[100*100*4];for(int i=0;i<pixels.Length;i+=4){pixels[i]=190;pixels[i+1]=210;pixels[i+2]=230;pixels[i+3]=255;}pixels[0]=pixels[1]=pixels[2]=20;
            Imaging.SavePng(BitmapSource.Create(100,100,96,96,PixelFormats.Bgra32,null,pixels,400),Path.Combine(folder,"original.png"));
            var page=new PageData{OriginalFile="original.png",ThumbnailFile="unused",AutoColorCorrection=true};var preview=Imaging.Render(page,folder);
            for(int n=0;n<3;n++){
                AssertPixels(preview,Imaging.Render(page,folder));var result=await new DocumentExporter().ExportAsync(new[]{page},folder,new(folder,"color-"+n,ExportFormat.Png),null,default);Assert.Null(result.Error);AssertPixels(preview,Imaging.Load(Assert.Single(result.Saved)));
            }
            AssertPixels(Imaging.Load(Path.Combine(folder,"original.png")),Imaging.Render(page with{AutoColorCorrection=false},folder));
        }finally{Directory.Delete(folder,true);}
    });
    private static void AssertPixels(BitmapSource expected,BitmapSource actual)
    {
        int stride=expected.PixelWidth*4;var a=new byte[stride*expected.PixelHeight];var b=new byte[a.Length];
        new FormatConvertedBitmap(expected,PixelFormats.Bgra32,null,0).CopyPixels(a,stride,0);new FormatConvertedBitmap(actual,PixelFormats.Bgra32,null,0).CopyPixels(b,stride,0);Assert.Equal(a,b);
    }
}
