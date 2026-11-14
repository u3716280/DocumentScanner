using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using DocumentScanner.Core;
namespace DocumentScanner.App;
public partial class ExportWindow : Window
{
    public ExportOptions? Options{get;private set;}
    public bool SelectedOnly=>SelectedBox.IsChecked==true;
    public ExportWindow(AppSettings settings,int selected,bool sendOnly=false)
    {
        InitializeComponent();FolderBox.Text=settings.OutputFolder;QualityBox.Text=settings.JpegQuality.ToString();
        FormatBox.ItemsSource=Enum.GetValues<ExportFormat>();FormatBox.SelectedItem=settings.Format;
        PaperBox.ItemsSource=Enum.GetValues<OutputPaper>();PaperBox.SelectedItem=OutputPaper.Original;
        NameBox.Text=FindAutomaticName(settings.Output);SelectedBox.IsChecked=true;SelectedBox.IsEnabled=false;SelectedBox.Content=$"Selected pages only · {selected} pages";
        if(sendOnly){FolderBox.IsEnabled=false;FolderBox.ToolTip="Send-only uses temporary session artifacts; no permanent folder is required.";}
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
            Options=new(FolderBox.Text.Trim(),NameBox.Text.Trim(),(ExportFormat)FormatBox.SelectedItem,quality,(OutputPaper)PaperBox.SelectedItem,FitBox.IsChecked==true);
            Options.Validate();DialogResult=true;
        }catch(ArgumentException ex){ValidationMessage.Text=ex.Message;}
    }
}
