using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using ArenaDesktopCompanion.Core.Settings;
namespace ArenaDesktopCompanion.Workspace;
internal static class MonitorPlacement
{
    [StructLayout(LayoutKind.Sequential)]private struct Rect{public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)]private struct Info{public int Size;public Rect Monitor,Work;public uint Flags;}
    [DllImport("user32.dll")]private static extern IntPtr MonitorFromWindow(IntPtr window,uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Auto)]private static extern bool GetMonitorInfo(IntPtr monitor,ref Info info);
    private delegate bool MonitorProc(IntPtr monitor,IntPtr dc,ref Rect rect,IntPtr data);
    [DllImport("user32.dll")]private static extern bool EnumDisplayMonitors(IntPtr dc,IntPtr clip,MonitorProc callback,IntPtr data);
    [DllImport("user32.dll")]private static extern bool SetWindowPos(IntPtr window,IntPtr insert,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll")]private static extern bool GetWindowRect(IntPtr window,out Rect rect);
    internal static IReadOnlyList<(IntPtr Handle,DisplayArea Physical)> WorkAreas()
    {
        var result=new List<(IntPtr,DisplayArea)>();
        EnumDisplayMonitors(IntPtr.Zero,IntPtr.Zero,(IntPtr monitor,IntPtr dc,ref Rect rect,IntPtr data)=>{var info=new Info{Size=Marshal.SizeOf<Info>()};if(GetMonitorInfo(monitor,ref info))result.Add((monitor,new(info.Work.Left,info.Work.Top,info.Work.Right-info.Work.Left,info.Work.Bottom-info.Work.Top)));return true;},IntPtr.Zero);return result;
    }
    internal static void MoveForSmoke(Window window,DisplayArea area)
    {
        if(Environment.GetEnvironmentVariable("AXON_RC30_SMOKE")!="1")throw new InvalidOperationException("Nur isolierter Prüfstand erlaubt.");
        if(!SetWindowPos(new WindowInteropHelper(window).Handle,IntPtr.Zero,(int)area.Left+30,(int)area.Top+30,0,0,0x0015))throw new InvalidOperationException("Prüffenster konnte nicht auf Monitor gesetzt werden.");
    }
    internal static bool PhysicallyInside(Window window,IntPtr monitor,DisplayArea area)
    {
        var hwnd=new WindowInteropHelper(window).Handle;
        return MonitorFromWindow(hwnd,2)==monitor&&GetWindowRect(hwnd,out var r)&&r.Left>=area.Left&&r.Top>=area.Top&&r.Right<=area.Left+area.Width+1&&r.Bottom<=area.Top+area.Height+1;
    }
    public static DisplayArea Area(Window window)
    {
        var handle=new WindowInteropHelper(window).Handle;
        if(handle!=IntPtr.Zero)
        {
            var info=new Info{Size=Marshal.SizeOf<Info>()};
            if(GetMonitorInfo(MonitorFromWindow(handle,2),ref info))
            {
                var transform=HwndSource.FromHwnd(handle)?.CompositionTarget?.TransformFromDevice??System.Windows.Media.Matrix.Identity;
                var a=transform.Transform(new Point(info.Work.Left,info.Work.Top));var b=transform.Transform(new Point(info.Work.Right,info.Work.Bottom));return new(a.X,a.Y,b.X-a.X,b.Y-a.Y);
            }
        }
        var fallback=SystemParameters.WorkArea;return new(fallback.Left,fallback.Top,fallback.Width,fallback.Height);
    }
}
