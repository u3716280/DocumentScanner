using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using DocumentScanner.Core;
using DocumentScanner.Infrastructure;
namespace DocumentScanner.App;

/// <summary>ERPNext sites and credentials, the scan link registration, attachment descriptions and the data folder.</summary>
public sealed class SettingsWindow : Window
{
    private readonly ListBox list=new(){DisplayMemberPath="Name",MinHeight=72,Margin=new(0,0,0,8)};
    private readonly TextBox name=Ui.Input("ERPNext"),url=Ui.Input("https://");
    private readonly PasswordBox key=new(){MinHeight=32,Margin=new(0,3,0,10)},secret=new(){MinHeight=32,Margin=new(0,3,0,10)};
    private readonly TextBlock status=Ui.Note("The API key and secret are kept in Windows Credential Manager for your Windows account only. They are never written to the settings file or the application folder.");
    private readonly TextBlock linkStatus=Ui.Note("");
    private readonly TextBox descriptions=new(){AcceptsReturn=true,MinHeight=160,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,TextWrapping=TextWrapping.NoWrap};
    public List<ErpSite> Sites{get;}
    public List<string> Descriptions=>descriptions.Text.Split('\n').Select(AttachmentNaming.Sanitize).Where(d=>d.Length>0)
        .Select(d=>d.Length>AttachmentNaming.MaxDescription?d[..AttachmentNaming.MaxDescription]:d).Distinct(StringComparer.OrdinalIgnoreCase).Take(50).ToList();

    public SettingsWindow(IEnumerable<ErpSite> sites,IEnumerable<string> attachmentDescriptions,string dataFolder)
    {
        Sites=sites.ToList();Title="Settings";Width=700;Height=760;MinWidth=560;MinHeight=520;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var tabs=new TabControl();
        StackPanel Tab(string title){var panel=Ui.Panel();tabs.Items.Add(new TabItem{Header=title,Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}});return panel;}

        var erp=Tab("ERPNext");
        erp.Children.Add(Ui.Note("Scan links are accepted only from the sites listed here. Each person uses their own ERPNext API key, so ERPNext applies that person's permissions."));
        list.ItemsSource=Sites;erp.Children.Add(list);
        list.SelectionChanged+=(_,_)=>{if(list.SelectedItem is ErpSite s){name.Text=s.Name;url.Text=s.Url;key.Clear();secret.Clear();status.Text=WindowsCredentialStore.Load(s.Id)==null?"No API key is stored for this site yet.":"An API key is stored. Fill in both fields only to replace it.";}};
        Ui.Field(erp,"Site name",name);Ui.Field(erp,"Site address (https only, for example https://erp.example.com)",url);Ui.Field(erp,"API key",key);Ui.Field(erp,"API secret",secret);erp.Children.Add(status);
        var row=new WrapPanel();
        row.Children.Add(Ui.Button("New site",()=>{list.SelectedItem=null;name.Text="ERPNext";url.Text="https://";key.Clear();secret.Clear();status.Text="Enter the site address and your API key, then Save site.";}));
        row.Children.Add(Ui.Button("Save site",()=>Save(),true));
        var test=Ui.Button("Test connection",()=>{});
        test.Click+=async(_,_)=>await Ui.Guard(async()=>{
            test.IsEnabled=false;status.Text="Connecting…";
            try
            {
                var site=Save();
                using var client=new FrappeClient(site,WindowsCredentialStore.Load(site.Id)??throw new InvalidOperationException("Enter your API key and secret first."));
                string user=await client.GetLoggedUser(default);long limit=await client.GetMaxFileSize(default);
                status.Text=$"Connected as {user}.\nLargest file this site accepts: {UploadRunner.Megabytes(limit)}.";
            }
            catch(ErpException e){status.Text="Not connected. "+e.Message;}
            finally{test.IsEnabled=true;}
        });
        row.Children.Add(test);
        row.Children.Add(Ui.Button("Delete site",()=>{if(list.SelectedItem is ErpSite s&&MessageBox.Show($"Remove {s.Name} and its stored API key from this computer? Scan links from this site will be refused.",Title,MessageBoxButton.YesNo)==MessageBoxResult.Yes){Sites.Remove(s);WindowsCredentialStore.Delete(s.Id);Refresh();status.Text="Site removed.";}}));
        erp.Children.Add(row);

        var link=Tab("Scan link");
        link.Children.Add(Ui.Note("The Scan with DocumentScanner button in ERPNext opens a documentscanner:// link. Windows has to know which program handles it. Registering applies to your Windows account only and needs no administrator rights."));
        link.Children.Add(linkStatus);
        var linkRow=new WrapPanel();
        linkRow.Children.Add(Ui.Button("Register ERPNext scan link",()=>{UriRegistration.Register();ShowLinkStatus();},true));
        linkRow.Children.Add(Ui.Button("Test scan link",()=>{if(!UriRegistration.Status().MatchesThisCopy)throw new InvalidOperationException("Register the scan link for this copy first.");UriRegistration.OpenTestLink();}));
        linkRow.Children.Add(Ui.Button("Remove registration",()=>{UriRegistration.Remove();ShowLinkStatus();}));
        link.Children.Add(linkRow);
        link.Children.Add(Ui.Note("The first time, the browser asks whether to open DocumentScanner. Allow it. If you move this folder, register again from the new location."));

        var names=Tab("File names");
        names.Children.Add(Ui.Note("Files for an ERPNext document are named after it, for example PI_ACC-PINV-2611-0042.pdf. A description can be added, such as PI_ACC-PINV-2611-0042_ใบกำกับภาษี.pdf. List the descriptions to suggest, one per line."));
        descriptions.Text=string.Join(Environment.NewLine,attachmentDescriptions);names.Children.Add(descriptions);

        var data=Tab("Data");
        data.Children.Add(Ui.Note(Text.Get("ConfigInfo")));
        data.Children.Add(Ui.Button(Text.Get("OpenFolder"),()=>Process.Start(new ProcessStartInfo(dataFolder){UseShellExecute=true})));

        var root=new DockPanel();var close=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right,Margin=new(12)};DockPanel.SetDock(close,Dock.Bottom);
        close.Children.Add(Ui.Button("Close",Close));root.Children.Add(close);root.Children.Add(tabs);Content=root;
        if(Sites.Count>0)list.SelectedIndex=0;
        Ui.Guard(ShowLinkStatus);
    }
    private void ShowLinkStatus()=>linkStatus.Text=UriRegistration.Status().Describe(UriRegistration.CurrentExecutable);
    private ErpSite Save()
    {
        var previous=list.SelectedItem as ErpSite;
        var site=new ErpSite{Id=previous?.Id??Guid.NewGuid(),Name=name.Text.Trim(),Url=url.Text.Trim().TrimEnd('/')};site.Validate();
        if(Sites.Any(s=>s.Id!=site.Id&&SiteOrigins.Same(s.Url,site.Url)))throw new ArgumentException("This site address is already in the list.");
        bool entered=key.Password.Length>0||secret.Password.Length>0;
        if(entered)
        {
            if(key.Password.Length==0||secret.Password.Length==0)throw new ArgumentException("Enter both the API key and the API secret.");
            WindowsCredentialStore.Save(site.Id,new(key.Password.Trim(),secret.Password.Trim()));key.Clear();secret.Clear();
        }
        if(previous!=null)Sites.Remove(previous);Sites.Add(site);Refresh();list.SelectedItem=site;
        status.Text="Site saved. Use Test connection to check the address and key.";return site;
    }
    private void Refresh(){list.ItemsSource=null;list.ItemsSource=Sites;}
}
