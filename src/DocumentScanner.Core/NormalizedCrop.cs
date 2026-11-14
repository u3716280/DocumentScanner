namespace DocumentScanner.Core;
// Fractions of the unrotated original, independent of preview resolution and DPI.
public sealed record NormalizedCrop(double X,double Y,double Width,double Height)
{
    public void Validate()
    {
        if(!double.IsFinite(X)||!double.IsFinite(Y)||!double.IsFinite(Width)||!double.IsFinite(Height)||
            X<0||Y<0||Width<=0||Height<=0||X+Width>1.000000001||Y+Height>1.000000001)
            throw new ArgumentException("Invalid crop rectangle.");
    }
    public static NormalizedCrop FromView(NormalizedCrop view,int rotation)
    {
        view.Validate();var r=((rotation%360)+360)%360;
        var result=r switch{
            0=>view,
            90=>new(view.Y,1-view.X-view.Width,view.Height,view.Width),
            180=>new(1-view.X-view.Width,1-view.Y-view.Height,view.Width,view.Height),
            270=>new(1-view.Y-view.Height,view.X,view.Height,view.Width),
            _=>throw new ArgumentException("Rotation must be a multiple of 90.")};
        // Roundoff near an edge must not create a negative coordinate.
        result=result with{X=Math.Max(0,result.X),Y=Math.Max(0,result.Y)};result.Validate();return result;
    }
}
