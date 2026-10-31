using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ArenaDesktopCompanion.Core.Bridge;
using ArenaDesktopCompanion.Core.Settings;
using ArenaDesktopCompanion.Infrastructure;
using ArenaDesktopCompanion.Workspace;
using AXON.Connector;
using ArenaDesktopCompanion.WebView;

namespace ArenaDesktopCompanion;
public partial class MainWindow : Window
{
    private readonly AppSettings _settings=App.Services.Settings;
    private readonly Dictionary<string,TabSession> _sessions=new(StringComparer.Ordinal);
    private readonly DispatcherTimer _timer=new(){Interval=TimeSpan.FromSeconds(1)};
    private TabSession? _active;
    private Point _dragOrigin;
    private bool _closing;
    public MainWindow()
    {
        InitializeComponent();
        Title="ISAS - AXON RC30 · "+Core.Product.ProductPolicy.CurrentVersion.ToString(3);
        _timer.Tick+=(_,_)=>RefreshStatus();
        var menu=new ContextMenu();var tree=new MenuItem{Header="Workspace-Dateibaum öffnen"};tree.Click+=(_,_)=>{if(_active is{} s)ShowPanel(s,"files");};menu.Items.Add(tree);
        // The normal click opens Explorer; right-click offers the optional workspace tree.
        ProjectFilesButton.ContextMenu=menu;
    }
    protected override void OnSourceInitialized(EventArgs e){base.OnSourceInitialized(e);DarkTitleBar.Apply(this);}
    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T:DependencyObject
    {for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var child=VisualTreeHelper.GetChild(parent,i);if(child is T t)yield return t;foreach(var nested in FindVisualChildren<T>(child))yield return nested;}}
    private async void Window_Loaded(object sender,RoutedEventArgs e)
    {
        try
        {
            _settings.ArbeitsbereicheSicherstellen();_settings.ApplyRc30Preferences();CleanupForgottenTabs();
            foreach(var tab in _settings.Arbeitsbereiche){var session=CreateSession(tab);_sessions.Add(tab.Id,session);session.StartProject();}
            var active=_sessions[_settings.Arbeitsbereiche[_settings.AktiverArbeitsbereich].Id];
            Activate(active);if(App.Services.SettingsStore.RecoveryNotice is{} recovery)active.Hint=recovery;_timer.Start();Save();
            // Every restored session starts independently, including tabs not currently selected.
            await Task.WhenAll(_sessions.Values.Select(s=>s.EnsureBrowserAsync()));
            if(!_closing&&_active==active)ShowBrowser(active);
            if(Environment.GetEnvironmentVariable("AXON_RC30_SMOKE")=="1")await RunUiSmokeAsync();
        }
        catch(Exception ex){if(Environment.GetEnvironmentVariable("AXON_RC30_SMOKE")=="1"){App.ReportSmokeFailure(ex);Application.Current.Shutdown(2);}else Report(ex);}
    }
    private TabSession CreateSession(Arbeitsbereich tab,TabSession? loginSource=null)
    {
        var session=new TabSession(tab,App.Services){LoginSource=loginSource};
        session.MetadataChanged+=s=>{if(!s.Disposed&&!_closing){DrawTabs();Save();}};
        session.ChatSwitched+=s=>{if(s.Disposed||_closing)return;if(Directory.Exists(s.Config.ProjectFolder)){Rebind(s,s.Config.ProjectFolder!);s.Hint="Anderer Chat erkannt. Für diesen Chat einmal Connect senden; Projekt und Tabprofil bleiben erhalten.";}else{s.Config.ConnectionId="";s.Config.ConnectedChatAddress="";}};
        session.BrowserCreated+=browser=>{if(!session.Disposed&&!_closing){BrowserHost.Children.Add(browser);browser.Visibility=_active==session?Visibility.Visible:Visibility.Hidden;}};
        return session;
    }
    private void ShowBrowser(TabSession session)
    {
        foreach(var child in BrowserHost.Children.OfType<Microsoft.Web.WebView2.Wpf.WebView2>())child.Visibility=ReferenceEquals(child,session.Browser)?Visibility.Visible:Visibility.Hidden;
    }
    private bool Save()
    {
        foreach(var s in _sessions.Values)foreach(var c in s.Cards)c.CaptureLayout();
        if(_active is{} active)
        {
            _settings.AktiverArbeitsbereich=_settings.Arbeitsbereiche.FindIndex(t=>t.Id==active.Config.Id);
            if(SideColumn.ActualWidth>=290)active.Config.SideWidth=SideColumn.ActualWidth;
        }
        try{App.Services.SettingsStore.Save(_settings);return true;}catch(Exception ex){Report(ex);return false;}
    }
    private void Activate(TabSession session)
    {
        if(session.Disposed||_closing)return;
        if(_active is{} previous)previous.Config.SideWidth=Math.Clamp(SideColumn.ActualWidth,290,700);
        foreach(var s in _sessions.Values)foreach(var c in s.Cards)c.SetOwnerActive(s==session);
        _active=session;SideColumn.Width=new GridLength(Math.Clamp(session.Config.SideWidth,290,700));
        ShowBrowser(session);Panels.Children.Clear();
        foreach(var layout in session.Config.Panels.ToList())if(!session.Cards.Any(c=>c.Kind==layout.Kind))CreatePanel(session,layout);
        foreach(var layout in session.Config.Panels){var card=session.Cards.First(c=>ReferenceEquals(c.Layout,layout));if(card.Parent is Panel old)old.Children.Remove(card);Panels.Children.Add(card);}
        DrawTabs();Save();RefreshStatus();_ = ActivateBrowserAsync(session);
    }
    private async Task ActivateBrowserAsync(TabSession session)
    {try{await session.EnsureBrowserAsync();if(_active==session&&!session.Disposed&&!_closing){ShowBrowser(session);session.RefreshChatName();}}catch(Exception ex){session.Log(ex.Message,"ERROR");}}
    private void DrawTabs()
    {
        Tabs.Children.Clear();HistoryButton.Content=L("Verlauf ▾")+(_settings.ClosedWorkspaces.Count>0?" ("+_settings.ClosedWorkspaces.Count+")":"");
        foreach(var tab in _settings.Arbeitsbereiche)
        {
            var session=_sessions[tab.Id];
            var frame=new Border{CornerRadius=new CornerRadius(6),Background=Brush(_active==session?"Brush.Selected":"Brush.Surface"),BorderBrush=Brush(_active==session?"Brush.Accent":"Brush.Border"),BorderThickness=new Thickness(1),Margin=new Thickness(0,0,6,0),AllowDrop=true};
            var row=new StackPanel{Orientation=Orientation.Horizontal};
            var label=(tab.Group.Length>0?tab.Group+" / ":"")+tab.Beschriftung();
            var select=new Button{Content=new TextBlock{Text=label,Tag="UserContent",TextTrimming=TextTrimming.CharacterEllipsis},MaxWidth=300,Tag=tab.Id,Padding=new Thickness(12,8,8,8),MinWidth=100,ToolTip=tab.Beschriftung()+"\n"+tab.ChatAddress+"\n"+L("Eigene Sitzung")+" · "+tab.Id+"\n"+tab.ProjectFolder};select.Click+=(_,_)=>Activate(session);
            System.Windows.Automation.AutomationProperties.SetAutomationId(select,"Tab-"+tab.Id);
            select.PreviewMouseLeftButtonDown+=(_,e)=>_dragOrigin=e.GetPosition(select);
            select.PreviewMouseMove+=(_,e)=>{if(e.LeftButton==MouseButtonState.Pressed&&(e.GetPosition(select)-_dragOrigin).Length>10)DragDrop.DoDragDrop(select,new DataObject("AXON.Tab",tab.Id),DragDropEffects.Move);};
            frame.Drop+=(_,e)=>{if(e.Data.GetData("AXON.Tab") is string id&&TabOrder.Move(_settings,id,tab.Id)){DrawTabs();Save();e.Handled=true;}};
            var context=new ContextMenu();
            var rename=new MenuItem{Header="Umbenennen …"};rename.Click+=(_,_)=>{var text=AskText("Tab umbenennen",tab.Name);if(text is{Length:>0}){tab.Name=text[..Math.Min(160,text.Length)];tab.AutoChatName=false;DrawTabs();Save();}};context.Items.Add(rename);
            var autoName=new MenuItem{Header="Arena-Chatnamen automatisch übernehmen",IsCheckable=true,IsChecked=tab.AutoChatName};autoName.Click+=(_,_)=>{tab.AutoChatName=autoName.IsChecked;session.RefreshChatName();Save();};context.Items.Add(autoName);
            var group=new MenuItem{Header="Gruppe ändern …"};group.Click+=(_,_)=>{var text=AskText("Gruppe (leer = ohne Gruppe)",tab.Group);if(text is not null){tab.Group=text.Trim()[..Math.Min(24,text.Trim().Length)];DrawTabs();Save();}};context.Items.Add(group);
            var login=new MenuItem{Header="Arena-Anmeldung übernehmen von …"};
            foreach(var other in _sessions.Values.Where(o=>o!=session))
            {
                var source=other;var choice=new MenuItem{Header=source.Config.Beschriftung(),Tag="UserContent"};choice.Click+=async(_,_)=>{try{await source.EnsureBrowserAsync();await session.EnsureBrowserAsync();if(source.Disposed||session.Disposed)return;var n=await ArenaLoginBootstrap.CopyAsync(source.Browser!.CoreWebView2,session.Browser!.CoreWebView2,true);session.Hint=n>0?"Arena-Anmeldung übernommen; Seite wird neu geladen.":"Quelle hat keine Arena-Cookies; dort zuerst anmelden.";if(n>0)session.Browser.CoreWebView2.Reload();}catch(Exception ex){session.Hint=ex.Message;session.Log(ex.Message,"ERROR");}};login.Items.Add(choice);
            }
            context.Items.Add(login);
            var explorer=new MenuItem{Header="Projekt im Explorer"};explorer.Click+=(_,_)=>OpenProject(session);context.Items.Add(explorer);select.ContextMenu=context;
            row.Children.Add(select);var close=new Button{Content="×",Padding=new Thickness(5),Width=25,ToolTip="Nur diese Sitzung schließen"};close.Click+=(_,_)=>CloseTab(session);row.Children.Add(close);frame.Child=row;Tabs.Children.Add(frame);
        }
    }
    private static Brush Brush(string name)=>name.StartsWith("#",StringComparison.Ordinal)?new SolidColorBrush((Color)ColorConverter.ConvertFromString(name)):(Brush)Application.Current.FindResource(name);
    private void NewTab_Click(object sender,RoutedEventArgs e)
    {
        var source=(Keyboard.Modifiers&ModifierKeys.Shift)==0?_active:null;
        var index=_settings.ArbeitsbereichAnlegen();var tab=_settings.Arbeitsbereiche[index];var session=CreateSession(tab,source);_sessions.Add(tab.Id,session);Activate(session);ShowPanel(session,"settings");
    }
    private void CloseTab(TabSession session)
    {
        if(session.Connecting){session.Hint="Connect läuft noch. Nach Abschluss schließen.";return;}
        foreach(var c in session.Cards)c.CaptureLayout();
        if(!ChangeHistory(()=>TabHistory.Close(_settings,session.Config.Id,DateTimeOffset.UtcNow)))return;
        _sessions.Remove(session.Config.Id);
        if(session.Browser is{} browser)BrowserHost.Children.Remove(browser);session.Dispose();
        foreach(var tab in _settings.Arbeitsbereiche.Where(t=>!_sessions.ContainsKey(t.Id)))_sessions.Add(tab.Id,CreateSession(tab));
        if(_active==session){_active=null;Activate(_sessions[_settings.Arbeitsbereiche[_settings.AktiverArbeitsbereich].Id]);}
        DrawTabs();Save();
    }

    private void RefreshStatus()
    {
        foreach(var s in _sessions.Values)s.PollChatMetadata();
        if(_active is not{} session)return;
        session.RequestScan();var service=session.Host?.Dienst;
        if(service?.PeerConfirmed==true&&session.Config.ConnectedChatAddress.Length==0&&session.Browser?.CoreWebView2?.Source is{} attached&&attached.StartsWith("https://arena.ai/agent/",StringComparison.Ordinal)) {session.Config.ConnectedChatAddress=attached;Save();}
        var light=ConnectionHealth.Evaluate(session.Host?.Laeuft==true,service?.LetzterFehler,service?.PeerConfirmed==true&&service.PeerRestored==false,service?.PeerLastSeen,DateTimeOffset.UtcNow);
        session.Connector.Observe(light);
        StatusLight.Fill=Brush(light==ConnectionLight.Green?"Brush.Success":light==ConnectionLight.Red?"Brush.Danger":"Brush.Warn");
        ConnectionText.Text=session.ProjectError is{} problem?L("Tab gestört")+": "+problem:session.Host?.Status??"Projekt noch nicht eingerichtet";
        ProjectText.Text=session.Config.ProjectFolder??"Projekt in Einstellungen auswählen";
        CountsText.Text=!session.ScanComplete?"Dateibestand wird ermittelt …":session.FileCount==0?"0 Projektdateien · Ordner noch leer":$"{session.FileCount:N0} {(_settings.UiLanguage=="en"?"files":"Dateien")}  /  {session.ByteCount/1048576d:N1} MB";
        ContactText.Text=service?.PeerLastSeen is{} peer?$"{L("Chat-Kontakt")} {peer.ToLocalTime():HH:mm:ss}":service?.LetzterKontakt is{} contact?$"{L("Relay erreichbar")} {contact:HH:mm:ss} · {L("Chat unbestätigt")}":"Noch kein Kontakt";
        ConnectButton.IsEnabled=!session.Connecting;ConnectButton.Content=session.Connecting?"Vorbereiten …":"Connect";
        Product.UiText.Apply(this);
        ConnectHint.Text=Product.UiText.Translate(session.Hint);Footer.Text="RC30 · "+session.Config.Beschriftung()+" · "+L("eigenes Browserprofil")+" · "+(service?.PublikationsFehler is{} err?L("Dateiabgleich")+": "+err:L("Sitzungen unabhängig"));
    }
    private async void Connect_Click(object sender,RoutedEventArgs e)
    {
        if(_active is not{} session||session.Connecting)return;
        session.Connecting=true;
        try
        {
            if(!Directory.Exists(session.Config.ProjectFolder)&&!await ChooseProjectAsync(session))return;
            await session.EnsureBrowserAsync();
            if(session.Disposed||_active!=session)throw new InvalidOperationException("Tab gewechselt; Connect für den gewählten Tab erneut auslösen.");
            var address=session.Browser?.CoreWebView2?.Source;
            var adapter=new ArenaProviderAdapter();
            if(address is null||!Uri.TryCreate(address,UriKind.Absolute,out var uri)||!adapter.IsSupportedChat(uri))throw new InvalidOperationException("Zuerst Arena Agent Mode öffnen und anmelden.");
            if(session.Config.ConnectedChatAddress.Length>0&&!string.Equals(session.Config.ConnectedChatAddress,uri.GetLeftPart(UriPartial.Path),StringComparison.Ordinal))
            {
                Rebind(session,session.Config.ProjectFolder!);
            }
            var prepared=await session.Connector.PrepareAsync(session.Config,uri,adapter,prepared=>
            {
                var previous=session.Config.ConnectionId;var previousChat=session.Config.ConnectedChatAddress;
                session.Config.ConnectionId=prepared.Id;session.Config.ConnectedChatAddress=ChatIdentity.Address(uri.AbsoluteUri)??uri.GetLeftPart(UriPartial.Path);
                try { App.Services.SettingsStore.Save(_settings); }
                catch { session.Config.ConnectionId=previous;session.Config.ConnectedChatAddress=previousChat; throw; }
                if(session.Host is null)session.StartProject();session.Host?.PrepareConnect(prepared.Id);
                return Task.CompletedTask;
            },session.Lifetime.Token);
            if(session.Disposed||_active!=session)throw new InvalidOperationException("Tab geändert; Übergabe nicht in einen anderen Chat kopiert.");
            var dir=Rc30Paths.SessionFolder(session.Config.Id);Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir,"AXON-CONNECT.json"),prepared.CapsuleJson);
            Clipboard.SetText(prepared.Prompt);
            if(session.Browser?.CoreWebView2 is not{} core)throw new InvalidOperationException("Browser nicht bereit; Übergabe liegt in der Zwischenablage.");
            session.Browser.Focus();
            var result=await DraftWriter.InsertAsync(core,prepared.Prompt,uri,session.Lifetime.Token);
            session.Hint=result.Ok?"Connect-Text steht im Eingabefeld. Bitte selbst absenden. Grün erst nach Handshake.":result.Code=="draft_not_empty"?"Vorhandener Entwurf bleibt erhalten. Connect-Text liegt in der Zwischenablage; bitte Entwurf sichern/leeren und Connect erneut drücken.":"Einsetzen nicht bestätigt ("+result.Code+"). Connect-Text liegt als Rückfall in der Zwischenablage.";
            if(result.Ok&&_active==session)session.Browser.Focus();
            session.Log(result.Ok?"Connect-Entwurf eingesetzt und nachgeprüft; NICHT abgesendet":"Connect-Entwurf nicht eingesetzt: "+result.Code,result.Ok?"INFO":"WARN");
            await Task.Yield();
        }
        catch(Exception ex){session.Hint=ex.Message;session.Log(ex.Message,"ERROR");}
        finally{session.Connecting=false;RefreshStatus();}
    }
    private void Files_Click(object sender,RoutedEventArgs e){if(_active is{} s)OpenProject(s);}
    private void OpenProject(TabSession s){if(Directory.Exists(s.Config.ProjectFolder))OpenPath(s.Config.ProjectFolder!);else{ShowPanel(s,"settings");s.Hint="Projektordner auswählen.";}}
    private void Logs_Click(object sender,RoutedEventArgs e){if(_active is{} s)ShowPanel(s,"logs");}
    private void Settings_Click(object sender,RoutedEventArgs e){if(_active is{} s)ShowPanel(s,"settings");}
    private void Info_Click(object sender,RoutedEventArgs e){if(_active is{} s)ShowPanel(s,"info");}
    private void Side_Resized(object sender,DragCompletedEventArgs e)=>Save();
    private void ShowPanel(TabSession session,string kind)
    {
        var exists=session.Cards.FirstOrDefault(c=>c.Kind==kind);if(exists is not null){exists.BringIntoView();return;}
        var layout=new PanelLayout{Kind=kind,Height=kind=="settings"?420:360};session.Config.Panels.Add(layout);var card=CreatePanel(session,layout);if(_active==session)Panels.Children.Add(card);Save();
    }
    private DockCard CreatePanel(TabSession s,PanelLayout layout)
    {
        var content=layout.Kind switch{"files"=>BuildFiles(s),"settings"=>BuildSettings(s),"logs"=>BuildLogs(s),_=>BuildInfo(s)};
        var title=layout.Kind switch{"files"=>"Projektdateien","settings"=>"Einstellungen","logs"=>"Protokolle",_=>"Info"};
        var restoreDetached=layout.Detached;layout.Detached=false;
        var card=new DockCard(s.Config.Id,title,layout,content);card.SetOwnerActive(_active==s);s.Cards.Add(card);
        card.Closed+=c=>{c.DisposeCard();if(c.Parent is Panel parent)parent.Children.Remove(c);s.Cards.Remove(c);s.Config.Panels.Remove(c.Layout);Save();};
        card.LayoutChanged+=()=>{if(_active==s){var ordered=Panels.Children.OfType<DockCard>().Where(c=>c.SessionId==s.Config.Id).Select(c=>c.Layout).ToList();if(ordered.Count==s.Config.Panels.Count)s.Config.Panels=ordered;}Save();};
        if(restoreDetached)Dispatcher.BeginInvoke(()=>{if(!s.Disposed)card.ToggleFloat();});return card;
    }
    private void Rebind(TabSession session,string root)
    {
        root=ProjectRootPolicy.Validate(root);
        var c=session.Config;var old=(c.ProjectFolder,c.BridgeBin,c.BridgeToken,c.BridgeEncrypted,c.ConnectionId,c.ConnectedChatAddress);
        c.ProjectFolder=Path.GetFullPath(root);c.BridgeBin=RelayClient.NeuerName();c.BridgeToken=RelayClient.NeuesKennwort();c.BridgeEncrypted=true;c.ConnectionId="";c.ConnectedChatAddress="";
        try{App.Services.SettingsStore.Save(_settings);}
        catch{(c.ProjectFolder,c.BridgeBin,c.BridgeToken,c.BridgeEncrypted,c.ConnectionId,c.ConnectedChatAddress)=old;throw;}
        session.StartProject();
    }
    private FrameworkElement BuildSettings(TabSession s)
    {
        var box=new StackPanel();AddProductSettings(s,box);box.Children.Add(Label("PROJEKT DIESER SITZUNG"));
        var root=new TextBlock{Text=s.Config.ProjectFolder??"Noch nicht gewählt",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,8)};box.Children.Add(root);
        box.Children.Add(ActionButton("Projekt auswählen …",()=>
        {
            if(s.Connecting)return;var dialog=new Microsoft.Win32.OpenFolderDialog{Title=L("Projekt für")+" "+s.Config.Beschriftung(),InitialDirectory=s.Config.ProjectFolder??Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)};
            if(dialog.ShowDialog(this)!=true)return;
            var folder=Path.GetFullPath(dialog.FolderName);
            if(!string.Equals(folder,s.Config.ProjectFolder,StringComparison.OrdinalIgnoreCase))
            {
                Rebind(s,folder);s.Hint="Projekt geändert: alte Chat-Kapsel ungültig. Jetzt Connect drücken und den neuen Text im selben Chat senden. Keine automatische Workspace-Umleitung.";
            }
            root.Text=folder;
        }));
        box.Children.Add(AsyncButton("Projekt öffnen",async()=>{await ChooseProjectAsync(s);}));
        var auto=new CheckBox{Content="Projekt-Downloads automatisch übernehmen",IsChecked=s.Config.AutoImport,Margin=new Thickness(0,16,0,8)};
        auto.Checked+=(_,_)=>{s.Config.AutoImport=true;s.ApplyOptions();Save();};auto.Unchecked+=(_,_)=>{s.Config.AutoImport=false;s.ApplyOptions();Save();};box.Children.Add(auto);
        var downloads=new CheckBox{Content="Downloads in Projekt-Inbox ablegen",IsChecked=s.Config.RouteDownloads,Margin=new Thickness(0,0,0,12)};
        downloads.Checked+=(_,_)=>{s.Config.RouteDownloads=true;s.ApplyOptions();Save();};downloads.Unchecked+=(_,_)=>{s.Config.RouteDownloads=false;s.ApplyOptions();Save();};box.Children.Add(downloads);
        box.Children.Add(new TextBlock{Text="Jeder Tab hat ein eigenes Profil. Neue Tabs können die Arena-Anmeldung einmalig übernehmen (Shift+Plus: ohne Übernahme). Sicherung vor Dateiänderungen bleibt aktiv. Keine rc27-Einstellungen werden übernommen.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,14,0,0),Foreground=Brush("Brush.TextMuted")});
        return new ScrollViewer{Content=box,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    }
    private FrameworkElement BuildFiles(TabSession s)
    {
        var grid=new DockPanel();var row=new StackPanel{Orientation=Orientation.Horizontal};DockPanel.SetDock(row,Dock.Top);grid.Children.Add(row);var tree=new TreeView{Background=Brush("Brush.Card"),BorderThickness=new Thickness(0)};
        void Fill(){tree.Items.Clear();if(s.Config.ProjectFolder is{} root&&Directory.Exists(root))LoadDirectory(tree.Items,root,s);}
        row.Children.Add(ActionButton("Explorer",()=>OpenProject(s)));row.Children.Add(ActionButton("Aktualisieren",Fill));grid.Children.Add(tree);Fill();return grid;
    }
    private void LoadDirectory(ItemCollection items,string folder,TabSession s)
    {
        try
        {
            foreach(var file in Directory.EnumerateFileSystemEntries(folder).Where(p=>(File.GetAttributes(p)&FileAttributes.ReparsePoint)==0).OrderBy(p=>!Directory.Exists(p)).ThenBy(p=>Path.GetFileName(p),StringComparer.OrdinalIgnoreCase).Take(500))
            {
                var directory=Directory.Exists(file);var item=new TreeViewItem{Header=(directory?"▸ ":"  ")+Path.GetFileName(file),ToolTip=Path.GetRelativePath(s.Config.ProjectFolder!,file),Tag=file};items.Add(item);
                if(directory){item.Items.Add("…");item.Expanded+=(_,e)=>{if(item.Items.Count==1&&item.Items[0] is string){item.Items.Clear();LoadDirectory(item.Items,file,s);}e.Handled=true;};}
                else item.MouseDoubleClick+=(_,e)=>{if(BridgeProtocol.SichererPfad(s.Config.ProjectFolder!,Path.GetRelativePath(s.Config.ProjectFolder!,file),out var pathError))OpenPath(file);e.Handled=true;};
            }
        }catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){items.Add("Nicht lesbar: "+ex.Message);}
    }
    private FrameworkElement BuildLogs(TabSession s)
    {
        var dock=new DockPanel();var buttons=new StackPanel{Orientation=Orientation.Horizontal};DockPanel.SetDock(buttons,Dock.Top);buttons.Children.Add(ActionButton("Protokoll öffnen",()=>{Directory.CreateDirectory(Path.GetDirectoryName(s.EventLog)!);if(!File.Exists(s.EventLog))File.WriteAllText(s.EventLog,"");OpenPath(s.EventLog);}));buttons.Children.Add(ActionButton("Protokollordner öffnen",()=>{Directory.CreateDirectory(Path.GetDirectoryName(s.EventLog)!);OpenPath(Path.GetDirectoryName(s.EventLog)!);}));dock.Children.Add(buttons);
        var list=new ItemsControl{ItemsSource=s.Events};var template=new DataTemplate(typeof(SessionEvent));
        var border=new FrameworkElementFactory(typeof(Border));border.SetValue(Border.BorderThicknessProperty,new Thickness(0,0,0,1));border.SetValue(Border.BorderBrushProperty,Brush("Brush.Border"));border.SetValue(Border.PaddingProperty,new Thickness(0,10,0,10));
        var stack=new FrameworkElementFactory(typeof(StackPanel));border.AppendChild(stack);
        void Field(string binding,string brush,bool mono=false){var text=new FrameworkElementFactory(typeof(TextBlock));text.SetBinding(TextBlock.TextProperty,new Binding(binding){StringFormat=binding=="Time"?"{0:dd.MM. HH:mm:ss}":null});if(binding=="Message")
            {
                var style=new Style(typeof(TextBlock));style.Setters.Add(new Setter(TextBlock.ForegroundProperty,Brush(brush)));
                foreach(var pair in new[]{("ERROR","#F27D81"),("WARN","#E6B869"),("SUCCESS","#75D7AB")}){var trigger=new DataTrigger{Binding=new Binding("Level"),Value=pair.Item1};trigger.Setters.Add(new Setter(TextBlock.ForegroundProperty,Brush(pair.Item2)));style.Triggers.Add(trigger);}
                text.SetValue(FrameworkElement.StyleProperty,style);
            }
            else text.SetValue(TextBlock.ForegroundProperty,Brush(brush));text.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);text.SetValue(TextBlock.MarginProperty,new Thickness(0,0,0,5));if(mono)text.SetValue(TextBlock.FontFamilyProperty,new FontFamily("Consolas"));stack.AppendChild(text);}
        Field("Time","Brush.TextDim");Field("Message","Brush.Text");Field("Path","Brush.Accent",true);template.VisualTree=border;list.ItemTemplate=template;dock.Children.Add(new ScrollViewer{Content=list,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});return dock;
    }
    private FrameworkElement BuildInfo(TabSession s)
    {
        var text=_settings.UiLanguage=="en"?"AXON RC30\n\nIndependent of rc27. Connector: AXON.Connector.dll. Encrypted file relay, project-bound jobs, persistent journal and mandatory backups. No process sandbox.\n\nConnect inserts the handover text into the current editor after your click; you send it yourself. Only the current chat URL/title are detected; no message extraction. New tabs can copy Arena cookies once from the active RC30 tab, then profiles remain independent. Shift+Plus skips copying.\n\nA full chat archive is not claimed. Local workspace events are logged separately.\n\nRC30 data: "+Rc30Paths.DataRoot+"\n\nBrowser profile: "+Rc30Paths.ProfileFolder(s.Config.Id):"AXON RC30\n\nUnabhängig von rc27. Connector: AXON.Connector.dll. Verschlüsselter Dateibriefkasten, projektgebundene Aufträge, persistentes Journal und Pflichtsicherung. Keine Prozess-Sandbox.\n\nConnect setzt den Übergabetext auf deinen Klick ins Eingabefeld; absenden musst du selbst. Nur aktuelle Chat-URL und aktueller Chatname werden erkannt; keine Nachrichtenauslese oder Verlaufsextraktion. Neue Tabs übernehmen einmalig Arena-Cookies aus dem aktiven RC30-Tab; danach bleiben Profile getrennt. Shift+Plus startet ohne Übernahme.\n\nEin vollständiges Chatname.md-Archiv wird nicht vorgetäuscht. Die lokalen Ereignisse werden pro Sitzung protokolliert.\n\nRC30-Daten: "+Rc30Paths.DataRoot+"\n\nBrowserprofil: "+Rc30Paths.ProfileFolder(s.Config.Id);
        return new ScrollViewer{Content=new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap,Foreground=Brush("Brush.TextMuted")},VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    }
    private static TextBlock Label(string text)=>new(){Text=L(text),FontWeight=FontWeights.SemiBold,FontSize=11};
    private Button ActionButton(string title,Action action){var b=new Button{Content=L(title),Padding=new Thickness(9,6,9,6),Margin=new Thickness(0,0,6,6)};b.Click+=(_,_)=>{try{action();}catch(Exception ex){Report(ex);}};return b;}
    private string? AskText(string title,string initial)
    {
        var field=new TextBox{Text=initial,Margin=new Thickness(16),Padding=new Thickness(8)};var panel=new StackPanel();panel.Children.Add(field);var dialog=new Window{Title=L(title),Width=420,SizeToContent=SizeToContent.Height,Owner=this,WindowStartupLocation=WindowStartupLocation.CenterOwner,ResizeMode=ResizeMode.NoResize,Background=Brush("Brush.Surface"),Foreground=Brush("Brush.Text")};
        var ok=new Button{Content=L("Übernehmen"),IsDefault=true,Margin=new Thickness(16,0,16,16),Padding=new Thickness(8)};ok.Click+=(_,_)=>dialog.DialogResult=true;panel.Children.Add(ok);dialog.Content=panel;dialog.Loaded+=(_,_)=>{field.Focus();field.SelectAll();};return dialog.ShowDialog()==true?field.Text:null;
    }
    private void OpenPath(string path){try{Process.Start(new ProcessStartInfo(path){UseShellExecute=true});}catch(Exception ex){Report(ex);}}
    private void Report(Exception ex){App.Services.Logger.Write("ERROR",ex.ToString());if(_active is{} s){s.Hint=ex.Message;s.Log(ex.Message,"ERROR");}else Footer.Text=ex.Message;}
    private void Window_Closing(object? sender,CancelEventArgs e)
    {
        _productLifetime.Cancel();
        _closing=true;_timer.Stop();Save();BrowserHost.Children.Clear();foreach(var session in _sessions.Values)session.Dispose();
    }
}
