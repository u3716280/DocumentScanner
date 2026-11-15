using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Diagnostics;
using DocumentScanner.Core;
namespace DocumentScanner.App;
public sealed class SaveResultsWindow : Window
{
    public bool RetryRequested{get;private set;}
    public SaveResultsWindow(IReadOnlyList<string> local,string? localError,UploadJournal? journal,string? sendError=null)
    {
        Title="Save results";Width=900;Height=680;WindowStartupLocation=WindowStartupLocation.CenterOwner;var root=Ui.Panel();
        root.Children.Add(new TextBlock{Text="Save results",FontSize=24,FontWeight=FontWeights.SemiBold});root.Children.Add(Ui.Note("Originals and the scan session are preserved. Local and ERPNext outcomes are independent."));
        root.Children.Add(new TextBlock{Text=journal?.LocalComplete==true||journal==null?$"Local: {local.Count} files saved" : local.Count==0?"Local: not requested":"Local: partial — "+local.Count+" files",FontSize=18});if(localError!=null)root.Children.Add(Ui.Note(localError));
        foreach(string file in local)root.Children.Add(Ui.Note(file));
        if(journal!=null){root.Children.Add(new TextBlock{Text=$"ERPNext: {journal.Items.Count(i=>i.State==UploadState.Succeeded)} / {journal.Items.Count} uploaded",FontSize=18});root.Children.Add(Ui.Note($"{journal.Target.DocType} {journal.Target.Name} · {(journal.Target.Private?"Private":"Public")}"));foreach(var item in journal.Items)root.Children.Add(Ui.Note($"{Path.GetFileName(item.File)} · {item.State}\n{item.Error??item.Receipt}"));root.Children.Add(Ui.Note("Retry verifies server receipts first and skips acknowledged attachments. A recovered or retried job requires fresh destination confirmation."));}
        if(sendError!=null)root.Children.Add(Ui.Note(sendError));var actions=new WrapPanel();if(local.Count>0)actions.Children.Add(Ui.Button("Open local folder",()=>Process.Start(new ProcessStartInfo(Path.GetDirectoryName(local[0])!){UseShellExecute=true})));
        if(journal!=null&&DeliveryStatus.HasRetryableWork(journal))actions.Children.Add(Ui.Button(journal!.NeedsLocal&&!journal.LocalComplete?"Retry local save, then upload":"Retry unresolved uploads",()=>{RetryRequested=true;DialogResult=true;},true));actions.Children.Add(Ui.Button("Back to document",Close));root.Children.Add(actions);Content=new ScrollViewer{Content=root,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    }
}
