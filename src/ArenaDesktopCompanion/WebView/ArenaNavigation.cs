using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using ArenaDesktopCompanion.Core.Product;
namespace ArenaDesktopCompanion.WebView;

/// <summary>Top-level Arena stays fixed; OAuth popups keep the native opener and shared profile.</summary>
public sealed class ArenaNavigation : IDisposable
{
    public event Action<CoreWebView2>? PopupCreated;
    public IReadOnlyList<Window> Windows => _windows;
    private readonly WebView2 _main;
    private readonly List<Window> _windows=[];
    private bool _disposed;
    public ArenaNavigation(WebView2 main)
    {
        _main=main;var core=main.CoreWebView2;
        core.NavigationStarting+=(_,e)=>
        {
            if(_disposed||ProductPolicy.IsArena(e.Uri)||e.Uri=="about:blank")return;
            e.Cancel=true;
            if(IsWeb(e.Uri))_ = OpenAsync(e.Uri,null,core);
        };
        core.NewWindowRequested+=(_,e)=>{e.Handled=true;var d=e.GetDeferral();_ = OpenAsync(e.Uri,(e,d),core);};
    }
    private static bool IsWeb(string uri)=>Uri.TryCreate(uri,UriKind.Absolute,out var u)&&u.Scheme is "https" or "http";
    public Task OpenLinkAsync(string uri)=>OpenAsync(uri,null,_main.CoreWebView2);
    private async Task OpenAsync(string uri,(CoreWebView2NewWindowRequestedEventArgs Event,CoreWebView2Deferral Deferral)? request,CoreWebView2 parent)
    {
        Window? window=null;
        try
        {
            if(_disposed||(!IsWeb(uri)&&uri!="about:blank"))return;
            var child=new WebView2();var address=new System.Windows.Controls.TextBox{IsReadOnly=true,Padding=new Thickness(8),Text=uri};var layout=new System.Windows.Controls.DockPanel();System.Windows.Controls.DockPanel.SetDock(address,System.Windows.Controls.Dock.Top);layout.Children.Add(address);layout.Children.Add(child);window=new Window{Title="AXON RC30 · Web",Width=1000,Height=760,MinWidth=500,MinHeight=400,Owner=Application.Current.MainWindow,Content=layout,WindowStartupLocation=WindowStartupLocation.CenterOwner};
            var owner=window;_windows.Add(owner);owner.Closed+=(_,_)=>{_windows.Remove(owner);child.Dispose();};owner.Show();
            var options=parent.Environment.CreateCoreWebView2ControllerOptions();options.ProfileName=parent.Profile.ProfileName;options.IsInPrivateModeEnabled=parent.Profile.IsInPrivateModeEnabled;
            await child.EnsureCoreWebView2Async(parent.Environment,options);
            if(_disposed||!owner.IsVisible)return;
            var core=child.CoreWebView2;
            core.SourceChanged+=(_,_)=>address.Text=core.Source;
            PopupCreated?.Invoke(core);
            core.NavigationStarting+=(_,e)=>{if(!IsWeb(e.Uri)&&e.Uri!="about:blank")e.Cancel=true;};
            core.DocumentTitleChanged+=(_,_)=>owner.Title="AXON RC30 · "+core.DocumentTitle;
            core.WindowCloseRequested+=(_,_)=>owner.Close();
            core.NewWindowRequested+=(_,e)=>{e.Handled=true;var d=e.GetDeferral();_ = OpenAsync(e.Uri,(e,d),core);};
            // Complete OAuth navigation inside its native child first (including POST/cookies).
            // Then refresh Arena, never replay an authorization-code callback in the main view.
            core.NavigationCompleted+=(_,e)=>{if(!_disposed&&e.IsSuccess&&ProductPolicy.IsArena(core.Source)&&_main.CoreWebView2 is{} main)main.Reload();};
            if(request is{} pending)pending.Event.NewWindow=core;else core.Navigate(uri);
        }
        catch(Exception ex) when(ex is not OutOfMemoryException){window?.Close();App.Services.Logger.Write("WARN","WebView popup: "+ex.GetType().Name);}
        finally{request?.Deferral.Complete();}
    }
    public void Dispose(){_disposed=true;foreach(var w in _windows.ToArray())w.Close();_windows.Clear();}
}
