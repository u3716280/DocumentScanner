namespace DocumentScanner.Core;
public static class DocumentColorCorrection
{
    // Conservative document white balance + levels. Never darken paper or amplify a channel > 1.6x.
    // Fully transparent pixels do not influence the estimate and alpha is retained.
    public static void Apply(Span<byte> bgra)
    {
        if(bgra.Length%4!=0)throw new ArgumentException("Expected BGRA pixels.");
        var hist=new int[3,256];int count=0;
        for(int i=0;i<bgra.Length;i+=4){if(bgra[i+3]==0)continue;count++;for(int c=0;c<3;c++)hist[c,bgra[i+c]]++;}
        if(count==0)return;
        var maps=new byte[3,256];
        for(int c=0;c<3;c++)
        {
            int white=255,black=0,sum=0;
            for(int v=0;v<256;v++){sum+=hist[c,v];if(sum>=Math.Ceiling(count*0.95)){white=v;break;}}
            sum=0;for(int v=0;v<256;v++){sum+=hist[c,v];if(sum>=Math.Ceiling(count*0.005)){black=Math.Min(v,24);break;}}
            // Dark/full-bleed pages are ambiguous: leave them alone instead of bleaching them.
            if(white<160||white-black<64){for(int v=0;v<256;v++)maps[c,v]=(byte)v;continue;}
            double gain=Math.Min(1.6,255.0/(white-black));
            for(int v=0;v<256;v++)maps[c,v]=(byte)Math.Clamp((int)Math.Round((v-black)*gain),0,255);
        }
        for(int i=0;i<bgra.Length;i+=4){if(bgra[i+3]==0)continue;for(int c=0;c<3;c++)bgra[i+c]=maps[c,bgra[i+c]];}
    }
}
