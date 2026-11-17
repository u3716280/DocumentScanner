using System.IO;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DocumentScanner.App;
using DocumentScanner.Core;
using DocumentScanner.Export;
using DocumentScanner.Infrastructure;
using DocumentScanner.ImagingSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
namespace DocumentScanner.IntegrationTests;
public sealed class OfficeWorkspaceTests
{
    private static readonly DeviceInfo Device=new("Wia|False|sample","Office Scanner","Wia","sample");
    private sealed class Scanner : IScannerService
    {
        public int Discoveries,Lookups,Capabilities;
        public Task<(IReadOnlyList<DeviceInfo>,IReadOnlyList<string>)> DiscoverAsync(CancellationToken ct){Discoveries++;return Task.FromResult(((IReadOnlyList<DeviceInfo>)new[]{Device},(IReadOnlyList<string>)Array.Empty<string>()));}
        public Task<DeviceInfo?> FindDeviceAsync(DeviceInfo saved,CancellationToken ct){Lookups++;return Task.FromResult<DeviceInfo?>(saved);}
        public Task<ScannerCapabilities> GetCapabilitiesAsync(DeviceInfo device,CancellationToken ct){Capabilities++;return Task.FromResult(new ScannerCapabilities([new(ScanSource.Flatbed,[150,300,600],[ScanColor.Color,ScanColor.Grayscale],["A4","Letter"])],true));}
        public Task<DeviceInfo> ConnectEndpointAsync(string endpoint,CancellationToken ct)=>throw new NotSupportedException();
        public Task ScanAsync(DeviceInfo device,ScanSettings settings,string folder,Func<PageData,Task> acquired,nint handle,CancellationToken ct)=>throw new NotSupportedException();
        public void Dispose(){}
    }
    private static PageData SamplePage(string folder,int number)
    {
        const int width=700,height=960;var visual=new DrawingVisual();
        using(var dc=visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White,null,new Rect(0,0,width,height));
            void Text(string text,int size,double x,double y,Brush? ink=null)=>dc.DrawText(new FormattedText(text,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),size,ink??new SolidColorBrush(Color.FromRgb(23,43,77)),1),new Point(x,y));
            Text("NORTHLINE SUPPLY",28,45,50);Text("SAMPLE DOCUMENT",12,45,88,Brushes.SlateGray);Text(number==3?"DELIVERY NOTE":"INVOICE",32,45,146);Text($"INV-2026-004{number}",17,45,194);
            Text("Bill to",13,45,258,Brushes.SlateGray);Text("Example Manufacturing Co.",19,45,282);Text("Date: 15 Nov 2026",14,430,280);
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(234,242,255)),null,new Rect(45,360,610,40));Text("Description",15,60,370);Text("Qty",15,448,370);Text("Amount",15,545,370);
            string[] descriptions=["Copy paper A4","Toner cartridge","Binder clips"];for(int i=0;i<3;i++){Text(descriptions[i],16,60,425+i*65);Text((i+1).ToString(),16,455,425+i*65);Text($"{(i+1)*55:0.00}",16,553,425+i*65);dc.DrawLine(new Pen(Brushes.LightGray,1),new Point(45,465+i*65),new Point(655,465+i*65));}
            Text("Total",20,430,680);Text("330.00",22,548,680);Text("Thank you for your business.",15,45,820);Text("Fictional data for interface verification",12,45,900,Brushes.SlateGray);
        }
        var image=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);image.Render(visual);image.Freeze();string file=$"sample-{number}.png";Imaging.SavePng(image,Path.Combine(folder,file));
        return new(){OriginalFile=file,ThumbnailFile=file,Width=width,Height=height,DpiX=96,DpiY=96};
    }
    private static void Layout(MainWindow window,double width,double height)
    {
        window.Width=width;window.Height=height;var content=(FrameworkElement)window.Content;content.Measure(new Size(width,height));content.Arrange(new Rect(0,0,width,height));content.UpdateLayout();content.UpdateLayout();
        foreach(string name in new[]{"ScanButton","SaveLocalButton","SendErpButton","SaveAndSendButton"})
        {
            var button=(Button)window.FindName(name);Assert.True(button.ActualWidth>30);Assert.True(button.ActualHeight>20);
            Rect bounds=button.TransformToAncestor(content).TransformBounds(new Rect(button.RenderSize));Assert.InRange(bounds.Left,-1,width);Assert.InRange(bounds.Right,0,width+1);Assert.InRange(bounds.Top,-1,height);Assert.InRange(bounds.Bottom,0,height+1);
        }
    }
    private static void Capture(MainWindow window,string name,double scale=1)
    {
        string? root=Environment.GetEnvironmentVariable("DOCUMENTSCANNER_UI_PREVIEW_DIR");if(string.IsNullOrWhiteSpace(root))return;Directory.CreateDirectory(root);
        var content=(FrameworkElement)window.Content;var image=new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth*scale),(int)Math.Ceiling(content.ActualHeight*scale),96*scale,96*scale,PixelFormats.Pbgra32);image.Render(content);
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));using var stream=File.Create(Path.Combine(root,name+".png"));png.Save(stream);
    }
    private static async Task WaitPreview(MainViewModel vm)
    {
        var end=DateTime.UtcNow.AddSeconds(15);while(vm.Preview==null&&DateTime.UtcNow<end)await Dispatcher.Yield(DispatcherPriority.ContextIdle);Assert.NotNull(vm.Preview);await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    }
    [Fact]public Task WorkspaceSeparatesScanAndEditsAndRendersAtSupportedSizes()=>ExportTests.OnSta(async()=>
    {
        string root=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));
        try
        {
            var paths=new AppPaths(root);var settings=new SettingsStore(paths);var profile=new ScanProfile("Invoices · 300 DPI",Device,new());settings.Save(new(){Profiles=[profile],DefaultProfileId=profile.Id});using var scanner=new Scanner();var store=new SessionStore(paths);using var vm=new MainViewModel(scanner,new DocumentExporter(),settings,store,NullLogger<MainViewModel>.Instance);
            var window=new MainWindow(vm,paths);await vm.InitializeAsync();Assert.Equal(0,scanner.Discoveries);Assert.Equal(1,scanner.Lookups);Assert.Equal(1,scanner.Capabilities);Assert.False(vm.HasDestination);Assert.Equal(SaveActionRules.DirectLaunchMessage,vm.DirectLaunchHint);Assert.Equal("Scan",vm.ScanButtonText);Assert.False(vm.HasPendingDelivery);
            Layout(window,1440,900);Capture(window,"01-empty-1440");
            var session=(DocumentSession)typeof(MainViewModel).GetField("session",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(vm)!;string folder=store.Folder(session.Id);Directory.CreateDirectory(folder);
            foreach(int number in new[]{1,2,3}){var page=SamplePage(folder,number);session.Append(page);vm.Pages.Add(new(page,number,folder));}
            var list=(ListBox)window.FindName("PageList");list.SelectedItems.Add(vm.Pages[1]);await WaitPreview(vm);Assert.Equal("1 page selected",vm.SelectionSummary);Assert.Equal("Page 2 of 3",vm.PreviewTitle);Assert.Equal("Scan more",vm.ScanButtonText);Assert.True(vm.ScannerReady);Assert.True(vm.SaveCommand.CanExecute(null));
            // Opened directly: only the folder save is offered. Sending needs a destination from an ERPNext scan link.
            Assert.False(vm.SendCommand.CanExecute(null));Assert.False(vm.SaveAndSendCommand.CanExecute(null));Assert.Equal(Wpf.Ui.Controls.ControlAppearance.Primary,vm.SaveAppearance);
            var end=DateTime.UtcNow.AddSeconds(10);while(vm.Pages.Any(p=>p.Thumbnail==null)&&DateTime.UtcNow<end)await Dispatcher.Yield(DispatcherPriority.ContextIdle);Assert.All(vm.Pages,p=>Assert.NotNull(p.Thumbnail));
            var display=new SelectionDisplayConverter();Assert.Equal(profile.Name,display.Convert(profile,typeof(string),null!,CultureInfo.InvariantCulture));Assert.Equal(Device.DisplayName,display.Convert(Device,typeof(string),null!,CultureInfo.InvariantCulture));Assert.Equal("PDF",display.Convert(ExportFormat.Pdf,typeof(string),null!,CultureInfo.InvariantCulture));
            var tabs=(TabControl)window.FindName("SettingsTabs");Assert.Equal(new[]{"Scan settings","Edit page"},tabs.Items.Cast<TabItem>().Select(t=>t.Header?.ToString()).ToArray());
            Layout(window,1440,900);Capture(window,"02-review-1440");
            Layout(window,1280,800);Capture(window,"03-review-125-percent",1.25);
            Layout(window,1000,700);var viewport=(ScrollViewer)window.FindName("PreviewScroll");Assert.True(viewport.ExtentWidth<=viewport.ViewportWidth+1);Capture(window,"04-review-150-percent",1.5);
            tabs.SelectedIndex=1;Layout(window,1440,900);Capture(window,"05-edit-page-1440");
            // Started from ERPNext: the destination is shown locked, Send becomes the primary action, and all three actions are offered.
            tabs.SelectedIndex=0;
            vm.ApplyDestination(new(){SiteId=Guid.NewGuid(),SiteOrigin="https://erp.example.com",Account="scanner.user@example.com",DocType="Purchase Invoice",DocName="ACC-PINV-2611-0042",Party="Northline Supply Co., Ltd.",Date="2026-11-16",Reference="IV6811-0123",Total="330.00",Currency="THB",Status="Unpaid",DocStatus=1});
            Assert.True(vm.HasDestination);Assert.True(vm.CanUpload);Assert.True(vm.SendCommand.CanExecute(null));Assert.True(vm.SaveAndSendCommand.CanExecute(null));Assert.True(vm.SaveCommand.CanExecute(null));
            Assert.Equal(Wpf.Ui.Controls.ControlAppearance.Primary,vm.SendAppearance);Assert.Equal(Wpf.Ui.Controls.ControlAppearance.Secondary,vm.SaveAppearance);
            Assert.True(File.Exists(Path.Combine(folder,"destination.json")));Assert.DoesNotContain("secret",File.ReadAllText(Path.Combine(folder,"destination.json")),StringComparison.OrdinalIgnoreCase);
            Assert.Equal("PI_ACC-PINV-2611-0042.pdf",vm.FilenamePreview);Assert.Contains("16-11-2026",vm.DestinationDetails);
            Layout(window,1440,900);Assert.Equal("ACC-PINV-2611-0042",((TextBlock)window.FindName("DestinationNumberText")).Text);Capture(window,"06-erpnext-destination-1440");
            Layout(window,1000,700);Capture(window,"07-erpnext-destination-150-percent",1.5);
            // A cancelled document keeps the pages and the folder save, and blocks both ERPNext actions with a stated reason.
            vm.ApplyDestination(vm.Destination! with{DocStatus=2,Status="Cancelled"});
            Assert.False(vm.CanUpload);Assert.False(vm.SendCommand.CanExecute(null));Assert.False(vm.SaveAndSendCommand.CanExecute(null));Assert.True(vm.SaveCommand.CanExecute(null));Assert.Equal(DocStatusRules.CancelledMessage,vm.DestinationWarning);
            Layout(window,1440,900);Capture(window,"08-erpnext-destination-cancelled-1440");
            vm.ApplyDestination(null);Assert.False(File.Exists(Path.Combine(folder,"destination.json")));Assert.False(vm.SendCommand.CanExecute(null));
            tabs.SelectedIndex=1;Layout(window,1440,900);
            byte[] before=File.ReadAllBytes(Path.Combine(folder,session.Pages[1].OriginalFile));int scanBrightness=vm.Brightness;
            vm.PageBrightness=120;vm.PageContrast=-60;vm.ApplyPageToneCommand.Execute(null);await WaitPreview(vm);Assert.Equal(120,session.Pages[1].Brightness);Assert.Equal(-60,session.Pages[1].Contrast);Assert.Equal(scanBrightness,vm.Brightness);Assert.Equal(before,File.ReadAllBytes(Path.Combine(folder,session.Pages[1].OriginalFile)));
            vm.ResetPageToneCommand.Execute(null);await WaitPreview(vm);Assert.Equal(0,session.Pages[1].Brightness);Assert.Equal(0,session.Pages[1].Contrast);Assert.Equal(scanBrightness,vm.Brightness);
            vm.ShowOriginal=true;await WaitPreview(vm);Assert.False(vm.ShowAdjusted);vm.ShowAdjusted=true;await WaitPreview(vm);Assert.False(vm.ShowOriginal);
            list.SelectAll();await WaitPreview(vm);Assert.Equal("3 pages selected",vm.SelectionSummary);Assert.False(vm.ManualCropCommand.CanExecute(null));Assert.True(vm.CropCommand.CanExecute(null));Assert.Equal(before,File.ReadAllBytes(Path.Combine(folder,session.Pages[1].OriginalFile)));
            Assert.Equal(0,scanner.Discoveries);Assert.Equal(1,scanner.Lookups);Assert.Equal(1,scanner.Capabilities);
        }
        finally{await DeleteWhenReleased(root);}
    });
    // A preview render started by the last selection change can still be reading a page file when the test ends.
    // Wait briefly for it; a handle that is never released still fails the test.
    private static async Task DeleteWhenReleased(string root)
    {
        for(int attempt=0;;attempt++)
        {
            try{Directory.Delete(root,true);return;}
            catch(IOException) when(attempt<50){await Task.Delay(100);}
        }
    }
}
