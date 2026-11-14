using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using DocumentScanner.Core;
namespace DocumentScanner.App;
public sealed class CropWindow : Window
{
    private readonly Canvas surface=new(){Background=Brushes.Transparent,Cursor=Cursors.Cross};
    private readonly Rectangle frame=new(){Stroke=Brushes.DodgerBlue,StrokeThickness=2,Fill=new SolidColorBrush(Color.FromArgb(35,30,120,255)),IsHitTestVisible=false};
    private readonly Image image;private readonly Button apply=new(){Content=Text.Get("ApplyCrop"),IsEnabled=false,MinWidth=110};
    private Point start;private bool dragging;private Rect content;
    public NormalizedCrop? Selection{get;private set;}
    public CropWindow(BitmapSource bitmap)
    {
        Title=Text.Get("ManualCrop");Width=950;Height=750;MinWidth=440;MinHeight=360;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        image=new(){Source=bitmap,Stretch=Stretch.Uniform,IsHitTestVisible=false};
        var root=new DockPanel{Margin=new Thickness(12)};
        var hint=new TextBlock{Text=Text.Get("CropHint"),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8)};DockPanel.SetDock(hint,Dock.Top);root.Children.Add(hint);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};DockPanel.SetDock(buttons,Dock.Bottom);
        var cancel=new Button{Content=Text.Get("CropCancel"),IsCancel=true,MinWidth=90};buttons.Children.Add(apply);buttons.Children.Add(cancel);root.Children.Add(buttons);
        var grid=new Grid{Background=Brushes.DimGray};grid.Children.Add(image);grid.Children.Add(surface);surface.Children.Add(frame);frame.Visibility=Visibility.Collapsed;root.Children.Add(grid);Content=root;
        apply.Click+=(_,_)=>{if(Selection!=null)DialogResult=true;};
        surface.SizeChanged+=(_,_)=>{
            double scale=Math.Min(surface.ActualWidth/bitmap.Width,surface.ActualHeight/bitmap.Height);
            content=new((surface.ActualWidth-bitmap.Width*scale)/2,(surface.ActualHeight-bitmap.Height*scale)/2,bitmap.Width*scale,bitmap.Height*scale);
            dragging=false;surface.ReleaseMouseCapture();Selection=null;apply.IsEnabled=false;frame.Visibility=Visibility.Collapsed;
        };
        surface.MouseLeftButtonDown+=(_,e)=>{var p=e.GetPosition(surface);if(!content.Contains(p))return;start=p;dragging=true;surface.CaptureMouse();Selection=null;apply.IsEnabled=false;Draw(p);};
        surface.MouseMove+=(_,e)=>{if(dragging)Draw(e.GetPosition(surface));};
        surface.MouseLeftButtonUp+=(_,e)=>{if(!dragging)return;Draw(e.GetPosition(surface));dragging=false;surface.ReleaseMouseCapture();};
    }
    private void Draw(Point p)
    {
        p=new(Math.Clamp(p.X,content.Left,content.Right),Math.Clamp(p.Y,content.Top,content.Bottom));var rect=new Rect(start,p);
        Canvas.SetLeft(frame,rect.X);Canvas.SetTop(frame,rect.Y);frame.Width=rect.Width;frame.Height=rect.Height;frame.Visibility=Visibility.Visible;
        if(rect.Width<4||rect.Height<4){Selection=null;apply.IsEnabled=false;return;}
        Selection=new((rect.X-content.X)/content.Width,(rect.Y-content.Y)/content.Height,rect.Width/content.Width,rect.Height/content.Height);apply.IsEnabled=true;
    }
}
