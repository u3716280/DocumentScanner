using System.IO;
namespace DocumentScanner.Core;
public sealed class DocumentSession
{
    private List<PageData> pages = [];
    private readonly Stack<List<PageData>> undo = new();
    public Guid Id { get; }
    public IReadOnlyList<PageData> Pages => pages.AsReadOnly();
    public bool CanUndo => undo.Count > 0;
    public DocumentSession(Guid? id = null) => Id = id ?? Guid.NewGuid();
    public void Restore(IEnumerable<PageData> restored) { pages = restored.ToList(); undo.Clear(); }
    private void Checkpoint()
    {
        if (undo.Count >= 50)
        {
            var keep = undo.Take(49).Reverse().ToArray(); undo.Clear();
            foreach(var item in keep) undo.Push(item);
        }
        undo.Push([..pages]);
    }
    // Acquisition preserves editing history without allowing Undo to remove newly acquired pages.
    public void Append(PageData page)
    {
        if (pages.Any(p => p.Id == page.Id)) throw new ArgumentException("Duplicate page id.");
        pages.Add(page);
        foreach(var snapshot in undo) snapshot.Add(page);
    }
    public void Delete(IEnumerable<Guid> ids) { var set=ids.ToHashSet(); if(!pages.Any(p=>set.Contains(p.Id)))return; Checkpoint(); pages.RemoveAll(p=>set.Contains(p.Id)); }
    public void Rotate(IEnumerable<Guid> ids, int degrees)
    {
        if (degrees % 90 != 0) throw new ArgumentException("Rotation must be a multiple of 90.");
        var set=ids.ToHashSet(); if(!pages.Any(p=>set.Contains(p.Id)))return; Checkpoint();
        pages=pages.Select(p=>set.Contains(p.Id)?p with { Rotation=((p.Rotation+degrees)%360+360)%360 }:p).ToList();
    }
    public void SetCrop(IEnumerable<Guid> ids, bool crop) { var set=ids.ToHashSet(); if(!pages.Any(p=>set.Contains(p.Id)&&(p.AutoCrop!=crop||p.ManualCrop!=null)))return; Checkpoint(); pages=pages.Select(p=>set.Contains(p.Id)?p with {AutoCrop=crop,ManualCrop=null}:p).ToList(); }
    public void SetManualCrop(Guid id, NormalizedCrop crop)
    {
        crop.Validate();if(!pages.Any(p=>p.Id==id))return;Checkpoint();
        pages=pages.Select(p=>p.Id==id?p with{ManualCrop=crop,AutoCrop=false}:p).ToList();
    }
    public void SetTone(IEnumerable<Guid> ids,int brightness,int contrast)
    {
        new ScanSettings{Brightness=brightness,Contrast=contrast}.Validate();var set=ids.ToHashSet();if(!pages.Any(p=>set.Contains(p.Id)&&(p.Brightness!=brightness||p.Contrast!=contrast)))return;Checkpoint();pages=pages.Select(p=>set.Contains(p.Id)?p with{Brightness=brightness,Contrast=contrast}:p).ToList();
    }
    public void SetAutoColor(IEnumerable<Guid> ids,bool enabled)
    {
        var set=ids.ToHashSet();if(!pages.Any(p=>set.Contains(p.Id)&&p.AutoColorCorrection!=enabled))return;
        Checkpoint();pages=pages.Select(p=>set.Contains(p.Id)?p with{AutoColorCorrection=enabled}:p).ToList();
    }
    public void Move(IEnumerable<Guid> ids, int targetIndex)
    {
        var set=ids.ToHashSet(); var moving=pages.Where(p=>set.Contains(p.Id)).ToList();
        if(moving.Count==0)return;
        targetIndex=Math.Clamp(targetIndex,0,pages.Count);
        int removedBefore=pages.Take(targetIndex).Count(p=>set.Contains(p.Id));
        Checkpoint(); pages.RemoveAll(p=>set.Contains(p.Id)); pages.InsertRange(targetIndex-removedBefore,moving);
    }
    public void Clear() { if(pages.Count==0)return; Checkpoint(); pages=[]; }
    public void Undo() { if(undo.TryPop(out var previous)) pages=previous; }
}
public sealed class SessionStateMachine
{
    public SessionState State { get; private set; } = SessionState.Idle;
    public void MoveTo(SessionState next)
    {
        bool valid = State switch {
            SessionState.Idle or SessionState.Completed or SessionState.Cancelled or SessionState.Error => next == SessionState.Initializing,
            SessionState.Initializing => next is SessionState.Scanning or SessionState.Error or SessionState.Cancelled,
            SessionState.Scanning => next is SessionState.Processing or SessionState.Completed or SessionState.Cancelled or SessionState.Error,
            SessionState.Processing => next is SessionState.Scanning or SessionState.Completed or SessionState.Cancelled or SessionState.Error,
            _ => false
        };
        if(!valid)throw new InvalidOperationException($"Invalid state transition: {State} to {next}.");
        State=next;
    }
}
public static class FilenameRules
{
    public static void Validate(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if(name.Length>120 || name.EndsWith('.') || name.EndsWith(' ') || name.Any(c=> c<32 || "<>:\"/\\|?*".Contains(c)))
            throw new ArgumentException("Invalid filename. Do not include a path or reserved characters.");
        string stem=name.Split('.')[0].ToUpperInvariant();
        if(new[]{"CON","PRN","AUX","NUL","CONIN$","CONOUT$"}.Contains(stem) ||
            (stem.Length==4 && (stem.StartsWith("COM")||stem.StartsWith("LPT")) && ("123456789¹²³".Contains(stem[3]))))
            throw new ArgumentException("This filename is reserved by Windows.");
    }
    public static string Automatic(DateTime date, int number) => $"SCAN-{date:yyyyMMdd}-{number:0000}";
    public static string PageName(string basename, int page, ExportFormat format) => $"{basename}-{page:000}.{format.ToString().ToLowerInvariant()}";
}
