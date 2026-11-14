using System.IO;
using DocumentScanner.Core;
using DocumentScanner.Export;
using DocumentScanner.ImagingSupport;
using DocumentScanner.Scanning;
using NAPS2.Scan;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Xunit;
namespace DocumentScanner.IntegrationTests;
public sealed class ToneRegressionTests
{
    private static BitmapSource Input()
    {
        const int w=400,h=500;byte[] bgr=Enumerable.Repeat((byte)245,w*h*3).ToArray();
        for(int y=120;y<380;y++)for(int x=100;x<300;x++){int i=(y*w+x)*3;bgr[i]=140;bgr[i+1]=100;bgr[i+2]=60;}
        var bitmap=BitmapSource.Create(w,h,100,100,PixelFormats.Bgr24,null,bgr,w*3);bitmap.Freeze();return bitmap;
    }
    private static byte[] Pixel(BitmapSource source,int x,int y)
    {var rgba=new FormatConvertedBitmap(source,PixelFormats.Bgra32,null,0);var result=new byte[4];rgba.CopyPixels(new Int32Rect(x,y,1,1),result,4,0);return result;}
    [Fact]public Task NeutralCropAndRepeatedExportsPreserveColorBytes()=>ExportTests.OnSta(async()=>{
        string folder=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try{
            string file=Path.Combine(folder,"original.png");Imaging.SavePng(Input(),file);var originalBytes=File.ReadAllBytes(file);
            var page=new PageData{OriginalFile="original.png",ThumbnailFile="unused.png",Width=400,Height=500,DpiX=100,DpiY=100};
            var original=Imaging.Load(file);Assert.Same(original,Imaging.ApplyTone(original,0,0));
            var bounds=Imaging.Analyze(original).Bounds;
            for(int iteration=0;iteration<4;iteration++)
            {
                bool crop=iteration%2==1;var rendered=Imaging.Render(page with{AutoCrop=crop},folder);
                int ox=crop?bounds.X:0,oy=crop?bounds.Y:0;
                // Compare every retained pixel, including gray background, not only the page dimensions.
                var source=new CroppedBitmap(original,new Int32Rect(ox,oy,rendered.PixelWidth,rendered.PixelHeight));
                int stride=rendered.PixelWidth*4;var expected=new byte[stride*rendered.PixelHeight];var actual=new byte[expected.Length];
                new FormatConvertedBitmap(source,PixelFormats.Bgra32,null,0).CopyPixels(expected,stride,0);
                new FormatConvertedBitmap(rendered,PixelFormats.Bgra32,null,0).CopyPixels(actual,stride,0);Assert.Equal(expected,actual);
                var result=await new DocumentExporter().ExportAsync(new[]{page with{AutoCrop=crop}},folder,new(folder,"repeat-"+iteration,ExportFormat.Png),null,default);
                Assert.Null(result.Error);var saved=Imaging.Load(Assert.Single(result.Saved));Assert.Equal(Pixel(original,200,200),Pixel(saved,200-ox,200-oy));
            }
            Assert.Equal(originalBytes,File.ReadAllBytes(file));
        }finally{Directory.Delete(folder,true);}
    });
    [Fact]public Task NonzeroToneAppliesOnceFromOriginalAcrossCropToggleAndExport()=>ExportTests.OnSta(async()=>{
        string folder=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try{
            string file=Path.Combine(folder,"original.png");Imaging.SavePng(Input(),file);var originalBytes=File.ReadAllBytes(file);var bounds=Imaging.Analyze(Imaging.Load(file)).Bounds;
            var page=new PageData{OriginalFile="original.png",ThumbnailFile="unused.png",Width=400,Height=500,DpiX=100,DpiY=100,Brightness=200};
            for(int i=0;i<5;i++)
            {
                bool crop=i%2==1;var current=page with{AutoCrop=crop};var rendered=Imaging.Render(current,folder);
                var expected=new byte[]{191,151,111,255}; // Original BGR 140/100/60 + 51, exactly once.
                Assert.Equal(expected,Pixel(rendered,200-(crop?bounds.X:0),200-(crop?bounds.Y:0)));
                var result=await new DocumentExporter().ExportAsync(new[]{current},folder,new(folder,"adjusted-"+i,ExportFormat.Png),null,default);
                Assert.Null(result.Error);Assert.Equal(expected,Pixel(Imaging.Load(Assert.Single(result.Saved)),200-(crop?bounds.X:0),200-(crop?bounds.Y:0)));
            }
            Assert.Equal(originalBytes,File.ReadAllBytes(file));
        }finally{Directory.Delete(folder,true);}
    });
    [Theory][InlineData("Wia")][InlineData("Twain")][InlineData("Escl")]
    public void EachScanResetsDriverToneAndDoesNotSendCropIntoAcquisition(string driver)
    {
        var device=new DeviceInfo("fake","Fake",driver,"test-only");var settings=new ScanSettings{Brightness=-350,Contrast=600,AutoCrop=true};
        var first=ScanOptionsFactory.Create(device,settings);first.Brightness=-999;first.Contrast=999;
        var second=ScanOptionsFactory.Create(device,settings);var uncropped=ScanOptionsFactory.Create(device,settings with{AutoCrop=false});
        Assert.Equal(0,second.Brightness);Assert.Equal(0,second.Contrast);Assert.False(second.BrightnessContrastAfterScan);
        Assert.Equal(uncropped.Brightness,second.Brightness);Assert.Equal(uncropped.Contrast,second.Contrast);
        Assert.Equal(uncropped.PageSize,second.PageSize);Assert.False(second.CropToPageSize);Assert.False(second.StretchToPageSize);
    }
    [Fact]public Task IntegerSliderAndLiveValueStayInSyncAcrossLoadAndReset()=>ExportTests.OnSta(async()=>{
        var model=new ImageAdjustments{Brightness=137,Contrast=-246};var slider=new Slider{Minimum=-1000,Maximum=1000};var value=new TextBox{IsReadOnly=true};
        slider.SetBinding(Slider.ValueProperty,new Binding(nameof(model.Brightness)){Source=model,Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged});
        value.SetBinding(TextBox.TextProperty,new Binding(nameof(model.Brightness)){Source=model});
        await Dispatcher.Yield(DispatcherPriority.ContextIdle);Assert.Equal(137,slider.Value);Assert.Equal("137",value.Text);
        slider.Value=320;await Dispatcher.Yield(DispatcherPriority.ContextIdle);Assert.Equal(320,model.Brightness);Assert.Equal("320",value.Text);
        model.Brightness=-420;await Dispatcher.Yield(DispatcherPriority.ContextIdle);Assert.Equal(-420,slider.Value);Assert.Equal("-420",value.Text);
        model.Reset();await Dispatcher.Yield(DispatcherPriority.ContextIdle);Assert.Equal(0,slider.Value);Assert.Equal("0",value.Text);Assert.Equal(0,model.Contrast);
    });
}
