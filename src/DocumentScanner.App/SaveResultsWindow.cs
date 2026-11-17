using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DocumentScanner.Core;
using DocumentScanner.Infrastructure;
namespace DocumentScanner.App;

/// <summary>Shown before anything is sent: where it goes, as whom, and the final file names.</summary>
public sealed class ConfirmSendWindow : Window
{
    public ConfirmSendWindow(DeliveryJournal journal,int pages,long limit,IReadOnlyCollection<DeliveryItem> renamed)
    {
        var target=journal.Destination??throw new ArgumentException("A destination is required.");
        string format=journal.Format.ToString().ToUpperInvariant();int files=journal.Items.Count;
        Title="Confirm sending";Width=640;Height=620;MinHeight=420;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var p=Ui.Panel();
        p.Children.Add(new TextBlock{Text=journal.Format==ExportFormat.Pdf?$"Send 1 PDF file, {pages} {(pages==1?"page":"pages")}":$"Send {files} {format} {(files==1?"file":"files")}",FontSize=22,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap});
        p.Children.Add(Ui.Heading("Destination"));
        p.Children.Add(Ui.Line(target.DocType));p.Children.Add(Ui.Line(target.DocName,true));
        if(target.Party.Length>0)p.Children.Add(Ui.Line(target.Party));
        p.Children.Add(Ui.Note($"{DocStatusRules.Label(target.DocStatus)}\n{target.SiteOrigin}\nSending as {target.Account}"));
        p.Children.Add(Ui.Heading(files==1?"File":"Files"));
        foreach(var item in journal.Items)
        {
            bool changed=renamed.Contains(item),tooLarge=item.Size>limit;
            p.Children.Add(Ui.Line($"{item.Name}  ({UploadRunner.Megabytes(item.Size)})"));
            if(changed)p.Children.Add(Ui.Note("A file with the first name already exists, so a number was added. Nothing will be overwritten."));
            if(tooLarge)p.Children.Add(Ui.Warning($"Larger than this site accepts ({UploadRunner.Megabytes(limit)}). This file will not be sent."));
        }
        p.Children.Add(Ui.Note("Attachment: Private"+(journal.NeedsLocal?$"\nSaved first to: {journal.LocalFolder}":"")));
        var actions=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right};
        actions.Children.Add(Ui.Button("Cancel",()=>DialogResult=false));
        actions.Children.Add(Ui.Button(journal.NeedsLocal?"Save, then send":"Confirm and send",()=>DialogResult=true,true));
        p.Children.Add(actions);
        Content=new ScrollViewer{Content=p,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    }
}

/// <summary>The outcome per file, with the folder and ERPNext reported separately.</summary>
public sealed class SaveResultsWindow : Window
{
    public bool RetryRequested{get;private set;}

    /// <summary>A plain local export that was not started from ERPNext.</summary>
    public SaveResultsWindow(IReadOnlyList<string> local,string? error)
    {
        var root=Start(error==null&&local.Count>0?"Saved":"Not completed");
        root.Children.Add(Ui.Heading("Folder"));
        foreach(string file in local)root.Children.Add(Ui.Line("✓ "+Path.GetFileName(file)));
        if(local.Count==0)root.Children.Add(Ui.Note("No file was saved."));
        if(error!=null)root.Children.Add(Ui.Warning(error));
        Finish(root,local.Count>0?Path.GetDirectoryName(local[0]):null,null,false);
    }

    public SaveResultsWindow(DeliveryJournal journal,string? error)
    {
        var outcome=DeliveryStatus.Outcome(journal);
        var root=Start(DeliveryStatus.Text(outcome));
        if(journal.NeedsLocal)
        {
            root.Children.Add(Ui.Heading("Folder"));
            foreach(var item in journal.Items)root.Children.Add(Ui.Line((item.LocalDone?"✓ ":"✗ ")+item.Name));
            if(journal.LocalError!=null)root.Children.Add(Ui.Warning(journal.LocalError));
            else if(journal.LocalComplete)root.Children.Add(Ui.Note(journal.LocalFolder??""));
        }
        if(journal.NeedsUpload)
        {
            var target=journal.Destination!;
            root.Children.Add(Ui.Heading("ERPNext"));
            root.Children.Add(Ui.Note($"{target.DocType} {target.DocName}"));
            bool waiting=DeliveryStatus.LocalOpen(journal);
            foreach(var item in journal.Items)
            {
                string mark=item.State switch{UploadState.Succeeded=>"✓ ",UploadState.Unknown=>"? ",UploadState.Failed=>"✗ ",_=>"– "};
                root.Children.Add(Ui.Line(mark+item.Name));
                if(item.State==UploadState.Succeeded&&item.ServerFileName is {Length:>0} stored&&!string.Equals(stored,item.Name,StringComparison.Ordinal))root.Children.Add(Ui.Note($"Stored in ERPNext as {stored}"));
                if(item.Error!=null)root.Children.Add(item.State==UploadState.Unknown?Ui.Note(item.Error):Ui.Warning(item.Error));
                else if(item.State==UploadState.Pending)root.Children.Add(Ui.Note(waiting?"Not sent: the folder save has to finish first.":"Not sent yet."));
            }
        }
        if(error!=null)root.Children.Add(Ui.Warning(error));
        if(outcome!=DeliveryOutcome.Completed)root.Children.Add(Ui.Note("Your scanned pages are kept."+(DeliveryStatus.HasRetryableWork(journal)?" Retry sends only what is missing, and checks ERPNext first.":"")));
        string? folder=journal.Items.FirstOrDefault(i=>i.LocalDone)?.LocalDestination is {} saved?Path.GetDirectoryName(saved):null;
        Finish(root,folder,journal.Destination?.DocumentUrl,DeliveryStatus.HasRetryableWork(journal));
    }

    private StackPanel Start(string headline)
    {
        Title="Result";Width=680;Height=620;MinHeight=360;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var root=Ui.Panel();root.Children.Add(new TextBlock{Text=headline,FontSize=22,FontWeight=FontWeights.SemiBold});return root;
    }
    private void Finish(StackPanel root,string? folder,string? documentUrl,bool retry)
    {
        var actions=new WrapPanel{Margin=new(0,12,0,0)};
        if(retry)actions.Children.Add(Ui.Button("Retry",()=>{RetryRequested=true;DialogResult=true;},true));
        if(folder!=null)actions.Children.Add(Ui.Button("Open folder",()=>Process.Start(new ProcessStartInfo(folder){UseShellExecute=true})));
        if(documentUrl!=null)actions.Children.Add(Ui.Button("Open document in ERPNext",()=>Process.Start(new ProcessStartInfo(documentUrl){UseShellExecute=true})));
        actions.Children.Add(Ui.Button("Close",Close));root.Children.Add(actions);
        Content=new ScrollViewer{Content=root,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    }
}
