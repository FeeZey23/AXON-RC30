using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using ArenaDesktopCompanion.Workspace;
namespace ArenaDesktopCompanion;
public partial class MainWindow
{
    [StructLayout(LayoutKind.Sequential)]private struct NativePoint{public int X,Y;}
    [DllImport("user32.dll")]private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")]private static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")]private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")]private static extern bool ShowWindow(IntPtr hwnd,int command);
    [DllImport("user32.dll")]private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")]private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]private static extern IntPtr WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")]private static extern IntPtr GetAncestor(IntPtr hwnd,uint flags);
    [DllImport("user32.dll")]private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")]private static extern void mouse_event(uint flags,uint x,uint y,uint data,UIntPtr extra);
    [DllImport("user32.dll")]private static extern int GetSystemMetrics(int index);
    private async Task RunNativePanelDragAsync(TabSession owner,TabSession other,List<string> checks)
    {
        if(Environment.GetEnvironmentVariable("AXON_RC30_SMOKE")!="1")throw new InvalidOperationException("Nur isolierter RC30-Prüfstand erlaubt.");
        Activate(owner);UpdateLayout();var cards=Panels.Children.OfType<DockCard>().Where(c=>!c.Layout.Detached).Take(2).ToArray();var target=cards[0];var source=cards[1];
        foreach(var c in cards){var thumb=FindVisualChildren<System.Windows.Controls.Primitives.Thumb>(c).Single();thumb.RaiseEvent(new System.Windows.Controls.Primitives.DragDeltaEventArgs(0,-1200){RoutedEvent=System.Windows.Controls.Primitives.Thumb.DragDeltaEvent});}
        FindVisualChildren<System.Windows.Controls.ScrollViewer>(this).Single(s=>ReferenceEquals(s.Content,Panels)).ScrollToTop();UpdateLayout();await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);await Task.Delay(350);
        checks.Add("Hardware-Monitorzahl im Windows-Prüfstand: "+GetSystemMetrics(80)+"; weitere Topologien und 100/125/150/200 Prozent separat synthetisch geprüft");
        var a=source.PointToScreen(new Point(source.ActualWidth/2,20));var b=target.PointToScreen(new Point(target.ActualWidth/2,20));
        var from=new NativePoint{X=(int)a.X,Y=(int)a.Y};var to=new NativePoint{X=(int)b.X,Y=(int)b.Y};var handle=new WindowInteropHelper(this).Handle;
        GetCursorPos(out var original);var priorTopmost=Topmost;Topmost=true;WindowState=WindowState.Normal;Show();ShowWindow(handle,9);ShowWindow(handle,5);Activate();SetForegroundWindow(handle);UpdateLayout();await Task.Delay(250);
        a=source.PointToScreen(new Point(source.ActualWidth/2,20));b=target.PointToScreen(new Point(target.ActualWidth/2,20));from=new NativePoint{X=(int)a.X,Y=(int)a.Y};to=new NativePoint{X=(int)b.X,Y=(int)b.Y};
        if(GetForegroundWindow()!=handle&&(GetAsyncKeyState(1)&0x8000)==0)
        {
            var header=PointToScreen(new Point(300,20));var focusPoint=new NativePoint{X=(int)header.X,Y=(int)header.Y};
            if(GetAncestor(WindowFromPoint(focusPoint),2)==handle)
            {
                await Task.Run(()=>{SetCursorPos(focusPoint.X,focusPoint.Y);mouse_event(2,0,0,0,UIntPtr.Zero);Thread.Sleep(70);mouse_event(4,0,0,0,UIntPtr.Zero);});await Task.Delay(150);
            }
        }
        if(GetForegroundWindow()!=handle||GetAncestor(WindowFromPoint(from),2)!=handle||GetAncestor(WindowFromPoint(to),2)!=handle||(GetAsyncKeyState(1)&0x8000)!=0){checks.Add("SKIP: Native Maus-Geste nicht sicher möglich; Fokus="+(GetForegroundWindow()==handle)+", Start sichtbar="+(GetAncestor(WindowFromPoint(from),2)==handle)+", Ziel sichtbar="+(GetAncestor(WindowFromPoint(to),2)==handle)+", Maustaste frei="+((GetAsyncKeyState(1)&0x8000)==0)+", NativeVisible="+IsWindowVisible(handle)+", WpfVisible="+IsVisible+", State="+WindowState+"; keine Drag-Geste injiziert");SetCursorPos(original.X,original.Y);Topmost=priorTopmost;return;}
        var mouseDowns=0;var pressedMoves=0;var nativeDrops=0;
        System.Windows.Input.MouseButtonEventHandler observeDown=(_,_)=>mouseDowns++;
        System.Windows.Input.MouseEventHandler observeMove=(_,e)=>{if(e.LeftButton==System.Windows.Input.MouseButtonState.Pressed)pressedMoves++;};
        DragEventHandler observeDrop=(_,e)=>{if(ReferenceEquals(e.Data.GetData("AXON.Panel"),source))nativeDrops++;};
        source.AddHandler(System.Windows.Input.Mouse.PreviewMouseDownEvent,observeDown,true);
        source.AddHandler(System.Windows.Input.Mouse.PreviewMouseMoveEvent,observeMove,true);
        target.AddHandler(DragDrop.PreviewDropEvent,observeDrop,true);
        try
        {
            await Task.Run(()=>
            {
                if(GetForegroundWindow()!=handle)throw new InvalidOperationException("Prüffenster hat Fokus verloren; keine Maus-Geste gestartet.");
                SetCursorPos(from.X,from.Y);mouse_event(2,0,0,0,UIntPtr.Zero);
                try{Thread.Sleep(150);for(var i=1;i<=24;i++){SetCursorPos(from.X+(to.X-from.X)*i/24,from.Y+(to.Y-from.Y)*i/24);Thread.Sleep(25);}Thread.Sleep(100);}
                finally{mouse_event(4,0,0,0,UIntPtr.Zero);}
            });
            await Task.Delay(150);
            if(mouseDowns<1||pressedMoves<1||nativeDrops<1||source.Parent!=Panels||target.Parent!=Panels||Panels.Children.IndexOf(source)<0||Panels.Children.IndexOf(target)<0||Panels.Children.IndexOf(source)>=Panels.Children.IndexOf(target))throw new InvalidOperationException($"Echter Windows-Maus-Drag hat Panelreihenfolge nicht geändert. Down={mouseDowns}; PressedMoves={pressedMoves}; TargetDrops={nativeDrops}; Source={source.ActualWidth}x{source.ActualHeight}; From={from.X},{from.Y}; To={to.X},{to.Y}; Modifiers={System.Windows.Input.Keyboard.Modifiers}");
            checks.Add("Fix4: Echte Windows-Maus-Geste löst WPF DragDrop aus und ordnet Panel vor Ziel ein");
            Activate(other);Activate(owner);if(Panels.Children.IndexOf(source)>=Panels.Children.IndexOf(target))throw new InvalidOperationException("Panelreihenfolge nach Tabwechsel verloren.");
            checks.Add("Fix4: Per Maus geänderte Panelreihenfolge überlebt Tabwechsel");
            var loaded=App.Services.SettingsStore.Load().Arbeitsbereiche.Single(t=>t.Id==owner.Config.Id).Panels;
            if(loaded.FindIndex(p=>p.Kind==source.Kind)>=loaded.FindIndex(p=>p.Kind==target.Kind))throw new InvalidOperationException("Panelreihenfolge nicht dauerhaft gespeichert.");
            checks.Add("Fix4: Per Maus geänderte Panelreihenfolge dauerhaft in Einstellungen gespeichert");
        }
        finally{source.RemoveHandler(System.Windows.Input.Mouse.PreviewMouseDownEvent,observeDown);source.RemoveHandler(System.Windows.Input.Mouse.PreviewMouseMoveEvent,observeMove);target.RemoveHandler(DragDrop.PreviewDropEvent,observeDrop);SetCursorPos(original.X,original.Y);Topmost=priorTopmost;}
    }
}
