using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using DocumentScanner.Core;
using DocumentScanner.Infrastructure;
namespace DocumentScanner.App;
public sealed class ProfileManagerWindow : Window
{
    private readonly ListBox list=new(){DisplayMemberPath="Name",MinWidth=215,Margin=new(12)};private readonly StackPanel editor=Ui.Panel();private readonly IReadOnlyList<DeviceInfo> devices;private readonly Action<AppSettings> persist;
    private TextBox name=null!,dpi=null!,brightness=null!,contrast=null!,folder=null!,prefix=null!,pattern=null!,quality=null!,doctype=null!;
    private ComboBox device=null!,source=null!,color=null!,paper=null!,format=null!,site=null!;
    private CheckBox landscape=null!,deskew=null!,crop=null!,blank=null!,remove=null!,native=null!,privacy=null!;
    private ScanProfile? editing;private bool dirty,loading;
    public AppSettings ResultSettings{get;private set;}
    public ScanProfile? LoadRequested{get;private set;}
    public ProfileManagerWindow(AppSettings settings,IReadOnlyList<DeviceInfo> devices,ScanProfile? selected,ScanProfile? snapshot,Action<AppSettings> persist)
    {
        ResultSettings=settings;this.devices=devices;this.persist=persist;Title="Profile Manager";Width=1000;Height=820;MinWidth=760;MinHeight=600;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var root=new DockPanel();var top=new WrapPanel{Margin=new(12)};DockPanel.SetDock(top,Dock.Top);
        top.Children.Add(Ui.Button("Create profile",()=>SelectNew(snapshot)));top.Children.Add(Ui.Button("Import JSON",Import));top.Children.Add(Ui.Button("Export JSON",Export));root.Children.Add(top);
        var bottom=new WrapPanel{Margin=new(12),HorizontalAlignment=HorizontalAlignment.Right};DockPanel.SetDock(bottom,Dock.Bottom);
        bottom.Children.Add(Ui.Button("Rename",()=>Save(false)));bottom.Children.Add(Ui.Button("Delete",Delete));bottom.Children.Add(Ui.Button("Set default",()=>{var p=editing??throw new ArgumentException("Save a profile first.");Commit(ResultSettings with{DefaultProfileId=p.Id});MessageBox.Show("Default profile set. It will be capability-validated on startup.",Title);}));
        bottom.Children.Add(Ui.Button("Save as",()=>Save(true)));bottom.Children.Add(Ui.Button("Save profile",()=>Save(false),true));bottom.Children.Add(Ui.Button("Load selected",()=>{Save(false);LoadRequested=editing;dirty=false;DialogResult=true;}));bottom.Children.Add(Ui.Button("Close",Close));root.Children.Add(bottom);
        var grid=new Grid();grid.ColumnDefinitions.Add(new(){Width=new(240)});grid.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});grid.Children.Add(list);var scroll=new ScrollViewer{Content=editor,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};Grid.SetColumn(scroll,1);grid.Children.Add(scroll);root.Children.Add(grid);Content=root;
        list.SelectionChanged+=(_,_)=>{if(loading)return;if(dirty&&MessageBox.Show("Discard unsaved form changes?",Title,MessageBoxButton.YesNo)!=MessageBoxResult.Yes){loading=true;list.SelectedItem=editing;loading=false;return;}Show(list.SelectedItem as ScanProfile);};
        Closing+=(_,e)=>{if(dirty&&MessageBox.Show("Discard unsaved form changes? Saved profiles are kept.",Title,MessageBoxButton.YesNo)!=MessageBoxResult.Yes)e.Cancel=true;};
        Refresh(selected?.Id);if(selected==null)SelectNew(snapshot);
    }
    private void Commit(AppSettings value){value.Validate();persist(value);ResultSettings=value;}
    private void Refresh(Guid? id){loading=true;list.ItemsSource=ResultSettings.Profiles;list.SelectedItem=ResultSettings.Profiles.FirstOrDefault(p=>p.Id==id);loading=false;Show(list.SelectedItem as ScanProfile);}
    private void SelectNew(ScanProfile? snapshot){if(dirty&&MessageBox.Show("Discard unsaved changes?",Title,MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;loading=true;list.SelectedItem=null;loading=false;Show(snapshot is null?null:snapshot with{Id=Guid.NewGuid(),Name="New profile"});editing=null;}
    private void Show(ScanProfile? p)
    {
        loading=true;editor.Children.Clear();editing=p;var s=p?.Settings??new();var o=p?.Output??ResultSettings.Output;var e=p?.Erp??ResultSettings.Erp;
        editor.Children.Add(Ui.Note("Settings are saved as profile schema 2. Loading validates the exact device, source, DPI and color before applying. Existing pages keep their original settings."));name=Ui.Input(p?.Name??"New profile");Ui.Field(editor,"Profile name",name);
        var tabs=new TabControl();editor.Children.Add(tabs);
        StackPanel Tab(string title){var panel=Ui.Panel();tabs.Items.Add(new TabItem{Header=title,Content=panel});return panel;}
        var scan=Tab("Scanner");var available=devices.Concat(p==null?[]:new[]{p.Device}).DistinctBy(d=>d.Key).ToArray();device=Ui.Choice(available,p?.Device??available.FirstOrDefault());device.DisplayMemberPath="DisplayName";Ui.Field(scan,"Device / driver",device);
        source=Ui.Choice(Enum.GetValues<ScanSource>(),s.Source);Ui.Field(scan,"Paper source (Duplex = duplex feeder)",source);dpi=Ui.Input(s.Dpi.ToString());Ui.Field(scan,"DPI",dpi);color=Ui.Choice(Enum.GetValues<ScanColor>(),s.Color);Ui.Field(scan,"Color mode",color);paper=Ui.Choice(new[]{"Auto","A4","A5","Letter","Legal"},s.Paper);Ui.Field(scan,"Paper size",paper);landscape=Ui.Check("Landscape",s.Landscape);scan.Children.Add(landscape);native=Ui.Check("Use native scanner dialog (explicit driver overrides)",s.NativeUi);scan.Children.Add(native);
        var processing=Tab("Processing");brightness=Ui.Input(s.Brightness.ToString());Ui.Field(processing,"Brightness (-1000…1000; 0 = no app adjustment)",brightness);contrast=Ui.Input(s.Contrast.ToString());Ui.Field(processing,"Contrast (-1000…1000)",contrast);deskew=Ui.Check("Auto deskew",s.Deskew);crop=Ui.Check("Auto crop — geometry only",s.AutoCrop);blank=Ui.Check("Mark possible blank pages",s.DetectBlank);remove=Ui.Check("Review blank candidates for recoverable removal after scanning",s.RemoveBlank);foreach(var c in new[]{deskew,crop,blank,remove})processing.Children.Add(c);
        var output=Tab("Output");format=Ui.Choice(Enum.GetValues<ExportFormat>(),o.Format);Ui.Field(output,"Export format",format);quality=Ui.Input(o.JpegQuality.ToString());Ui.Field(output,"JPEG quality (60–100)",quality);folder=Ui.Input(o.Folder);Ui.Field(output,"Local folder",folder);prefix=Ui.Input(o.Prefix);Ui.Field(output,"Filename prefix",prefix);pattern=Ui.Input(o.Pattern);Ui.Field(output,"Filename pattern",pattern);output.Children.Add(Ui.Note("Tokens: {prefix}, {date:yyyyMMdd}, {sequence:0000}. JPG/PNG append page numbers. Missing local folder does not prevent send-only."));
        var erp=Tab("ERPNext");erp.Children.Add(Ui.BridgeNotice());site=Ui.Choice(ResultSettings.Sites,ResultSettings.Sites.FirstOrDefault(x=>x.Id==e.SiteId));site.DisplayMemberPath="Name";Ui.Field(erp,"ERPNext Site Profile",site);erp.Children.Add(Ui.Button("Manage sites / credentials",()=>{var w=new SitesWindow(ResultSettings.Sites){Owner=this};w.ShowDialog();Commit(ResultSettings with{Sites=w.Sites});site.ItemsSource=ResultSettings.Sites;}));erp.Children.Add(Ui.Button("Clear site reference",()=>site.SelectedItem=null));doctype=Ui.Input(e.DocType);Ui.Field(erp,"Default DocType",doctype);privacy=Ui.Check("Private attachment",e.Private);erp.Children.Add(privacy);erp.Children.Add(Ui.Note("API credentials are stored separately in Windows Credential Manager and never exported. Loading a profile never selects or confirms a destination document. Search and confirm before sending."));
        foreach(var input in new[]{name,dpi,brightness,contrast,folder,prefix,pattern,quality,doctype})input.TextChanged+=(_,_)=>{if(!loading)dirty=true;};foreach(var box in new[]{device,source,color,paper,format,site})box.SelectionChanged+=(_,_)=>{if(!loading)dirty=true;};foreach(var box in new[]{landscape,native,deskew,crop,blank,remove,privacy}){box.Checked+=(_,_)=>{if(!loading)dirty=true;};box.Unchecked+=(_,_)=>{if(!loading)dirty=true;};}loading=false;dirty=false;
    }
    private void Save(bool copy)
    {
        int Number(TextBox x,string label)=>int.TryParse(x.Text,out var n)?n:throw new ArgumentException(label+" must be an integer.");
        var p=new ScanProfile(name.Text.Trim(),device.SelectedItem as DeviceInfo??throw new ArgumentException("Select a scanner. Discover/connect it in the main window first."),new(){Source=(ScanSource)source.SelectedItem,Dpi=Number(dpi,"DPI"),Color=(ScanColor)color.SelectedItem,Paper=(string)paper.SelectedItem,Landscape=landscape.IsChecked==true,Deskew=deskew.IsChecked==true,AutoCrop=crop.IsChecked==true,DetectBlank=blank.IsChecked==true,RemoveBlank=remove.IsChecked==true,NativeUi=native.IsChecked==true,Brightness=Number(brightness,"Brightness"),Contrast=Number(contrast,"Contrast")})
        {Id=copy||editing==null?Guid.NewGuid():editing.Id,Output=new(){Format=(ExportFormat)format.SelectedItem,JpegQuality=Number(quality,"Quality"),Folder=folder.Text.Trim(),Prefix=prefix.Text,Pattern=pattern.Text},Erp=new(){SiteId=(site.SelectedItem as ErpSite)?.Id,DocType=doctype.Text.Trim(),Private=privacy.IsChecked==true}};
        ProfileValidation.Validate(p);if(ResultSettings.Profiles.Any(x=>x.Id!=p.Id&&x.Name.Equals(p.Name,StringComparison.OrdinalIgnoreCase)))throw new ArgumentException("Choose a unique profile name (Save As also needs a new name).");
        var profiles=ResultSettings.Profiles.Where(x=>x.Id!=p.Id).Append(p).ToList();Commit(ResultSettings with{Profiles=profiles});dirty=false;Refresh(p.Id);
    }
    private void Delete(){if(editing is not {} p)return;if(MessageBox.Show("Delete profile "+p.Name+"? Scan pages and credentials are kept.",Title,MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;Commit(ResultSettings with{Profiles=ResultSettings.Profiles.Where(x=>x.Id!=p.Id).ToList(),DefaultProfileId=ResultSettings.DefaultProfileId==p.Id?null:ResultSettings.DefaultProfileId});dirty=false;Refresh(null);}
    private void Export(){if(editing is not {} p)throw new ArgumentException("Select/save a profile first.");var d=new SaveFileDialog{Filter="Profile JSON|*.json",FileName="scanner-profile.json"};if(d.ShowDialog()==true)JsonFiles.Write(d.FileName,new ProfilePackage(2,[p]));}
    private void Import(){var d=new OpenFileDialog{Filter="Profile JSON|*.json"};if(d.ShowDialog()!=true)return;var package=JsonFiles.Read<ProfilePackage>(d.FileName);package.Validate();var profiles=ResultSettings.Profiles.ToList();foreach(var p in package.Profiles){var conflict=profiles.FirstOrDefault(x=>x.Id==p.Id||x.Name.Equals(p.Name,StringComparison.OrdinalIgnoreCase));if(conflict!=null){var choice=MessageBox.Show("Profile conflict: "+p.Name+"\nYes = replace existing; No = import as a new copy; Cancel = stop import.",Title,MessageBoxButton.YesNoCancel);if(choice==MessageBoxResult.Cancel)return;if(choice==MessageBoxResult.Yes){profiles.Remove(conflict);profiles.Add(p with{Id=conflict.Id});}else{string n=p.Name+" (imported)";while(profiles.Any(x=>x.Name.Equals(n,StringComparison.OrdinalIgnoreCase)))n+=" copy";profiles.Add(p with{Id=Guid.NewGuid(),Name=n});}}else profiles.Add(p);}Commit(ResultSettings with{Profiles=profiles});dirty=false;Refresh(profiles.Last().Id);}
}

