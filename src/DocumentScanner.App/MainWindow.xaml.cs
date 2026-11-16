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
    private void OnClosing(object? sender,CancelEventArgs e)=>e.Cancel=!vm.CanClose();
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

