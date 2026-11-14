using System.IO;
using Microsoft.Extensions.Logging;
using DocumentScanner.Core;
using DocumentScanner.Infrastructure;
using DocumentScanner.ImagingSupport;
using NAPS2.Images;
using NAPS2.Images.Wpf;
using NAPS2.ImportExport;
using NAPS2.Pdf;
using NAPS2.Scan;
using System.Windows.Media.Imaging;
namespace DocumentScanner.Export;
public sealed class DocumentExporter(ILogger<DocumentExporter>? logger = null) : IDocumentExporter
{
    public async Task<ExportResult> ExportAsync(IReadOnlyList<PageData> pages,string sessionFolder,ExportOptions options,IProgress<int>? progress,CancellationToken ct)
    {
        options.Validate();if(pages.Count==0)throw new ArgumentException("There are no pages to export.");
        Directory.CreateDirectory(options.Folder);var saved=new List<string>();
        try {
            if(options.Format==ExportFormat.Pdf)
            {
                var path=await ExportPdf(pages,sessionFolder,options,progress,ct);saved.Add(path);
            }
            else for(int i=0;i<pages.Count;i++)
            {
                ct.ThrowIfCancellationRequested();var image=Imaging.Render(pages[i],sessionFolder);
                string temp=SafeOutput.Temporary(options.Folder,options.Format==ExportFormat.Jpg?".jpg":".png");
                try {
                    if(options.Format==ExportFormat.Png)Imaging.SavePng(image,temp);
                    else {
                        using var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None);
                        var encoder=new JpegBitmapEncoder{QualityLevel=options.Quality};encoder.Frames.Add(BitmapFrame.Create(image));encoder.Save(stream);stream.Flush(true);
                    }
                    var checkedImage=Imaging.Load(temp);
                    if(checkedImage.PixelWidth!=image.PixelWidth||checkedImage.PixelHeight!=image.PixelHeight)throw new InvalidDataException("Image validation failed.");
                    ct.ThrowIfCancellationRequested();saved.Add(SafeOutput.Finalize(temp,Path.Combine(options.Folder,FilenameRules.PageName(options.BaseName,i+1,options.Format))));
                } finally {if(File.Exists(temp))File.Delete(temp);}
                progress?.Report(i+1);
            }
            return new(saved,null);
        } catch(OperationCanceledException){return new(saved,null,true);}
        catch(Exception ex) when(ex is not OutOfMemoryException)
        { logger?.LogError(ex,"Document export failed"); return new(saved,ex is UnauthorizedAccessException?"The output folder is not writable.":"Export failed. Check free disk space and folder access. Your session is preserved."); }
    }
    private static async Task<string> ExportPdf(IReadOnlyList<PageData> pages,string folder,ExportOptions options,IProgress<int>? progress,CancellationToken ct)
    {
        string work=Path.Combine(folder,"pdf-work",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(work);
        string temporary=SafeOutput.Temporary(options.Folder,".pdf");
        var processed=new List<ProcessedImage>();
        using var context=new ScanningContext(new WpfImageContext()){FileStorageManager=FileStorageManager.CreateFolder(Path.Combine(work,"cache"))};
        try {
            var importer=new ImageImporter(context);
            for(int i=0;i<pages.Count;i++)
            {
                ct.ThrowIfCancellationRequested();
                string file=Path.Combine(work,$"{i:000000}.png");
                var image=Imaging.FitPaper(Imaging.Render(pages[i],folder),options.Paper,options.Fit);
                Imaging.SavePng(image,file);
                await foreach(var imported in importer.Import(file))processed.Add(imported);
                File.Delete(file);progress?.Report(i+1);
            }
            ct.ThrowIfCancellationRequested();
            // The SDK export may not interrupt immediately. Do not finalize if cancellation was requested.
            await new PdfExporter(context).Export(temporary,processed);
            using(var pdf=UglyToad.PdfPig.PdfDocument.Open(temporary))
            {
                if(pdf.NumberOfPages!=pages.Count)throw new InvalidDataException("PDF page count mismatch.");
                for(int i=1;i<=pdf.NumberOfPages;i++)
                {
                    var page=pdf.GetPage(i);if(page.Width<=0||page.Height<=0)throw new InvalidDataException("Invalid PDF page dimensions.");
                }
            }
            ct.ThrowIfCancellationRequested();
            return SafeOutput.Finalize(temporary,Path.Combine(options.Folder,options.BaseName+".pdf"));
        }
        finally {
            foreach(var image in processed)image.Dispose();context.Dispose();
            if(File.Exists(temporary))File.Delete(temporary);
            if(Directory.Exists(work))Directory.Delete(work,true);
        }
    }
}
