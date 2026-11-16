using System.IO;
namespace DocumentScanner.Core;
public enum ScanSource { Flatbed, Feeder, Duplex }
public enum ScanColor { Color, Grayscale, BlackAndWhite }
public enum SessionState { Idle, Initializing, Scanning, Processing, Completed, Cancelled, Error }
public enum ExportFormat { Pdf, Jpg, Png }
public enum OutputPaper { Original, A4, Letter }
public sealed record DeviceInfo(string Key, string Name, string Driver, string Id, bool Twain64 = false)
{
    public string DisplayName => $"{Name} · {Driver}{(Twain64 ? " 64-bit" : "")}";
}
public sealed record SourceCapabilities(ScanSource Source, int[]? Dpis, ScanColor[]? Colors, string[]? PaperSizes);
public sealed record ScannerCapabilities(SourceCapabilities[] Sources, bool Known, string? Warning = null);
public sealed record ScanSettings
{
    public ScanSource Source { get; init; } = ScanSource.Flatbed;
    public int Dpi { get; init; } = 300;
    public ScanColor Color { get; init; } = ScanColor.Color;
    public string Paper { get; init; } = "A4";
    public bool Landscape { get; init; }
    public bool Deskew { get; init; } = true;
    public bool AutoCrop { get; init; }
    public bool DetectBlank { get; init; }
    public bool RemoveBlank { get; init; }
    public bool NativeUi { get; init; }
    public int Brightness { get; init; }
    public int Contrast { get; init; }
    public void Validate()
    {
        if (Dpi is < 50 or > 2400) throw new ArgumentException("Invalid resolution.");
        if (Brightness is < -1000 or > 1000 || Contrast is < -1000 or > 1000) throw new ArgumentException("Invalid image adjustment.");
        if (!new[]{"Auto","A4","A5","Letter","Legal"}.Contains(Paper)) throw new ArgumentException("Invalid paper size.");
        if (!Enum.IsDefined(Source) || !Enum.IsDefined(Color)) throw new ArgumentException("Invalid scanner settings.");
    }
}
public sealed record ScanProfile(string Name, DeviceInfo Device, ScanSettings Settings)
{
    public int SchemaVersion{get;init;}=2;
    public Guid Id{get;init;}=Guid.NewGuid();
    public OutputDefaults Output{get;init;}=new();
}
public sealed record AppSettings
{
    public const int CurrentSchema = 2;
    public int SchemaVersion { get; init; } = CurrentSchema;
    public Guid? DefaultProfileId { get; init; }
    public OutputDefaults Output { get; init; } = new();
    /// <summary>ERPNext sites this user allows scan links from. Credentials are not stored here.</summary>
    public List<ErpSite> Sites { get; init; } = [];
    /// <summary>Suggested descriptions for attachment file names.</summary>
    public List<string> AttachmentDescriptions { get; init; } = ["ใบกำกับภาษี", "ใบเสร็จรับเงิน", "ใบส่งของ", "ใบแจ้งหนี้"];
    public string? LastDeviceKey { get; init; }
    public string? DefaultDeviceKey { get; init; }
    public string OutputFolder { get; init; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Scanned Documents");
    public ScanSettings Scan { get; init; } = new();
    public ExportFormat Format { get; init; } = ExportFormat.Pdf;
    public int JpegQuality { get; init; } = 90;
    public string Language { get; init; } = "en";
    public List<ScanProfile> Profiles { get; init; } = [];
    public List<DeviceInfo> ManualDevices { get; init; } = [];
    public void Validate()
    {
        if (Scan is null || Profiles is null || ManualDevices is null || Sites is null || Output is null || AttachmentDescriptions is null) throw new ArgumentException("Configuration sections cannot be null.");
        if (SchemaVersion != CurrentSchema || JpegQuality is <60 or >100 || !Enum.IsDefined(Format)) throw new ArgumentException("Invalid configuration version or export quality.");
        Scan.Validate();
        ArgumentException.ThrowIfNullOrWhiteSpace(OutputFolder);
        Output.Validate();foreach(var profile in Profiles) ProfileValidation.Validate(profile);foreach(var site in Sites)site.Validate();
        if(Sites.Select(s=>s.Id).Distinct().Count()!=Sites.Count)throw new ArgumentException("Duplicate ERPNext site identities.");
        if(AttachmentDescriptions.Count>50||AttachmentDescriptions.Any(d=>string.IsNullOrWhiteSpace(d)||d.Length>AttachmentNaming.MaxDescription))throw new ArgumentException("Invalid attachment description list.");
        if(Profiles.Select(p=>p.Id).Distinct().Count()!=Profiles.Count||Profiles.Select(p=>p.Name.ToUpperInvariant()).Distinct().Count()!=Profiles.Count)throw new ArgumentException("Duplicate profile identities.");
    }
}
public sealed record PageData
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string OriginalFile { get; init; }
    public required string ThumbnailFile { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public double DpiX { get; init; } = 300;
    public double DpiY { get; init; } = 300;
    public int Rotation { get; init; }
    public bool AutoCrop { get; init; }
    public NormalizedCrop? ManualCrop { get; init; }
    public bool AutoColorCorrection { get; init; }
    public bool BlankCandidate { get; init; }
    public int Brightness { get; init; }
    public int Contrast { get; init; }
}
public sealed record SessionManifest(int SchemaVersion, Guid Id, DateTimeOffset Updated, List<PageData> Pages);
public sealed record ExportOptions(string Folder, string BaseName, ExportFormat Format, int Quality = 90,
    OutputPaper Paper = OutputPaper.Original, bool Fit = true)
{
    public void Validate()
    {
        FilenameRules.Validate(BaseName);
        ArgumentException.ThrowIfNullOrWhiteSpace(Folder);
        if (!Path.IsPathFullyQualified(Folder)) throw new ArgumentException("Choose an absolute output folder.");
        if (Quality is <60 or >100 || !Enum.IsDefined(Format) || !Enum.IsDefined(Paper)) throw new ArgumentException("Invalid export settings.");
    }
}
public sealed record ExportResult(IReadOnlyList<string> Saved, string? Error, bool Cancelled = false);
public interface IScannerService : IDisposable
{
    Task<(IReadOnlyList<DeviceInfo> Devices, IReadOnlyList<string> Warnings)> DiscoverAsync(CancellationToken ct);
    Task<DeviceInfo?> FindDeviceAsync(DeviceInfo saved, CancellationToken ct);
    Task<ScannerCapabilities> GetCapabilitiesAsync(DeviceInfo device, CancellationToken ct);
    Task<DeviceInfo> ConnectEndpointAsync(string endpoint, CancellationToken ct);
    Task ScanAsync(DeviceInfo device, ScanSettings settings, string sessionFolder, Func<PageData,Task> acquired,
        nint windowHandle, CancellationToken ct);
}
public interface IDocumentExporter
{
    Task<ExportResult> ExportAsync(IReadOnlyList<PageData> pages, string sessionFolder, ExportOptions options,
        IProgress<int>? progress, CancellationToken ct);
}
