namespace ArenaDesktopCompanion.Core.Settings;
public readonly record struct DisplayArea(double Left,double Top,double Width,double Height);
public static class PanelPlacement
{
    public static DisplayArea Clamp(double left,double top,double width,double height,IReadOnlyList<DisplayArea> screens)
    {
        var target=screens.FirstOrDefault(s=>left>=s.Left&&left<s.Left+s.Width&&top>=s.Top&&top<s.Top+s.Height);
        if(target.Width<=0||target.Height<=0)target=screens.FirstOrDefault();
        if(target.Width<=0||target.Height<=0)target=new(0,0,1024,768);
        width=double.IsFinite(width)?Math.Clamp(width,Math.Min(320,target.Width),target.Width):Math.Min(460,target.Width);
        height=double.IsFinite(height)?Math.Clamp(height,Math.Min(220,target.Height),target.Height):Math.Min(300,target.Height);
        left=double.IsFinite(left)?Math.Clamp(left,target.Left,target.Left+Math.Max(0,target.Width-width)):target.Left;
        top=double.IsFinite(top)?Math.Clamp(top,target.Top,target.Top+Math.Max(0,target.Height-height)):target.Top;
        return new(left,top,width,height);
    }
}
