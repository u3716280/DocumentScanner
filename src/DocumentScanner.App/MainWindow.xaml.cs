using System.IO;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using DocumentScanner.Infrastructure;
namespace DocumentScanner.App;
public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly MainViewModel vm;private readonly AppPaths paths;private Point dragPoint;private double zoom=1;
    public MainWindow(MainViewModel vm,AppPaths paths)
    {
        ThemeResources.Ensure(this);InitializeComponent();this.vm=vm;this.paths=paths;DataContext=vm;
        vm.WindowHandle=()=>new WindowInteropHelper(this).Handle;
        vm.RestoreSelection=ids=>{
            PageList.SelectedItems.Clear();foreach(var page in vm.Pages.Where(p=>ids.Contains(p.Data.Id)))PageList.SelectedItems.Add(page);
        };
        Loaded+=async(_,_)=>await vm.InitializeAsync();
    }
    private async void PagesChanged(object sender,SelectionChangedEventArgs e)=>await vm.SetSelectionAsync(PageList.SelectedItems.Cast<PageViewModel>().ToArray());
    /// <summary>Set by App once the tray icon exists. Windows created directly (tests) keep the plain close behaviour.</summary>
    public bool CloseToTray{get;set;}
    private bool exitRequested;
    /// <summary>Raised when closing hid the window instead of quitting.</summary>
    public event EventHandler? HiddenToTray;
    private void OnClosing(object? sender,CancelEventArgs e)
    {
        if(CloseToTray&&!exitRequested)
        {
            // The session, the scanner and any running scan stay as they are; only the window goes away.
            e.Cancel=true;Hide();HiddenToTray?.Invoke(this,EventArgs.Empty);return;
        }
        e.Cancel=!vm.CanClose();
    }
    /// <summary>Brings the window back from the tray, from the taskbar or from behind other windows.</summary>
    public void ShowFromTray()
    {
        Show();
        if(WindowState==WindowState.Minimized)WindowState=WindowState.Normal;
        Activate();
    }
    /// <summary>Exit from the tray menu: the usual close checks (busy, keep or discard pages) decide whether the app quits.</summary>
    public void RequestExit()
    {
        ShowFromTray();
        // A dialog (Settings, Save, Crop…) is still open: bring it forward and let the user finish it first.
        if(OwnedWindows.Count>0){OwnedWindows[0].Activate();return;}
        exitRequested=true;
        try{Close();}
        finally{exitRequested=false;}
    }
    private void SelectAllClick(object sender,RoutedEventArgs e)=>PageList.SelectAll();
    private void SelectBlanksClick(object sender,RoutedEventArgs e){PageList.SelectedItems.Clear();foreach(var page in vm.Pages.Where(p=>p.Data.BlankCandidate))PageList.SelectedItems.Add(page);}
    private void DragStart(object sender,MouseButtonEventArgs e)=>dragPoint=e.GetPosition(PageList);
    private void DragMove(object sender,MouseEventArgs e)
    {
        if(e.LeftButton!=MouseButtonState.Pressed||vm.Busy||PageList.SelectedItems.Count==0)return;
        Point p=e.GetPosition(PageList);if(Math.Abs(p.X-dragPoint.X)<SystemParameters.MinimumHorizontalDragDistance&&Math.Abs(p.Y-dragPoint.Y)<SystemParameters.MinimumVerticalDragDistance)return;
        if(ItemsControl.ContainerFromElement(PageList,e.OriginalSource as DependencyObject) is not ListBoxItem)return;
        DragDrop.DoDragDrop(PageList,new DataObject("DocumentScanner.Pages",PageList),DragDropEffects.Move);
    }
    private void PagesDrop(object sender,DragEventArgs e)
    {
        if(vm.Busy||!e.Data.GetDataPresent("DocumentScanner.Pages")||!ReferenceEquals(e.Data.GetData("DocumentScanner.Pages"),PageList))return;
        var item=ItemsControl.ContainerFromElement(PageList,e.OriginalSource as DependencyObject) as ListBoxItem;
        int target=item==null?vm.Pages.Count:PageList.ItemContainerGenerator.IndexFromContainer(item);
        if(item!=null&&e.GetPosition(item).Y>item.ActualHeight/2)target++;
        vm.MoveSelectionTo(target);e.Handled=true;
    }
    private void FitClick(object sender,RoutedEventArgs e){zoom=1;ApplyZoom();}
    private void ZoomInClick(object sender,RoutedEventArgs e){zoom=Math.Min(4,zoom*1.25);ApplyZoom();}
    private void ZoomOutClick(object sender,RoutedEventArgs e){zoom=Math.Max(0.25,zoom/1.25);ApplyZoom();}
    private void ApplyZoom(){PreviewContainer.LayoutTransform=new ScaleTransform(zoom,zoom);ZoomText.Text=$"{zoom:P0}";}
    private void SettingsClick(object sender,RoutedEventArgs e)
    {
        vm.OpenSettings(paths.Root);
    }
}

