using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ArenaDesktopCompanion.Core.Settings;

namespace ArenaDesktopCompanion.Workspace;

/// <summary>One card, one tab owner. Reparent the same content when floating; never share sessions.</summary>
public sealed class DockCard : Border
{
    public string Kind => Layout.Kind;
    public PanelLayout Layout { get; }
    public string SessionId { get; }
    public event Action<DockCard>? Closed;
    public event Action? LayoutChanged;
    private readonly Grid _grid;
    private readonly ContentControl _body;
    private readonly Thumb _resize;
    private Window? _floating;
    private bool _disposing;
    private bool _ownerActive=true;
    public void CaptureLayout(){if(_floating is{} f&&f.WindowState==WindowState.Normal){Layout.Width=f.Width;Layout.Height=f.Height;Layout.Left=f.Left;Layout.Top=f.Top;}}
    public void SetOwnerActive(bool active){_ownerActive=active;if(_floating is{} f){CaptureLayout();if(active){f.Show();KeepVisible(f);}else f.Hide();}}
    private static void KeepVisible(Window f)
    {
        var area=PanelPlacement.Clamp(f.Left,f.Top,f.Width,f.Height,[MonitorPlacement.Area(f)]);
        f.Left=area.Left;f.Top=area.Top;f.Width=area.Width;f.Height=area.Height;
    }
    private Point _start;
    private bool _dragInProgress;
    private readonly string _title;
    private readonly Brush _background;
    public DockCard(string sessionId, string title, PanelLayout layout, FrameworkElement content)
    {
        SessionId=sessionId;Layout=layout;_title=title;
        _background=(Brush)Application.Current.FindResource("Brush.Card");
        Background=_background;BorderBrush=(Brush)Application.Current.FindResource("Brush.Border");BorderThickness=new Thickness(1);CornerRadius=new CornerRadius(8);Margin=new Thickness(0,0,0,10);
        _grid=new Grid {Height=Math.Clamp(layout.Height,180,1200)};
        _grid.RowDefinitions.Add(new RowDefinition{Height=new GridLength(38)});_grid.RowDefinitions.Add(new RowDefinition());_grid.RowDefinitions.Add(new RowDefinition{Height=new GridLength(6)});
        var header=new DockPanel{Background=(Brush)Application.Current.FindResource("Brush.Surface"),LastChildFill=true,Cursor=Cursors.SizeAll};
        var close=new Button{Content="×",Width=30,ToolTip="Panel schließen",Padding=new Thickness(2)};DockPanel.SetDock(close,Dock.Right);close.Click+=(_,_)=>Closed?.Invoke(this);header.Children.Add(close);
        var detach=new Button{Content="↗",Width=30,ToolTip="Aus-/wieder andocken",Padding=new Thickness(2)};DockPanel.SetDock(detach,Dock.Right);detach.Click+=(_,_)=>ToggleFloat();header.Children.Add(detach);
        header.Children.Add(new TextBlock{Text=title,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(12,0,0,0),FontWeight=FontWeights.SemiBold});
        header.AddHandler(Mouse.PreviewMouseDownEvent,new MouseButtonEventHandler((_,e)=>{if(e.ChangedButton==MouseButton.Left)_start=e.GetPosition(header);}),true);
        header.AddHandler(Mouse.PreviewMouseMoveEvent,new MouseEventHandler((_,e)=>{if(e.LeftButton!=MouseButtonState.Pressed||_floating is not null||_dragInProgress||(e.GetPosition(header)-_start).Length<=8)return;_dragInProgress=true;try{DragDrop.DoDragDrop(this,new DataObject("AXON.Panel",this),DragDropEffects.Move);}finally{_dragInProgress=false;}e.Handled=true;}),true);
        _grid.Children.Add(header);_body=new ContentControl{Content=content,Margin=new Thickness(12,8,12,8)};Grid.SetRow(_body,1);_grid.Children.Add(_body);
        _resize=new Thumb{Height=6,Cursor=Cursors.SizeNS,Background=(Brush)Application.Current.FindResource("Brush.Border")};Grid.SetRow(_resize,2);_grid.Children.Add(_resize);_resize.DragDelta+=(_,e)=>{_grid.Height=Math.Clamp(_grid.ActualHeight+e.VerticalChange,180,1200);Layout.Height=_grid.Height;LayoutChanged?.Invoke();};
        Child=_grid;AllowDrop=true;PreviewDragOver+=(_,e)=>{e.Effects=e.Data.GetData("AXON.Panel") is DockCard candidate&&candidate!=this&&candidate.SessionId==SessionId&&Parent is Panel parent&&candidate.Parent==parent?DragDropEffects.Move:DragDropEffects.None;e.Handled=true;};PreviewDrop+=(_,e)=>{if(e.Data.GetData("AXON.Panel") is DockCard other&&other!=this&&other.SessionId==SessionId&&Parent is Panel parent&&other.Parent==parent){var index=parent.Children.IndexOf(this);parent.Children.Remove(other);parent.Children.Insert(index,other);LayoutChanged?.Invoke();e.Handled=true;}};
    }
    public void ToggleFloat()
    {
        if(_disposing)return;
        if(_floating is not null){_floating.Close();return;}
        var owner=Window.GetWindow(this);Child=null;_grid.Height=double.NaN;_resize.Visibility=Visibility.Collapsed;
        _floating=new Window{Title="AXON RC30 · "+_title,Content=_grid,Owner=owner,Background=_background,Foreground=(Brush)Application.Current.FindResource("Brush.Text"),Width=Math.Clamp(Layout.Width,340,1400),Height=Math.Clamp(Layout.Height,250,1200),MinWidth=320,MinHeight=220,Left=Layout.Left,Top=Layout.Top};
        _floating.SourceInitialized+=(_,_)=>
        {
            if(_floating is null)return;KeepVisible(_floating);
            System.Windows.Interop.HwndSource.FromHwnd(new System.Windows.Interop.WindowInteropHelper(_floating).Handle)?.AddHook((IntPtr h,int message,IntPtr w,IntPtr l,ref bool handled)=>{if(message is 0x007E or 0x02E0)Dispatcher.BeginInvoke(()=>{if(_floating is{} current)KeepVisible(current);});return IntPtr.Zero;});
        };
        _floating.LocationChanged+=(_,_)=>{CaptureLayout();if(!_disposing)LayoutChanged?.Invoke();};
        _floating.SizeChanged+=(_,_)=>{CaptureLayout();if(!_disposing)LayoutChanged?.Invoke();};
        Layout.Detached=true;
        _floating.Closed+=(_,_)=>{if(_floating is null)return;Layout.Width=_floating.ActualWidth;Layout.Height=_floating.ActualHeight;Layout.Left=_floating.Left;Layout.Top=_floating.Top;_floating.Content=null;_floating=null;Layout.Detached=_disposing;if(!_disposing){_grid.Height=Math.Clamp(Layout.Height,180,1200);_resize.Visibility=Visibility.Visible;Child=_grid;Visibility=Visibility.Visible;LayoutChanged?.Invoke();}};
        Visibility=Visibility.Collapsed;if(_ownerActive)_floating.Show();CaptureLayout();LayoutChanged?.Invoke();
    }
    public void DisposeCard(){CaptureLayout();_disposing=true;_floating?.Close();}
}
