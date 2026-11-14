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
    public ExportWindow(AppSettings settings,int selected)
    {
        InitializeComponent();FolderBox.Text=settings.OutputFolder;QualityBox.Text=settings.JpegQuality.ToString();
        FormatBox.ItemsSource=Enum.GetValues<ExportFormat>();FormatBox.SelectedItem=settings.Format;
        PaperBox.ItemsSource=Enum.GetValues<OutputPaper>();PaperBox.SelectedItem=OutputPaper.Original;
        NameBox.Text=FindAutomaticName(settings.OutputFolder);SelectedBox.IsEnabled=selected>0;
    }
    private static string FindAutomaticName(string folder)
    {
        for(int n=1;n<100000;n++)
        {
            string name=FilenameRules.Automatic(DateTime.Now,n);
            if(!Directory.Exists(folder)||!Directory.EnumerateFiles(folder,name+"*").Any())return name;
        }
        return "SCAN-"+DateTime.Now.ToString("yyyyMMdd-HHmmssfff");
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
