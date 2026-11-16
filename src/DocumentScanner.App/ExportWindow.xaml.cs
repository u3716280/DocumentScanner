using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Microsoft.Win32;
using DocumentScanner.Core;
namespace DocumentScanner.App;
public partial class ExportWindow : Window
{
    private readonly string requiredPrefix;
    private readonly ErpDestination? destination;
    public ExportOptions? Options{get;private set;}
    public bool SelectedOnly=>SelectedBox.IsChecked==true;
    /// <param name="destination">
    /// When the scan was started from ERPNext: the file name is built from that document and cannot be typed.
    /// The user only chooses an optional description.
    /// </param>
    public ExportWindow(AppSettings settings,int selected,bool sendOnly=false,ErpDestination? destination=null,IEnumerable<string>? descriptions=null)
    {
        this.destination=destination;
        requiredPrefix=settings.Output.Prefix;InitializeComponent();FolderBox.Text=settings.OutputFolder;QualityBox.Text=settings.JpegQuality.ToString();
        FormatBox.ItemsSource=Enum.GetValues<ExportFormat>();FormatBox.SelectedItem=settings.Format;
        PaperBox.ItemsSource=Enum.GetValues<OutputPaper>();PaperBox.SelectedItem=OutputPaper.Original;
        SelectedBox.IsChecked=true;SelectedBox.IsEnabled=false;SelectedBox.Content=$"Selected pages only: {selected}";
        if(sendOnly){FolderBox.IsEnabled=false;FolderBox.ToolTip="Sending only keeps a temporary copy inside the scan session until ERPNext confirms it. No folder is needed.";}
        if(destination==null){NameBox.Text=FindAutomaticName(settings.Output);return;}
        DescriptionPanel.Visibility=Visibility.Visible;NameBox.IsReadOnly=true;NameBox.Focusable=false;NameBox.ToolTip="Named after the ERPNext document. A number is added if this name is already used.";
        var choices=new List<string>();
        if(!string.IsNullOrWhiteSpace(destination.Reference))choices.Add(destination.Reference);
        choices.AddRange(descriptions??[]);
        DescriptionBox.ItemsSource=choices.Select(AttachmentNaming.Sanitize).Where(c=>c.Length>0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        DescriptionBox.AddHandler(TextBoxBase.TextChangedEvent,new TextChangedEventHandler((_,_)=>UpdateName()));
        DescriptionBox.SelectionChanged+=(_,_)=>Dispatcher.BeginInvoke(UpdateName);
        UpdateName();
    }
    private void UpdateName()
    {
        if(destination==null)return;
        try{NameBox.Text=AttachmentNaming.BaseName(destination.DocType,destination.DocName,DescriptionBox.Text);ValidationMessage.Text="";}
        catch(Exception e) when(e is ArgumentException or ScanLinkException){ValidationMessage.Text=e.Message;}
    }
    private static string FindAutomaticName(OutputDefaults output)
    {
        for(int n=1;n<100000;n++)
        {
            string name=OutputNaming.Resolve(output,DateTime.Now,n);
            if(!Directory.Exists(output.Folder)||!Directory.EnumerateFiles(output.Folder,name+"*").Any())return name;
        }
        throw new IOException("No available output filename.");
    }
    private void FormatChanged(object sender,SelectionChangedEventArgs e)
    {
        if(PaperBox==null)return;bool pdf=FormatBox.SelectedItem is ExportFormat.Pdf;
        PaperBox.IsEnabled=pdf;FitBox.IsEnabled=pdf;QualityBox.IsEnabled=FormatBox.SelectedItem is ExportFormat.Jpg;
    }
    private void BrowseClick(object sender,RoutedEventArgs e)
    {
        var dialog=new OpenFolderDialog();if(Directory.Exists(FolderBox.Text))dialog.InitialDirectory=FolderBox.Text;
        if(dialog.ShowDialog()==true)FolderBox.Text=dialog.FolderName;
    }
    private void SaveClick(object sender,RoutedEventArgs e)
    {
        try {
            if(!int.TryParse(QualityBox.Text,out int quality))throw new ArgumentException("JPEG quality must be a number.");
            string name=destination==null?NameBox.Text.Trim():AttachmentNaming.BaseName(destination.DocType,destination.DocName,DescriptionBox.Text);
            if(destination==null&&requiredPrefix.Length>0&&!name.StartsWith(requiredPrefix,StringComparison.Ordinal))throw new ArgumentException("Filename must begin with the configured prefix: "+requiredPrefix+". Change Prefix in the main output bar if needed.");
            Options=new(FolderBox.Text.Trim(),name,(ExportFormat)FormatBox.SelectedItem,quality,(OutputPaper)PaperBox.SelectedItem,FitBox.IsChecked==true);
            Options.Validate();DialogResult=true;
        }catch(Exception ex) when(ex is ArgumentException or ScanLinkException){ValidationMessage.Text=ex.Message;}
    }
}
