using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DocumentScanner.Core;
using DocumentScanner.Infrastructure;
namespace DocumentScanner.ImagingSupport;
public static class Imaging
{
    public static BitmapSource Load(string file,int decodeWidth=0)
    {
        using var stream=File.OpenRead(file);var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;
        bitmap.StreamSource=stream;if(decodeWidth>0)bitmap.DecodePixelWidth=decodeWidth;bitmap.EndInit();bitmap.Freeze();return bitmap;
    }
    public static BitmapSource Thumbnail(BitmapSource image,int width)
    {
        double scale=Math.Min(1.0,(double)width/image.PixelWidth);
        var scaled=new TransformedBitmap(image,new ScaleTransform(scale,scale));
        // Detach the small preview from the full-resolution source graph before caching it.
        int stride=(scaled.PixelWidth*scaled.Format.BitsPerPixel+7)/8;
        byte[] pixels=new byte[stride*scaled.PixelHeight];scaled.CopyPixels(pixels,stride,0);
        var result=BitmapSource.Create(scaled.PixelWidth,scaled.PixelHeight,scaled.DpiX,scaled.DpiY,scaled.Format,scaled.Palette,pixels,stride);
        result.Freeze();return result;
    }
    public static void SavePng(BitmapSource image,string path)
    {
        using var stream=new FileStream(path,FileMode.Create,FileAccess.Write,FileShare.None);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));encoder.Save(stream);stream.Flush(true);
    }
    public static (bool Blank,Int32Rect Bounds) Analyze(BitmapSource image)
    {
        var small=Thumbnail(image,900);var rgb=new FormatConvertedBitmap(small,PixelFormats.Bgr24,null,0);
        int w=rgb.PixelWidth,h=rgb.PixelHeight,stride=(w*3+3)&~3;
        byte[] pixels=new byte[stride*h],gray=new byte[w*h];rgb.CopyPixels(pixels,stride,0);
        for(int y=0;y<h;y++)for(int x=0;x<w;x++)
        {
            int i=y*stride+x*3;
            gray[y*w+x]=(byte)((pixels[i]*29+pixels[i+1]*150+pixels[i+2]*77+128)>>8);
        }
        var analysis=ContentCropper.Analyze(gray,w,h);var b=analysis.Bounds;
        if(b==new PixelBounds(0,0,w,h))return(analysis.Blank,new(0,0,image.PixelWidth,image.PixelHeight));
        double sx=(double)image.PixelWidth/w,sy=(double)image.PixelHeight/h;
        int marginX=analysis.PaperDetected?0:(int)Math.Ceiling(image.DpiX*3/25.4),marginY=analysis.PaperDetected?0:(int)Math.Ceiling(image.DpiY*3/25.4);
        int x0=Math.Max(0,(int)(b.X*sx)-marginX),y0=Math.Max(0,(int)(b.Y*sy)-marginY);
        int x1=Math.Min(image.PixelWidth,(int)Math.Ceiling((b.X+b.Width)*sx)+marginX);
        int y1=Math.Min(image.PixelHeight,(int)Math.Ceiling((b.Y+b.Height)*sy)+marginY);
        return(analysis.Blank,new(x0,y0,x1-x0,y1-y0));
    }
    public static BitmapSource ApplyTone(BitmapSource image,int brightness,int contrast)
    {
        // Neutral means a true no-op: no conversion, gamma operation or re-encoding.
        if(brightness==0&&contrast==0)return image;
        var lookup=ImageTone.Lookup(brightness,contrast);
        var rgba=new FormatConvertedBitmap(image,PixelFormats.Bgra32,null,0);
        int stride=rgba.PixelWidth*4;byte[] pixels=new byte[stride*rgba.PixelHeight];rgba.CopyPixels(pixels,stride,0);
        for(int i=0;i<pixels.Length;i+=4)
        {
            pixels[i]=lookup[pixels[i]];pixels[i+1]=lookup[pixels[i+1]];pixels[i+2]=lookup[pixels[i+2]];
            // Alpha is retained, and the original BitmapSource/file is never modified.
        }
        var result=BitmapSource.Create(rgba.PixelWidth,rgba.PixelHeight,rgba.DpiX,rgba.DpiY,PixelFormats.Bgra32,null,pixels,stride);
        result.Freeze();return result;
    }
    public static BitmapSource ApplyAutoColor(BitmapSource image)
    {
        var rgba=new FormatConvertedBitmap(image,PixelFormats.Bgra32,null,0);
        int stride=rgba.PixelWidth*4;byte[] pixels=new byte[stride*rgba.PixelHeight];rgba.CopyPixels(pixels,stride,0);
        DocumentColorCorrection.Apply(pixels);
        var result=BitmapSource.Create(rgba.PixelWidth,rgba.PixelHeight,rgba.DpiX,rgba.DpiY,PixelFormats.Bgra32,null,pixels,stride);
        result.Freeze();return result;
    }
    public sealed record RenderedPage(BitmapSource Image,int Width,int Height,bool CropChanged);
    public static BitmapSource Render(PageData page,string folder,int previewWidth=0)=>RenderDetails(page,folder,previewWidth).Image;
    public static RenderedPage RenderDetails(PageData page,string folder,int previewWidth=0)
    {
        BitmapSource bitmap=Load(SessionStore.Resolve(folder,page.OriginalFile));bool cropChanged=false;
        if(page.ManualCrop is {} crop)
        {
            crop.Validate();
            int x=Math.Clamp((int)Math.Floor(crop.X*bitmap.PixelWidth),0,bitmap.PixelWidth-1);
            int y=Math.Clamp((int)Math.Floor(crop.Y*bitmap.PixelHeight),0,bitmap.PixelHeight-1);
            int right=Math.Clamp((int)Math.Ceiling((crop.X+crop.Width)*bitmap.PixelWidth),x+1,bitmap.PixelWidth);
            int bottom=Math.Clamp((int)Math.Ceiling((crop.Y+crop.Height)*bitmap.PixelHeight),y+1,bitmap.PixelHeight);
            cropChanged=x!=0||y!=0||right!=bitmap.PixelWidth||bottom!=bitmap.PixelHeight;
            bitmap=new CroppedBitmap(bitmap,new(x,y,right-x,bottom-y));
        }
        else if(page.AutoCrop)
        {
            var bounds=Analyze(bitmap).Bounds;
            cropChanged=bounds.Width!=bitmap.PixelWidth||bounds.Height!=bitmap.PixelHeight;
            if(cropChanged)bitmap=new CroppedBitmap(bitmap,bounds);
        }
        if(page.Rotation!=0)bitmap=new TransformedBitmap(bitmap,new RotateTransform(page.Rotation));
        if(page.AutoColorCorrection)bitmap=ApplyAutoColor(bitmap);
        bitmap=ApplyTone(bitmap,page.Brightness,page.Contrast);
        int width=bitmap.PixelWidth,height=bitmap.PixelHeight;
        if(previewWidth>0)bitmap=Thumbnail(bitmap,previewWidth);
        bitmap.Freeze();return new(bitmap,width,height,cropChanged);
    }
    public static BitmapSource FitPaper(BitmapSource image,OutputPaper paper,bool fit)
    {
        if(paper==OutputPaper.Original)return image;
        double width=paper==OutputPaper.A4?210/25.4:8.5,height=paper==OutputPaper.A4?297/25.4:11;
        if(image.Width>image.Height)(width,height)=(height,width);
        // Render at source resolution, cap at 600 dpi to keep x86 allocations bounded.
        double dpi=Math.Clamp(Math.Max(image.DpiX,image.DpiY),72,600);
        int w=(int)Math.Round(width*dpi),h=(int)Math.Round(height*dpi);
        double sourceW=image.PixelWidth/image.DpiX*dpi,sourceH=image.PixelHeight/image.DpiY*dpi;
        double scale=fit?Math.Min(w/sourceW,h/sourceH):1;
        var visual=new DrawingVisual();using(var dc=visual.RenderOpen())
        {
            // Drawing uses DIPs. RenderTargetBitmap applies DPI itself.
            dc.DrawRectangle(Brushes.White,null,new Rect(0,0,width*96,height*96));
            dc.DrawImage(image,new Rect((w-sourceW*scale)/2*96/dpi,(h-sourceH*scale)/2*96/dpi,sourceW*scale*96/dpi,sourceH*scale*96/dpi));
        }
        var result=new RenderTargetBitmap(w,h,dpi,dpi,PixelFormats.Pbgra32);result.Render(visual);result.Freeze();return result;
    }
}
