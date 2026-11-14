using System.Collections.ObjectModel;
using System.ComponentModel;
namespace DocumentScanner.Core;

/// <summary>Keeps selections stable while a bound ComboBox temporarily clears SelectedItem.</summary>
public sealed class ScanSettingsSelection : INotifyPropertyChanged
{
    private ScanSettings current=new();
    private ScannerCapabilities? capabilities;
    private bool refreshing;
    public ObservableCollection<ScanSource> Sources{get;}=[];
    public ObservableCollection<int> Dpis{get;}=[];
    public ObservableCollection<ScanColor> Colors{get;}=[];
    public ObservableCollection<string> Papers{get;}=[];
    public IReadOnlyList<string> Warnings{get;private set;}=[];
    public ScanSettings Value=>current;
    public event PropertyChangedEventHandler? PropertyChanged;
    public ScanSettingsSelection()=>ApplyCapabilities(null,current);
    // Null is a transient "no selection" from a bound control, never a request for enum value zero.
    public ScanSource? SelectedSource{get=>Sources.Contains(Source)?Source:null;set{if(value is {} selected)Source=selected;else if(!refreshing)Changed(nameof(Source));}}
    public int? SelectedDpi{get=>Dpis.Contains(Dpi)?Dpi:null;set{if(value is {} selected)Dpi=selected;else if(!refreshing)Changed(nameof(Dpi));}}
    public ScanColor? SelectedColor{get=>Colors.Contains(Color)?Color:null;set{if(value is {} selected)Color=selected;else if(!refreshing)Changed(nameof(Color));}}
    public string? SelectedPaper{get=>Papers.Contains(Paper)?Paper:null;set{if(value is {} selected)Paper=selected;else if(!refreshing)Changed(nameof(Paper));}}
    public ScanSource Source
    {
        get=>current.Source;
        set{if(!refreshing&&Sources.Contains(value)&&value!=current.Source)ApplyCapabilities(capabilities,current with{Source=value});}
    }
    public int Dpi
    {
        get=>current.Dpi;
        set{if(!refreshing&&Dpis.Contains(value)&&value!=current.Dpi){current=current with{Dpi=value};Changed(nameof(Dpi));}}
    }
    public ScanColor Color
    {
        get=>current.Color;
        set{if(!refreshing&&Colors.Contains(value)&&value!=current.Color){current=current with{Color=value};Changed(nameof(Color));}}
    }
    public string Paper
    {
        get=>current.Paper;
        set{if(!refreshing&&Papers.Contains(value)&&value!=current.Paper){current=current with{Paper=value};Changed(nameof(Paper));}}
    }
    public ScanSettings ApplyCapabilities(ScannerCapabilities? caps,ScanSettings requested)
    {
        requested.Validate();
        var warnings=new List<string>();
        var sources=caps==null?new[]{requested.Source}:caps.Known?caps.Sources.Select(s=>s.Source).Distinct().ToArray():new[]{ScanSource.Flatbed};
        var source=requested.Source;
        if(sources.Length==0)warnings.Add("The scanner reported no available paper sources.");
        else if(!sources.Contains(source))
        {
            source=sources[0];warnings.Add($"Saved paper source {requested.Source} is not available; using {source}.");
        }
        var perSource=caps?.Sources.FirstOrDefault(s=>s.Source==source);
        int[] dpis=(perSource?.Dpis??new[]{100,150,200,300,600,requested.Dpi}).Where(d=>d>=50&&d<=2400).Distinct().Order().ToArray();
        var colors=(perSource?.Colors??Enum.GetValues<ScanColor>()).Where(Enum.IsDefined).Distinct().ToArray();
        var papers=new[]{"Auto"}.Concat(perSource?.PaperSizes??new[]{"A4","A5","Letter","Legal"})
            .Where(p=>new[]{"Auto","A4","A5","Letter","Legal"}.Contains(p)).Distinct().ToArray();
        int dpi=requested.Dpi;var color=requested.Color;string paper=requested.Paper;
        if(dpis.Length==0)warnings.Add("No supported DPI values were reported. Use the native settings dialog.");
        else if(!dpis.Contains(dpi)){dpi=dpis.OrderBy(d=>Math.Abs(d-requested.Dpi)).First();warnings.Add($"Saved DPI {requested.Dpi} is not supported for {source}; using {dpi}.");}
        if(colors.Length==0)warnings.Add("No supported color modes were reported. Use the native settings dialog.");
        else if(!colors.Contains(color)){color=colors[0];warnings.Add($"Saved color mode {requested.Color} is not supported for {source}; using {color}.");}
        if(!papers.Contains(paper)){paper=papers[0];warnings.Add($"Saved paper size {requested.Paper} is not available for {source}; using {paper}.");}
        var effective=requested with{Source=source,Dpi=dpi,Color=color,Paper=paper,NativeUi=requested.NativeUi||caps is {Known:false}};
        refreshing=true;
        try
        {
            capabilities=caps;current=effective;Warnings=warnings.AsReadOnly();
            // Snapshot and effective values are committed BEFORE mutating bound collections.
            // Ignore reentrant SelectedItem=null/default writes until every list and notification is complete.
            Replace(Sources,sources);Replace(Dpis,dpis);Replace(Colors,colors);Replace(Papers,papers);
            foreach(string property in new[]{nameof(Source),nameof(Dpi),nameof(Color),nameof(Paper),nameof(Warnings),nameof(Value)})Changed(property);
        }
        finally{refreshing=false;}
        return effective;
    }
    private static void Replace<T>(ObservableCollection<T> target,IEnumerable<T> values)
    {target.Clear();foreach(var value in values)target.Add(value);}
    private void Changed(string name)
    {
        PropertyChanged?.Invoke(this,new(name));
        string? selection=name switch{nameof(Source)=>nameof(SelectedSource),nameof(Dpi)=>nameof(SelectedDpi),nameof(Color)=>nameof(SelectedColor),nameof(Paper)=>nameof(SelectedPaper),_=>null};
        if(selection!=null)PropertyChanged?.Invoke(this,new(selection));
    }
}
