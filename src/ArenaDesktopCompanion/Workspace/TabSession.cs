using System.Collections.ObjectModel;
using AXON.Connector;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using System.IO;
using System.Windows;
using ArenaDesktopCompanion.Bridge;
using ArenaDesktopCompanion.Core.Agents;
using ArenaDesktopCompanion.Core.Bridge;
using ArenaDesktopCompanion.Core.Diagnostics;
using ArenaDesktopCompanion.Core.Settings;
using ArenaDesktopCompanion.Core.Sync;
using ArenaDesktopCompanion.WebView;
using Microsoft.Web.WebView2.Wpf;

namespace ArenaDesktopCompanion.Workspace;
public sealed record SessionEvent(DateTimeOffset Time,string Message,string Path,string Level);
public sealed class TabSession : IDisposable
{
    public Arbeitsbereich Config { get; }
    public WebView2? Browser { get; private set; }
    public event Action<WebView2>? BrowserCreated;
    public event Action<TabSession>? MetadataChanged;
    public event Action<TabSession>? ChatSwitched;
    private CancellationTokenSource? _metadataRead;
    private DateTimeOffset _metadataAt;
    private Task? _metadataTask;
    public BridgeHost? Host { get; private set; }
    public ProjectSyncService? Sync { get; private set; }
    public ObservableCollection<SessionEvent> Events { get; }=[];
    public string Hint { get; set; }="";
    public ConnectController Connector { get; }=new();
    public bool Connecting { get; set; }
    public string? ProjectError { get; private set; }
    public bool ScanComplete { get; private set; }
    public long FileCount { get; private set; }
    public long ByteCount { get; private set; }
    public string EventLog => Path.Combine(Rc30Paths.SessionFolder(Config.Id),"events.log");
    public CancellationTokenSource Lifetime { get; }=new();
    public bool Disposed { get; private set; }
    public ArenaNavigation? Navigation { get; private set; }
    public TabSession? LoginSource { get; set; }
    private Task? _browserTask;
    private readonly FileLogger _logger;
    private readonly AppServices _services;
    private readonly DownloadRouter _downloads;
    private Task? _scan;
    private DateTimeOffset _scanAt;
    public List<DockCard> Cards { get; }=[];
    public TabSession(Arbeitsbereich config,AppServices services)
    {
        Config=config;_services=services;_logger=new FileLogger(Rc30Paths.SessionFolder(config.Id),"events.log");_downloads=new DownloadRouter(_logger);_downloads.Status+=m=>Log(m);
    }
    public Task EnsureBrowserAsync()=>_browserTask??=InitializeBrowserAsync();
    private async Task InitializeBrowserAsync()
    {
        var (browser,error)=WebView2Bootstrapper.CreateBrowser(_logger);
        if(browser is null){Log(error??"Browser fehlt","ERROR");return;}
        if(Config.RememberLogin is null)
        {
            Config.RememberLogin=Environment.GetEnvironmentVariable("AXON_RC30_SMOKE")=="1"?true:System.Windows.MessageBox.Show(Application.Current.MainWindow,Product.UiText.Translate("Anmeldung für diesen Tab merken und eingeloggt bleiben?")+"\n\n"+Product.UiText.Translate("Ja: privates Profil bleibt gespeichert. Nein: InPrivate-Sitzung ohne dauerhafte Anmeldung."),"AXON RC30",MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes;
            MetadataChanged?.Invoke(this);
        }
        browser.CreationProperties=new CoreWebView2CreationProperties{IsInPrivateModeEnabled=Config.RememberLogin==false};
        Browser=browser;BrowserCreated?.Invoke(browser);
        var result=await WebView2Bootstrapper.InitializeAsync(browser,_logger,Rc30Paths.ProfileFolder(Config.Id),Environment.GetEnvironmentVariable("AXON_RC30_SMOKE")=="1"?"about:blank":ChatIdentity.Address(Config.ChatAddress)??ChatIdentity.Address(Config.ConnectedChatAddress)??ChatIdentity.Address(Config.LetzteAdresse)??"https://arena.ai/agent",async core=>
        {
            Navigation=new ArenaNavigation(browser);
            core.SourceChanged+=(_,_)=>ObserveChat(core);
            core.DocumentTitleChanged+=(_,_)=>QueueChatName(core);
            core.NavigationCompleted+=(_,e)=>{if(Disposed)return;ObserveChat(core);if(!e.IsSuccess)Log("Navigation: "+e.WebErrorStatus,"WARN");};
            var source=LoginSource;LoginSource=null;
            if(source is null||source.Disposed||source.Config.RememberLogin==false||Config.RememberLogin==false)return;
            try{await source.EnsureBrowserAsync();if(!source.Disposed&&source.Browser?.CoreWebView2 is{} parent&&!Disposed){var copied=await ArenaLoginBootstrap.CopyAsync(parent,core);Hint=copied>0?"Arena-Anmeldung einmalig übernommen; Browserprofile bleiben getrennt.":"Keine Arena-Cookies übernommen; ggf. einmal anmelden.";Log("Einmalige Arena-Cookieübernahme: "+copied+" Einträge (keine Werte protokolliert)");}}
            catch(Exception ex){Hint="Anmeldung konnte nicht übernommen werden: "+ex.Message;Log(Hint,"WARN");}
        });
        if(Disposed){browser.Dispose();return;}
        if(!result.Success||browser.CoreWebView2 is null){Log(result.ErrorMessage??"Browser nicht bereit","ERROR");return;}
        var core=browser.CoreWebView2;
        ObserveChat(core);

        core.DownloadStarting+=(_,e)=>{if(!Disposed)_downloads.OnDownloadStarting(e);};
        // No generic DOM scrape or unapproved automatic send. All callbacks retain this owner.
        Log("Privates Browserprofil bereit");
    }
    public void ObserveChat(CoreWebView2 core)
    {
        if(Disposed||ChatIdentity.Address(core.Source) is not{} address)return;
        var changed=Config.ChatAddress!=address||Config.LetzteAdresse!=address;
        Config.ChatAddress=address;Config.LetzteAdresse=address;
        if(changed&&!ChatIdentity.IsChat(address)&&Config.AutoChatName)Config.Name="Neuer Chat";
        var bound=ChatIdentity.Address(Config.ConnectedChatAddress);
        if(bound=="https://arena.ai/agent"&&ChatIdentity.IsChat(address)){Config.ConnectedChatAddress=address;changed=true;}
        else if(ChatIdentity.IsChat(bound)&&ChatIdentity.IsChat(address)&&bound!=address)ChatSwitched?.Invoke(this);
        if(changed){Log("Chatadresse automatisch erkannt: "+address);MetadataChanged?.Invoke(this);}
        QueueChatName(core);
    }
    public void PollChatMetadata()
    {
        if(!Disposed&&Browser?.CoreWebView2 is{} core&&(_metadataTask is null||_metadataTask.IsCompleted)&&DateTimeOffset.UtcNow-_metadataAt>TimeSpan.FromSeconds(8))QueueChatName(core);
    }
    public void RefreshChatName(){if(Browser?.CoreWebView2 is{} core)QueueChatName(core);}
    private void QueueChatName(CoreWebView2 core)
    {
        if(Disposed||!Config.AutoChatName||!ChatIdentity.IsChat(core.Source))return;
        var address=ChatIdentity.Address(core.Source)!;_metadataRead?.Cancel();_metadataRead?.Dispose();_metadataRead=CancellationTokenSource.CreateLinkedTokenSource(Lifetime.Token);var token=_metadataRead.Token;
        _metadataAt=DateTimeOffset.UtcNow;_metadataTask=ReadChatNameAsync(core,address,token);
    }
    private async Task ReadChatNameAsync(CoreWebView2 core,string address,CancellationToken token)
    {
        try
        {
            await Task.Delay(350,token);var label=JsonSerializer.Deserialize<string>(await core.ExecuteScriptAsync(ArenaChatMetadata.NameScript(address)));
            if(Disposed||token.IsCancellationRequested||!Config.AutoChatName||ChatIdentity.Address(core.Source)!=address)return;
            var title=ChatIdentity.Name(label)??ChatIdentity.Title(core.DocumentTitle);
            if(title is not null&&Config.Name!=title){Config.Name=title;MetadataChanged?.Invoke(this);}
        }
        catch(OperationCanceledException){}catch(Exception ex)when(ex is InvalidOperationException or System.Runtime.InteropServices.COMException or JsonException){if(!Disposed)Log("Chatname derzeit nicht verfügbar: "+ex.Message,"WARN");}
    }
    public void StartProject()
    {
        ProjectError=null;
        try{StartProjectCore();}
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException)
        {
            Host?.Dispose();Host=null;Sync?.Dispose();Sync=null;_downloads.IsEnabled=false;
            ProjectError="Projektstart fehlgeschlagen ("+ex.GetType().Name+"). Journal/Originaldaten bleiben erhalten.";Hint=ProjectError;Log(ProjectError,"ERROR");
        }
    }
    private void StartProjectCore()
    {
        Host?.Dispose();Host=null;Sync?.Dispose();Sync=null;ScanComplete=false;FileCount=0;ByteCount=0;_scanAt=DateTimeOffset.MinValue;
        if(string.IsNullOrWhiteSpace(Config.ProjectFolder)||!Directory.Exists(Config.ProjectFolder))return;
        try{ProjectRootPolicy.Validate(Config.ProjectFolder);}catch(InvalidOperationException ex){Hint=ex.Message;Log(Hint,"WARN");return;}
        var local=new AppSettings{ProjectFolder=Config.ProjectFolder,AutoImport=Config.AutoImport,KeepBackups=true,BackupRunsToKeep=Config.BackupCount,RouteDownloadsToInbox=Config.RouteDownloads};
        Sync=new ProjectSyncService(Config.ProjectFolder,local,m=>Log(m),_logger);Sync.ImportFinished+=run=>{Log(run.Summarize());RequestScan(true);};Sync.ApplySettings();
        _downloads.Bind(Config.ProjectFolder);_downloads.IsEnabled=Config.RouteDownloads;
        Host=new BridgeHost(_services,Config){Meldung=m=>Log(m)};Host.Start();
        if(Host.Dienst is { } service)service.AcceptedJob+=job=>{if(job.Path is not null)Log(job.Kind=="write"?"Datei geschrieben":"Datei gelesen","INFO",job.Path);};
        RequestScan(true);Log("Projekt gebunden","INFO",Config.ProjectFolder);
    }
    public void ApplyOptions(){if(Sync is null)return;Sync.Settings.AutoImport=Config.AutoImport;Sync.Settings.BackupRunsToKeep=Config.BackupCount;Sync.ApplySettings();_downloads.IsEnabled=Config.RouteDownloads;}
    public void RequestScan(bool force=false)
    {
        if(Disposed||ProjectError is not null||string.IsNullOrWhiteSpace(Config.ProjectFolder)||(_scan is{IsCompleted:false})||(!force&&DateTimeOffset.UtcNow-_scanAt<TimeSpan.FromSeconds(30)))return;
        _scanAt=DateTimeOffset.UtcNow;var root=Config.ProjectFolder;
        _scan=Task.Run(()=>{try{var manifest=WorkspaceManifestBuilder.Build(root);var count=manifest.Counts.GetValueOrDefault("files");var size=manifest.Counts.GetValueOrDefault("bytes");Application.Current.Dispatcher.BeginInvoke(()=>{if(!Disposed&&Config.ProjectFolder==root){FileCount=count;ByteCount=size;ScanComplete=true;}});}catch(Exception ex){Log("Dateibestand: "+ex.Message,"WARN");}});
    }
    public void Log(string message,string level="INFO",string file="")
    {
        if(Disposed)return;message=SecretRedaction.Clean(message);_logger.Write(level,message+(file.Length>0?" · "+file:""));
        Application.Current.Dispatcher.BeginInvoke(()=>{if(Disposed)return;Events.Insert(0,new SessionEvent(DateTimeOffset.Now,message,TabOrder.ShortPath(file),level));while(Events.Count>250)Events.RemoveAt(Events.Count-1);});
    }
    public void Dispose(){if(Disposed)return;Disposed=true;_metadataRead?.Cancel();_metadataRead?.Dispose();Lifetime.Cancel();Host?.Dispose();Sync?.Dispose();foreach(var card in Cards)card.DisposeCard();Navigation?.Dispose();Browser?.Dispose();_logger.Dispose();Lifetime.Dispose();}
}
