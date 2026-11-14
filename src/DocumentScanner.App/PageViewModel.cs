using System.IO;
using DocumentScanner.Core;
using DocumentScanner.Infrastructure;
using DocumentScanner.ImagingSupport;
using System.Windows.Media.Imaging;
namespace DocumentScanner.App;
public sealed class PageViewModel(PageData data,int number,string folder) : Bindable
{
    // Bound the number of full-resolution images used for cropped thumbnail generation.
    private static readonly SemaphoreSlim thumbnailGate=new(1,1);
    private BitmapSource? thumbnail;private bool loading;
    public PageData Data=>data;
    public int Rotation=>data.Rotation;
    public string Label=>$"{number:000}";
    public string BlankText=>data.BlankCandidate?Text.Get("BlankLabel"):"";
    public BitmapSource? Thumbnail
    {
        get{if(!loading){loading=true;_=LoadAsync();}return thumbnail;}
    }
    private async Task LoadAsync()
    {
        await thumbnailGate.WaitAsync();
        try
        {
            thumbnail=await StaTask.Run(()=>Task.FromResult(data.AutoCrop||data.ManualCrop!=null||data.AutoColorCorrection||data.Brightness!=0||data.Contrast!=0
                ?Imaging.Render(data with{Rotation=0},folder,180)
                :Imaging.Load(SessionStore.Resolve(folder,data.ThumbnailFile),180)));
            Changed(nameof(Thumbnail));
        }
        catch(Exception ex) when(ex is IOException or NotSupportedException or System.FormatException){}
        finally{thumbnailGate.Release();}
    }
}
