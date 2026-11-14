using System.ComponentModel;
namespace DocumentScanner.Core;

/// <summary>The exact integer values used by both controls, profiles, and new pages.</summary>
public sealed class ImageAdjustments : INotifyPropertyChanged
{
    private int brightness,contrast;
    public event PropertyChangedEventHandler? PropertyChanged;
    public int Brightness{get=>brightness;set=>Set(ref brightness,value,nameof(Brightness));}
    public int Contrast{get=>contrast;set=>Set(ref contrast,value,nameof(Contrast));}
    public void Reset(){Brightness=0;Contrast=0;}
    private void Set(ref int field,int value,string name)
    {
        value=Math.Clamp(value,-1000,1000);if(field==value)return;field=value;PropertyChanged?.Invoke(this,new(name));
    }
}

/// <summary>A stateless channel lookup. Every render starts from the immutable original.</summary>
public static class ImageTone
{
    public static byte[] Lookup(int brightness,int contrast)
    {
        if(brightness is < -1000 or >1000 || contrast is < -1000 or >1000)throw new ArgumentOutOfRangeException(nameof(brightness));
        var map=new byte[256];double gain=Math.Exp(contrast/500.0),offset=brightness*255/1000.0;
        for(int value=0;value<256;value++)
        {
            double shifted=Math.Clamp(value+offset,0,255);
            map[value]=(byte)Math.Clamp((int)Math.Round(127.5+(shifted-127.5)*gain,MidpointRounding.AwayFromZero),0,255);
        }
        return map;
    }
}
