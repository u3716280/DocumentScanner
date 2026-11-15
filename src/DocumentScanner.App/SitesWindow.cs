using System.Windows;
using System.Windows.Controls;
using DocumentScanner.Core;
using DocumentScanner.Infrastructure;
namespace DocumentScanner.App;
public sealed class SitesWindow : Window
{
    private readonly ListBox list=new(){DisplayMemberPath="Name",MinHeight=80};private readonly TextBox name=Ui.Input(""),url=Ui.Input("https://");private readonly PasswordBox key=new(),secret=new();private readonly TextBlock status=Ui.Note("Credentials are stored in Windows Credential Manager for this Windows user, never in Profile JSON.");
    public List<ErpSite> Sites{get;private set;}
    public ErpSite? SelectedSite=>list.SelectedItem as ErpSite;
    public SitesWindow(IEnumerable<ErpSite> sites,Guid? selectedSiteId=null)
    {
        Sites=sites.ToList();Title="ERPNext site profiles";Width=650;Height=730;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var p=Ui.Panel();list.ItemsSource=Sites;p.Children.Add(list);list.SelectionChanged+=(_,_)=>{if(list.SelectedItem is ErpSite s){name.Text=s.Name;url.Text=s.Url;key.Clear();secret.Clear();status.Text="Credentials remain in Windows storage. Enter both fields only to replace them.";}};
        p.Children.Add(Ui.BridgeNotice());
        Ui.Field(p,"Site name",name);Ui.Field(p,"HTTPS site origin",url);Ui.Field(p,"API key (leave both empty to keep stored credentials)",key);Ui.Field(p,"API secret",secret);p.Children.Add(status);
        var row=new WrapPanel();row.Children.Add(Ui.Button("New site",()=>{list.SelectedItem=null;name.Text="ERPNext";url.Text="https://";key.Clear();secret.Clear();}));
        row.Children.Add(Ui.Button("Save site",()=>Save(),true));var test=Ui.Button("Test connection",()=>{});test.Click+=async(_,_)=>await Ui.Guard(async()=>{test.IsEnabled=false;try{var s=Save();using var client=new ErpClient(s,WindowsCredentialStore.Load(s.Id)??throw new InvalidOperationException("Enter API credentials first."));var r=await client.Connect(default);status.Text=$"Connected as {r.Account} · Bridge {r.Version}";}finally{test.IsEnabled=true;}});row.Children.Add(test);
        row.Children.Add(Ui.Button("Delete site",()=>{if(list.SelectedItem is ErpSite s&&MessageBox.Show("Delete this site reference? Existing scanner profiles will show it as unresolved. Credentials remain in Windows Credential Manager.",Title,MessageBoxButton.YesNo)==MessageBoxResult.Yes){Sites.Remove(s);Refresh();}}));
        row.Children.Add(Ui.Button("Close",Close));p.Children.Add(row);if(selectedSiteId is {} id)list.SelectedItem=Sites.FirstOrDefault(s=>s.Id==id);Content=new ScrollViewer{Content=p,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    }
    private ErpSite Save()
    {
        var previous=list.SelectedItem as ErpSite;var s=new ErpSite{Id=previous?.Id??Guid.NewGuid(),Name=name.Text.Trim(),Url=url.Text.Trim().TrimEnd('/')};s.Validate();
        bool entered=key.Password.Length>0||secret.Password.Length>0;
        if(entered){if(key.Password.Length==0||secret.Password.Length==0)throw new ArgumentException("Enter both API key and secret.");WindowsCredentialStore.Save(s.Id,new(key.Password,secret.Password));key.Clear();secret.Clear();}
        if(previous!=null)Sites.Remove(previous);Sites.Add(s);Refresh();list.SelectedItem=s;status.Text="Site saved. Test the connection to verify the bridge and account.";return s;
    }
    private void Refresh(){list.ItemsSource=null;list.ItemsSource=Sites;}
}

