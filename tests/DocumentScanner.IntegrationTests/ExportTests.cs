using System.IO;
using DocumentScanner.Core;
using DocumentScanner.Export;
using DocumentScanner.ImagingSupport;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Xunit;
namespace DocumentScanner.IntegrationTests;
public sealed class ExportTests
{
    internal static Task OnSta(Func<Task> action)
    {
        var tcs=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread=new Thread(()=>{
            var dispatcher=Dispatcher.CurrentDispatcher;SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(new Action(async()=>{try{await action();tcs.SetResult();}catch(Exception ex){tcs.SetException(ex);}finally{dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);}}));Dispatcher.Run();
        });thread.SetApartmentState(ApartmentState.STA);thread.Start();return tcs.Task;
    }
    private static PageData CreatePage(string folder,int w,int h,Color color,string name)
    {
        var visual=new DrawingVisual();using(var dc=visual.RenderOpen())dc.DrawRectangle(new SolidColorBrush(color),null,new Rect(0,0,w*96.0/300,h*96.0/300));
        var bitmap=new RenderTargetBitmap(w,h,300,300,PixelFormats.Pbgra32);bitmap.Render(visual);bitmap.Freeze();
        Imaging.SavePng(bitmap,Path.Combine(folder,name));return new(){OriginalFile=name,ThumbnailFile=name,Width=w,Height=h,DpiX=300,DpiY=300};
    }
    [Theory][InlineData(ExportFormat.Png)][InlineData(ExportFormat.Jpg)]
    public Task ImageExportRespectsRotationAndFileCount(ExportFormat format)=>OnSta(async()=>{
        var folder=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try{
            var p=CreatePage(folder,200,300,Colors.Red,"input.png") with{Rotation=90};
            var result=await new DocumentExporter().ExportAsync(new[]{p,p},folder,new(folder,"test",format),null,default);
            Assert.Null(result.Error);Assert.Equal(2,result.Saved.Count);var image=Imaging.Load(result.Saved[0]);Assert.Equal(300,image.PixelWidth);Assert.Equal(200,image.PixelHeight);
        }finally{Directory.Delete(folder,true);}
    });
    [Fact]public Task PdfPreservesPageOrderDimensionsAndOrientation()=>OnSta(async()=>{
        var folder=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try{
            var a=CreatePage(folder,200,300,Colors.Red,"a.png");var b=CreatePage(folder,400,200,Colors.Blue,"b.png");
            var result=await new DocumentExporter().ExportAsync(new[]{b,a},folder,new(folder,"ordered",ExportFormat.Pdf),null,default);
            Assert.Null(result.Error);using var pdf=UglyToad.PdfPig.PdfDocument.Open(Assert.Single(result.Saved));
            Assert.Equal(2,pdf.NumberOfPages);Assert.True(pdf.GetPage(1).Width>pdf.GetPage(1).Height);Assert.True(pdf.GetPage(2).Height>pdf.GetPage(2).Width);
            Assert.InRange(pdf.GetPage(1).Width,95.0,97.0); // 400 px / 300 dpi * 72 points
        }finally{Directory.Delete(folder,true);}
    });
    [Fact]public Task A4FitAndBlankReviewAreNonDestructive()=>OnSta(async()=>{
        var folder=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try{
            var page=CreatePage(folder,200,300,Colors.White,"white.png");Assert.True(Imaging.Analyze(Imaging.Load(Path.Combine(folder,"white.png"))).Blank);
            var original=File.ReadAllBytes(Path.Combine(folder,"white.png"));
            var result=await new DocumentExporter().ExportAsync(new[]{page},folder,new(folder,"a4",ExportFormat.Pdf,Paper:OutputPaper.A4),null,default);
            Assert.Null(result.Error);using var pdf=UglyToad.PdfPig.PdfDocument.Open(Assert.Single(result.Saved));Assert.InRange(pdf.GetPage(1).Width,594.0,596.0);
            Assert.Equal(original,File.ReadAllBytes(Path.Combine(folder,"white.png")));
        }finally{Directory.Delete(folder,true);}
    });
    [Fact]public Task CancellationDoesNotFinalizePdf()=>OnSta(async()=>{
        var folder=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try{
            var page=CreatePage(folder,200,300,Colors.Blue,"input.png");using var cts=new CancellationTokenSource();cts.Cancel();
            var result=await new DocumentExporter().ExportAsync(new[]{page},folder,new(folder,"cancel",ExportFormat.Pdf),null,cts.Token);
            Assert.True(result.Cancelled);Assert.Empty(result.Saved);Assert.False(File.Exists(Path.Combine(folder,"cancel.pdf")));
        }finally{Directory.Delete(folder,true);}
    });
    [Theory][InlineData(ExportFormat.Png)][InlineData(ExportFormat.Jpg)][InlineData(ExportFormat.Pdf)]
    public Task CropPreviewAndExportUseSameBoundsAndPreserveOriginal(ExportFormat format)=>OnSta(async()=>{
        var folder=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try{
            const int width=800,height=1100;byte[] pixels=Enumerable.Repeat((byte)232,width*height*3).ToArray();
            for(int y=250;y<850;y++)for(int x=200;x<600;x++)for(int c=0;c<3;c++)pixels[(y*width+x)*3+c]=35;
            pixels[0]=pixels[1]=pixels[2]=0; // Isolated scanner noise must not pin bounds to the original corner.
            var bitmap=BitmapSource.Create(width,height,100,100,PixelFormats.Bgr24,null,pixels,width*3);bitmap.Freeze();
            string original=Path.Combine(folder,"original.png");Imaging.SavePng(bitmap,original);byte[] bytes=File.ReadAllBytes(original);
            var page=new PageData{OriginalFile="original.png",ThumbnailFile="unused.png",Width=width,Height=height,DpiX=100,DpiY=100,AutoCrop=true};
            var preview=Imaging.RenderDetails(page,folder,1600);
            Assert.True(preview.CropChanged);Assert.Equal(424,preview.Width);Assert.Equal(624,preview.Height);
            var thumbnail=Imaging.Render(page,folder,180);Assert.Equal(180,thumbnail.PixelWidth);Assert.IsNotType<TransformedBitmap>(thumbnail);
            var result=await new DocumentExporter().ExportAsync(new[]{page},folder,new(folder,"crop",format),null,default);
            Assert.Null(result.Error);string output=Assert.Single(result.Saved);
            if(format==ExportFormat.Pdf){using var pdf=UglyToad.PdfPig.PdfDocument.Open(output);Assert.InRange(pdf.GetPage(1).Width,304.0,306.0);}
            else{var exported=Imaging.Load(output);Assert.Equal(preview.Width,exported.PixelWidth);Assert.Equal(preview.Height,exported.PixelHeight);}
            Assert.Equal(bytes,File.ReadAllBytes(original));
            var reset=Imaging.RenderDetails(page with{AutoCrop=false},folder,1600);Assert.False(reset.CropChanged);Assert.Equal(width,reset.Width);Assert.Equal(height,reset.Height);
        }finally{Directory.Delete(folder,true);}
    });

}
