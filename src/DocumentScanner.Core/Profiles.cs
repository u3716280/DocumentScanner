using System.Globalization;
namespace DocumentScanner.Core;
public sealed record OutputDefaults
{
    public ExportFormat Format{get;init;}=ExportFormat.Pdf;
    public int JpegQuality{get;init;}=90;
    public string Folder{get;init;}=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"Scanned Documents");
    public string Pattern{get;init;}="{prefix}-{date:yyyyMMdd}-{sequence:0000}";
    public string Prefix{get;init;}="SCAN";
    public void Validate(){if(!Enum.IsDefined(Format)||JpegQuality is <60 or >100)throw new ArgumentException("Invalid export settings.");ArgumentException.ThrowIfNullOrWhiteSpace(Folder);OutputNaming.Resolve(this,DateTime.Today,1);}
}
public static class OutputNaming
{
    public static string Resolve(OutputDefaults output,DateTime date,int sequence)
    {
        if(sequence<1)throw new ArgumentException("Invalid sequence.");
        if(output.Prefix.Any(c=>c<32||"<>:\"/\\|?*{}".Contains(c)))throw new ArgumentException("Invalid filename prefix.");
        string pattern=output.Pattern;
        if(pattern.Split("{prefix}").Length>2)throw new ArgumentException("Use the prefix token once.");
        if(output.Prefix.Length>0&&pattern.Contains("{prefix}")&&!pattern.StartsWith("{prefix}",StringComparison.Ordinal))throw new ArgumentException("The filename prefix token must be at the start of the pattern.");
        if(!pattern.Contains("{prefix}")&&output.Prefix.Length>0)pattern="{prefix}-"+pattern;
        string name=pattern.Replace("{prefix}",output.Prefix).Replace("{date:yyyyMMdd}",date.ToString("yyyyMMdd",CultureInfo.InvariantCulture)).Replace("{sequence:0000}",sequence.ToString("0000",CultureInfo.InvariantCulture));
        if(name.Contains('{')||name.Contains('}'))throw new ArgumentException("Supported tokens: {prefix}, {date:yyyyMMdd}, {sequence:0000}.");
        if(output.Prefix.Length==0)name=name.Trim('-');FilenameRules.Validate(name);return name;
    }
}
public sealed record ErpDefaults
{
    public Guid? SiteId{get;init;}
    public string DocType{get;init;}="Sales Order";
    public bool Private{get;init;}=true;
}
public static class ProfileValidation
{
    public static void Validate(ScanProfile p)
    {
        if(p.SchemaVersion!=2)throw new ArgumentException("Unsupported profile schema. This app supports version 2.");
        if(p.Id==Guid.Empty||string.IsNullOrWhiteSpace(p.Name)||p.Name.Length>80||p.Device==null||p.Settings==null||p.Output==null||p.Erp==null)throw new ArgumentException("Invalid profile.");
        p.Settings.Validate();p.Output.Validate();ArgumentException.ThrowIfNullOrWhiteSpace(p.Device.Id);ArgumentException.ThrowIfNullOrWhiteSpace(p.Erp.DocType);
    }
    public static IReadOnlyList<string> CapabilityIssues(ScanSettings s,ScannerCapabilities? caps)
    {
        var issues=new List<string>();
        if(caps?.Known!=true){issues.Add("Scanner capabilities are unknown. Explicitly use the native scanner dialog or cancel.");return issues;}
        var source=caps.Sources.FirstOrDefault(c=>c.Source==s.Source);
        if(source==null){issues.Add($"Paper source {s.Source} is not supported.");return issues;}
        if(source.Dpis==null)issues.Add("DPI capabilities are unknown.");else if(!source.Dpis.Contains(s.Dpi))issues.Add($"Saved {s.Dpi} DPI is unsupported. Available: {string.Join(", ",source.Dpis)}.");
        if(source.Colors==null)issues.Add("Color capabilities are unknown.");else if(!source.Colors.Contains(s.Color))issues.Add($"Saved color {s.Color} is unsupported. Available: {string.Join(", ",source.Colors)}.");
        if(source.PaperSizes==null)issues.Add("Paper size capabilities are unknown.");else if(s.Paper!="Auto"&&!source.PaperSizes.Contains(s.Paper))issues.Add($"Saved paper {s.Paper} is unsupported. Available: {string.Join(", ",source.PaperSizes)}.");
        return issues;
    }
    public static bool OnlyUnknown(IReadOnlyList<string> issues)=>issues.Count>0&&issues.All(x=>x.Contains("unknown",StringComparison.OrdinalIgnoreCase));
}
public sealed record ProfilePackage(int SchemaVersion,List<ScanProfile> Profiles)
{
    public void Validate(){if(SchemaVersion!=2||Profiles==null||Profiles.Count==0)throw new ArgumentException("Unsupported or empty profile package.");foreach(var p in Profiles)ProfileValidation.Validate(p);if(Profiles.Select(p=>p.Id).Distinct().Count()!=Profiles.Count||Profiles.Select(p=>p.Name.ToUpperInvariant()).Distinct().Count()!=Profiles.Count)throw new ArgumentException("Duplicate profile IDs/names in import.");}
}
