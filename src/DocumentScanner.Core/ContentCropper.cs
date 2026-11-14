namespace DocumentScanner.Core;

public readonly record struct PixelBounds(int X,int Y,int Width,int Height);
public readonly record struct CropAnalysis(bool Blank,PixelBounds Bounds,bool PaperDetected=false);

/// <summary>Conservative, reversible document/content bounds from an 8-bit grayscale sample.</summary>
public static class ContentCropper
{
    public static CropAnalysis Analyze(ReadOnlySpan<byte> gray,int width,int height)
    {
        int count=checked(width*height);
        if(width<1||height<1||gray.Length!=count)throw new ArgumentException("Invalid grayscale sample dimensions.");
        var full=new PixelBounds(0,0,width,height);
        var histogram=new int[256];var edgeHistogram=new int[256];int edgeCount=0;
        int edge=Math.Max(1,Math.Min(width,height)/50);
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)
        {
            byte value=gray[y*width+x];histogram[value]++;
            if(x<edge||x>=width-edge||y<edge||y>=height-edge){edgeHistogram[value]++;edgeCount++;}
        }
        int background=Percentile(histogram,count,0.85),border=Percentile(edgeHistogram,edgeCount,0.5);
        // A white document on a dark platen needs paper detection, rather than treating the platen as ink.
        if(border<130&&background>160)
        {
            var light=new bool[count];int threshold=Math.Max(140,border+45);
            for(int i=0;i<count;i++)light[i]=gray[i]>=threshold;
            Component largest=default;
            Components(light,width,height,c=>{if(c.Area>largest.Area)largest=c;});
            long rectangleArea=(long)largest.Bounds.Width*largest.Bounds.Height;
            if(largest.Area>=count*0.05&&rectangleArea<count*0.92)
                return new(false,largest.Bounds,true);
        }
        // Adapt to off-white/gray paper. A fixed "almost white" threshold mistakes the whole background for content.
        int inkThreshold=Math.Clamp(background-22,35,235);
        var ink=new bool[count];
        for(int i=0;i<count;i++)ink[i]=gray[i]<inkThreshold;
        int left=width,top=height,right=-1,bottom=-1,accepted=0;
        int minimumArea=Math.Max(3,count/250000);
        Components(ink,width,height,c=>{
            if(c.Area<minimumArea)return;
            var b=c.Bounds;
            if(c.Area<12&&b.Width<=3&&b.Height<=3)return;
            bool touchesX=b.X==0||b.X+b.Width==width,touchesY=b.Y==0||b.Y+b.Height==height;
            // Ignore long connected scanner-edge strips/frames, but retain normal content near page edges.
            bool edgeStrip=(touchesX&&b.Height>=height*0.9&&b.Width<=width*0.18)
                ||(touchesY&&b.Width>=width*0.9&&b.Height<=height*0.18);
            bool thinFrame=touchesX&&touchesY&&b.Width>=width*0.95&&b.Height>=height*0.95&&c.Area<count*0.12;
            if(edgeStrip||thinFrame)return;
            accepted+=c.Area;left=Math.Min(left,b.X);top=Math.Min(top,b.Y);
            right=Math.Max(right,b.X+b.Width);bottom=Math.Max(bottom,b.Y+b.Height);
        });
        if(right<0)return new(true,full); // No evidence of content: preserve the full original.
        return new(false,new(left,top,right-left,bottom-top));
    }
    private static int Percentile(int[] histogram,int count,double fraction)
    {
        int target=Math.Max(1,(int)Math.Ceiling(count*fraction)),sum=0;
        for(int i=0;i<histogram.Length;i++){sum+=histogram[i];if(sum>=target)return i;}
        return 255;
    }
    private readonly record struct Component(int Area,PixelBounds Bounds);
    private static void Components(bool[] mask,int width,int height,Action<Component> visit)
    {
        var queue=new int[mask.Length];
        for(int start=0;start<mask.Length;start++)
        {
            if(!mask[start])continue;
            int head=0,tail=1,left=width,top=height,right=0,bottom=0;queue[0]=start;mask[start]=false;
            while(head<tail)
            {
                int index=queue[head++],x=index%width,y=index/width;
                left=Math.Min(left,x);right=Math.Max(right,x);top=Math.Min(top,y);bottom=Math.Max(bottom,y);
                if(x>0)Add(index-1);if(x+1<width)Add(index+1);if(y>0)Add(index-width);if(y+1<height)Add(index+width);
            }
            visit(new(tail,new(left,top,right-left+1,bottom-top+1)));
            void Add(int index){if(mask[index]){mask[index]=false;queue[tail++]=index;}}
        }
    }
}
