using System.Globalization;
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
    private readonly Canvas surface=new(){Background=Brushes.Transparent,Cursor=Cursors.Cross,Focusable=true};
    private readonly Rectangle frame=new(){Stroke=Brushes.DodgerBlue,StrokeThickness=2,Fill=new SolidColorBrush(Color.FromArgb(35,30,120,255)),IsHitTestVisible=false};
    private readonly List<Rectangle> handles=[];private readonly TextBox[] coordinates=Enumerable.Range(0,4).Select(_=>Ui.Input("0")).ToArray();
    private readonly Button apply=new(){Content=Text.Get("ApplyCrop"),IsEnabled=false,MinWidth=110};
    private Point start;private bool dragging;private Rect content,initial;private string mode="new";
    public NormalizedCrop? Selection{get;private set;}
    public CropWindow(BitmapSource bitmap)
    {
        Title=Text.Get("ManualCrop");Width=1000;Height=790;MinWidth=550;MinHeight=440;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var root=new DockPanel{Margin=new(12)};var hint=Ui.Note("Drag a rectangle, move it, or resize its handles. Arrow keys move 1 image pixel; Shift = 10 pixels; Alt + arrows resize. Escape cancels.");DockPanel.SetDock(hint,Dock.Top);root.Children.Add(hint);
        var numeric=new WrapPanel();DockPanel.SetDock(numeric,Dock.Bottom);string[] names=["X %","Y %","Width %","Height %"];for(int i=0;i<4;i++){numeric.Children.Add(new Label{Content=names[i],Target=coordinates[i]});coordinates[i].Width=65;numeric.Children.Add(coordinates[i]);}numeric.Children.Add(Ui.Button("Set rectangle",()=>{var v=coordinates.Select(x=>double.Parse(x.Text,CultureInfo.CurrentCulture)/100).ToArray();var selection=new NormalizedCrop(v[0],v[1],v[2],v[3]);selection.Validate();Selection=selection;Project();}));root.Children.Add(numeric);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};DockPanel.SetDock(buttons,Dock.Bottom);buttons.Children.Add(apply);buttons.Children.Add(new Button{Content=Text.Get("CropCancel"),IsCancel=true,MinWidth=90});root.Children.Add(buttons);
        var grid=new Grid{Background=Brushes.DimGray};grid.Children.Add(new Image{Source=bitmap,Stretch=Stretch.Uniform,IsHitTestVisible=false});grid.Children.Add(surface);surface.Children.Add(frame);frame.Visibility=Visibility.Collapsed;
        for(int i=0;i<8;i++){var handle=new Rectangle{Width=9,Height=9,Fill=Brushes.White,Stroke=Brushes.DodgerBlue,IsHitTestVisible=false,Visibility=Visibility.Collapsed};handles.Add(handle);surface.Children.Add(handle);}root.Children.Add(grid);Content=root;
        apply.Click+=(_,_)=>{if(Selection!=null)DialogResult=true;};
        surface.SizeChanged+=(_,_)=>{double scale=Math.Min(surface.ActualWidth/bitmap.Width,surface.ActualHeight/bitmap.Height);content=new((surface.ActualWidth-bitmap.Width*scale)/2,(surface.ActualHeight-bitmap.Height*scale)/2,bitmap.Width*scale,bitmap.Height*scale);dragging=false;surface.ReleaseMouseCapture();Project();};
        surface.MouseLeftButtonDown+=(_,e)=>{
            start=e.GetPosition(surface);if(!content.Contains(start))return;surface.Focus();initial=Selection==null?Rect.Empty:Rectangle();mode="new";
            if(!initial.IsEmpty){bool l=Math.Abs(start.X-initial.Left)<=10,r=Math.Abs(start.X-initial.Right)<=10,t=Math.Abs(start.Y-initial.Top)<=10,b=Math.Abs(start.Y-initial.Bottom)<=10;
                if((l||r)&&start.Y>=initial.Top-10&&start.Y<=initial.Bottom+10||(t||b)&&start.X>=initial.Left-10&&start.X<=initial.Right+10)mode=(l?"l":r?"r":"")+(t?"t":b?"b":"");else if(initial.Contains(start))mode="move";}
            dragging=true;surface.CaptureMouse();Draw(start);
        };
        surface.MouseMove+=(_,e)=>{if(dragging)Draw(e.GetPosition(surface));};surface.MouseLeftButtonUp+=(_,e)=>{if(!dragging)return;Draw(e.GetPosition(surface));dragging=false;surface.ReleaseMouseCapture();};
        surface.KeyDown+=(_,e)=>{if(Selection is not {} c||e.Key is not(Key.Left or Key.Right or Key.Up or Key.Down))return;double dx=((Keyboard.Modifiers&ModifierKeys.Shift)!=0?10:1)/(double)bitmap.PixelWidth,dy=((Keyboard.Modifiers&ModifierKeys.Shift)!=0?10:1)/(double)bitmap.PixelHeight;bool resize=(Keyboard.Modifiers&ModifierKeys.Alt)!=0;
            var n=e.Key switch{Key.Left=>resize?c with{Width=Math.Max(dx,c.Width-dx)}:c with{X=Math.Max(0,c.X-dx)},Key.Right=>resize?c with{Width=Math.Min(1-c.X,c.Width+dx)}:c with{X=Math.Min(1-c.Width,c.X+dx)},Key.Up=>resize?c with{Height=Math.Max(dy,c.Height-dy)}:c with{Y=Math.Max(0,c.Y-dy)},_=>resize?c with{Height=Math.Min(1-c.Y,c.Height+dy)}:c with{Y=Math.Min(1-c.Height,c.Y+dy)}};Selection=n;Project();e.Handled=true;};
    }
    private Rect Rectangle()=>Selection is {} c?new(content.X+c.X*content.Width,content.Y+c.Y*content.Height,c.Width*content.Width,c.Height*content.Height):Rect.Empty;
    private void Draw(Point point)
    {
        point=new(Math.Clamp(point.X,content.Left,content.Right),Math.Clamp(point.Y,content.Top,content.Bottom));Rect r;
        if(mode=="new")r=new(start,point);
        else if(mode=="move")r=new(Math.Clamp(initial.X+point.X-start.X,content.Left,content.Right-initial.Width),Math.Clamp(initial.Y+point.Y-start.Y,content.Top,content.Bottom-initial.Height),initial.Width,initial.Height);
        else{double l=mode.Contains('l')?Math.Min(point.X,initial.Right-1):initial.Left,top=mode.Contains('t')?Math.Min(point.Y,initial.Bottom-1):initial.Top,right=mode.Contains('r')?Math.Max(point.X,initial.Left+1):initial.Right,bottom=mode.Contains('b')?Math.Max(point.Y,initial.Top+1):initial.Bottom;r=new(l,top,right-l,bottom-top);}
        if(r.Width<1||r.Height<1){Selection=null;Project();return;}Selection=new((r.X-content.X)/content.Width,(r.Y-content.Y)/content.Height,r.Width/content.Width,r.Height/content.Height);Project();
    }
    private void Project()
    {
        bool valid=Selection!=null;apply.IsEnabled=valid;frame.Visibility=valid?Visibility.Visible:Visibility.Collapsed;foreach(var h in handles)h.Visibility=frame.Visibility;if(!valid||content.Width<=0||content.Height<=0)return;
        var r=Rectangle();Canvas.SetLeft(frame,r.X);Canvas.SetTop(frame,r.Y);frame.Width=r.Width;frame.Height=r.Height;
        Point[] points=[r.TopLeft,new(r.X+r.Width/2,r.Y),r.TopRight,new(r.Right,r.Y+r.Height/2),r.BottomRight,new(r.X+r.Width/2,r.Bottom),r.BottomLeft,new(r.Left,r.Y+r.Height/2)];for(int i=0;i<8;i++){Canvas.SetLeft(handles[i],points[i].X-4.5);Canvas.SetTop(handles[i],points[i].Y-4.5);}
        double[] values=[Selection!.X,Selection.Y,Selection.Width,Selection.Height];for(int i=0;i<4;i++)coordinates[i].Text=(values[i]*100).ToString("0.###",CultureInfo.CurrentCulture);
    }
}
