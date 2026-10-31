using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using ArenaDesktopCompanion.Core.Product;
using ArenaDesktopCompanion.Product;
using ArenaDesktopCompanion.Workspace;
namespace ArenaDesktopCompanion;

public partial class MainWindow
{
    private readonly CancellationTokenSource _productLifetime=new();
    private readonly GitHubService _github=new();
    private static string L(string value)=>UiText.Translate(value);
    private Button AsyncButton(string title,Func<Task> action)
    {
        var b=new Button{Content=L(title),Padding=new Thickness(9,6,9,6),Margin=new Thickness(0,4,6,6)};
        b.Click+=async(_,_)=>{b.IsEnabled=false;try{await action();}catch(OperationCanceledException){}catch(Exception ex){Report(ex);}finally{if(!_closing)b.IsEnabled=true;}};return b;
    }
    private Window ProductDialog(string title,FrameworkElement content,double width=560)
    {
        var d=new Window{Title="AXON RC30 · "+L(title),Owner=this,Width=width,Height=660,MinHeight=300,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=Brush("Brush.Surface"),Foreground=Brush("Brush.Text"),Content=new ScrollViewer{Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}};
        d.Loaded+=(_,_)=>UiText.Apply(d);return d;
    }
    private async Task<bool> ChooseProjectAsync(TabSession session)
    {
        if(session.Disposed)return false;
        var box=new StackPanel{Margin=new Thickness(18)};var dialog=ProductDialog("Projekt auswählen …",box);var chosen=false;
        var path=new TextBlock{Text=L("Bitte zuerst einen Projektordner auswählen."),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,14)};box.Children.Add(path);
        void Bind(string folder,string repository=""){if(session.Disposed)throw new InvalidOperationException("Workspace closed.");Rebind(session,folder);session.Config.RepositoryUrl=repository;Save();chosen=true;dialog.DialogResult=true;}
        box.Children.Add(AsyncButton("Ordner auswählen",()=>
        {
            var picker=new Microsoft.Win32.OpenFolderDialog{Title=L("Ordner auswählen")};if(picker.ShowDialog(dialog)==true)Bind(picker.FolderName);return Task.CompletedTask;
        }));
        var url=new TextBox{Margin=new Thickness(0,10,0,4),Padding=new Thickness(6),ToolTip="https://github.com/owner/repository"};box.Children.Add(url);
        box.Children.Add(AsyncButton("GitHub-Link laden",async()=>{path.Text="GitHub clone …";var repository=ProductPolicy.ParseRepository(url.Text);var folder=await _github.CloneAsync(url.Text,_productLifetime.Token);Bind(folder,"https://github.com/"+repository);}));
        box.Children.Add(AsyncButton("Automatisch erstellen",()=>{Bind(AutomaticProjects.Create(AppContext.BaseDirectory));return Task.CompletedTask;}));
        box.Children.Add(new TextBlock{Text=Path.Combine(AppContext.BaseDirectory,".axon","projects","0001.axon.dd-MM-yyyy-HH-mm"),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,12)});
        box.Children.Add(new TextBlock{Text=_settings.UiLanguage=="en"?"Automatic numbering is persistent and locked across processes. A non-writable program directory is reported; no silent fallback or administrator prompt. Cloning never executes project code. Git and a GitHub login are required for private repositories.":"Die Nummerierung wird dauerhaft und prozessübergreifend gesperrt gespeichert. Ein nicht beschreibbares Programmverzeichnis wird gemeldet; kein stiller Ausweichpfad oder Adminprompt. Klonen führt keinen Projektcode aus. Git und für private Repositories eine GitHub-Anmeldung werden benötigt.",TextWrapping=TextWrapping.Wrap});
        box.Children.Add(ActionButton(L("Abbrechen"),()=>dialog.Close()));
        dialog.ShowDialog();await Task.CompletedTask;return chosen;
    }
    private void AddProductSettings(TabSession session,StackPanel box)
    {
        box.Children.Add(Label("AXON RC30 · "+ProductPolicy.CurrentVersion.ToString(3)));
        box.Children.Add(Label(L("Sprache")));var language=new ComboBox{ItemsSource=new[]{"Deutsch","English"},SelectedIndex=_settings.UiLanguage=="en"?1:0,Margin=new Thickness(0,4,0,10)};
        language.SelectionChanged+=(_,_)=>{_settings.UiLanguage=language.SelectedIndex==1?"en":"de";Save();UiText.Apply(this);Dispatcher.BeginInvoke(()=>{foreach(var s in _sessions.Values)foreach(var card in s.Cards.Where(c=>c.Kind is "settings" or "info").ToArray())card.ReplaceContent(card.Kind=="settings"?BuildSettings(s):BuildInfo(s));});};box.Children.Add(language);
        box.Children.Add(AsyncButton("Offizielles Repository",()=>OpenProductLinkAsync(ProductPolicy.RepositoryUrl,session)));
        var account=new TextBlock{Text="GitHub · "+L("GitHub-Status prüfen"),TextWrapping=TextWrapping.Wrap};box.Children.Add(account);
        box.Children.Add(AsyncButton("GitHub-Status prüfen",async()=>{account.Text="GitHub: "+await _github.AccountAsync(_productLifetime.Token);}));
        box.Children.Add(AsyncButton("GitHub verbinden",async()=>
        {
            var content=new StackPanel{Margin=new Thickness(18)};var state=new TextBlock{Text=L("GitHub-Freigabe läuft. Code auf der offiziellen GitHub-Seite bestätigen."),TextWrapping=TextWrapping.Wrap};content.Children.Add(state);
            var dialog=ProductDialog("GitHub verbinden",content);using var cancel=CancellationTokenSource.CreateLinkedTokenSource(_productLifetime.Token);
            content.Children.Add(AsyncButton("GitHub.com",()=>OpenProductLinkAsync("https://github.com/login/device",session)));
            dialog.Closed+=(_,_)=>cancel.Cancel();
            dialog.Loaded+=async(_,_)=>
            {
                try
                {
                    await _github.LoginAsync(line=>Dispatcher.BeginInvoke(()=>
                    {
                        if(!dialog.IsVisible)return;
                        var code=Regex.Match(line,@"\b[A-Z0-9]{4}-[A-Z0-9]{4}\b");if(code.Success){Clipboard.SetText(code.Value);state.Text="https://github.com/login/device\n\n"+code.Value+"\n\n"+L("GitHub-Freigabe läuft. Code auf der offiziellen GitHub-Seite bestätigen.");}
                    }),cancel.Token);
                    account.Text="GitHub: "+await _github.AccountAsync(cancel.Token);dialog.Close();
                }
                catch(OperationCanceledException){}catch(Exception ex){state.Text=ex.Message;}
            };
            dialog.ShowDialog();await Task.CompletedTask;
        }));
        UpdateOffer? offer=null;var updateState=new TextBlock{Text="",TextWrapping=TextWrapping.Wrap};box.Children.Add(updateState);
        var update=AsyncButton("Update",async()=>
        {
            if(offer is null)return;
            if(_sessions.Values.Any(s=>s.Connecting||s.Host?.Dienst?.HasRunningCommands==true))throw new InvalidOperationException(L("Laufende Aufträge zuerst abschließen lassen."));
            updateState.Text="Download + SHA-256 …";var msi=await _github.DownloadUpdateAsync(offer,_productLifetime.Token);
            if(_sessions.Values.Any(s=>s.Connecting||s.Host?.Dienst?.HasRunningCommands==true))throw new InvalidOperationException(L("Laufende Aufträge zuerst abschließen lassen."));
            var apply=Path.Combine(AppContext.BaseDirectory,"Product","Apply-Update.ps1");if(!File.Exists(apply))throw new FileNotFoundException("Update helper missing.");
            var log=Path.Combine(Core.Settings.Rc30Paths.DataRoot,"Updates","last-update.json");
            var start=new ProcessStartInfo("powershell.exe"){UseShellExecute=false,CreateNoWindow=true};
            foreach(var a in new[]{"-NoProfile","-ExecutionPolicy","Bypass","-File",apply,"-Package",msi,"-Sha256",offer.Sha256,"-ParentId",Environment.ProcessId.ToString(),"-Executable",Path.Combine(AppContext.BaseDirectory,"AXON.RC30.exe"),"-Result",log})start.ArgumentList.Add(a);
            Save();Process.Start(start);Application.Current.Shutdown();
        });update.IsEnabled=false;
        box.Children.Add(AsyncButton("Update im Repository prüfen",async()=>{offer=await _github.CheckUpdateAsync(_productLifetime.Token);updateState.Text=offer is null?L("Kein neueres kompatibles Update vorhanden."):"AXON RC30 "+offer.Version+"\n"+(_settings.UiLanguage=="en"?"Unsigned MSI bootstrapper. Update closes and restarts RC30; profiles/projects are retained.":"Unsignierter MSI-Bootstrapper. Update schließt und startet RC30 neu; Profile/Projekte bleiben erhalten.");update.IsEnabled=offer is not null;}));box.Children.Add(update);
        var remember=new CheckBox{Content=L("Anmeldung merken"),IsChecked=session.Config.RememberLogin!=false,Margin=new Thickness(0,12,0,8)};
        async void RememberChanged(object sender,RoutedEventArgs e)
        {
            try { session.Config.RememberLogin=remember.IsChecked==true;Save();
            if(!session.Config.RememberLogin.Value&&session.Browser?.CoreWebView2 is{} core)await core.Profile.ClearBrowsingDataAsync();
            session.Hint=L("Anmeldung geändert; gilt beim nächsten Öffnen dieses Tabs."); } catch(Exception ex){Report(ex); }
        }
        remember.Checked+=RememberChanged;remember.Unchecked+=RememberChanged;box.Children.Add(remember);
        box.Children.Add(Label(L("Berechtigungen")));var access=new ComboBox{ItemsSource=new[]{L("Nur lesen"),L("Projektdateien ändern"),L("Lokale Programme ausführen")},SelectedIndex=session.Config.AccessProfile switch{"write"=>1,"full"=>2,_=>0},Margin=new Thickness(0,4,0,8)};
        access.SelectionChanged+=(_,_)=>{session.Config.AccessProfile=new[]{"read","write","full"}[access.SelectedIndex];Save();if(session.Host?.Dienst is{} service){service.BefehleErlaubt=session.Config.AccessProfile=="full";service.VorschlaegeErlaubt=session.Config.AccessProfile=="full";service.SchreibendErlaubt=session.Config.AccessProfile is "write" or "full";}};box.Children.Add(access);
        box.Children.Add(new TextBlock{Text=_settings.UiLanguage=="en"?"Run local programs grants Windows-user rights, not a sandbox. No per-job popups; pairing and path checks still apply.":"Lokale Programme nutzen Windows-Benutzerrechte, keine Sandbox. Keine Rückfrage pro Auftrag; Paarungs- und Pfadprüfungen bleiben aktiv.",TextWrapping=TextWrapping.Wrap});
        box.Children.Add(AsyncButton("Projekt als GitHub-Repository veröffentlichen",()=>ShowPublishWizardAsync(session)));
    }
    private async Task OpenProductLinkAsync(string uri,TabSession session)
    {
        await session.EnsureBrowserAsync();if(session.Navigation is{} navigation)await navigation.OpenLinkAsync(uri);
    }
    private async Task ShowPublishWizardAsync(TabSession session)
    {
        if(session.Config.ProjectFolder is not{} root||!Directory.Exists(root))throw new InvalidOperationException(L("Bitte zuerst einen Projektordner auswählen."));
        var account=await _github.AccountAsync(_productLifetime.Token);var files=await Task.Run(()=>GitHubPublisher.ReviewFiles(root),_productLifetime.Token);
        var box=new StackPanel{Margin=new Thickness(18)};var dialog=ProductDialog("Projekt als GitHub-Repository veröffentlichen",box,720);
        box.Children.Add(new TextBlock{Text="GitHub: "+account+"\n"+root,TextWrapping=TextWrapping.Wrap});box.Children.Add(Label(L("Repositoryname")));var name=new TextBox{Text=Path.GetFileName(root),Margin=new Thickness(0,4,0,8)};box.Children.Add(name);
        box.Children.Add(Label(L("Beschreibung")));var description=new TextBox{Margin=new Thickness(0,4,0,8)};box.Children.Add(description);
        var privacy=new CheckBox{Content=L("Privates Repository"),IsChecked=true,Margin=new Thickness(0,4,0,8)};box.Children.Add(privacy);
        box.Children.Add(new TextBlock{Text=L("GitHub-Upload: Dateiauswahl prüfen; Secrets und Profile niemals veröffentlichen."),TextWrapping=TextWrapping.Wrap});
        var selectAll=new CheckBox{Content=_settings.UiLanguage=="en"?"Select all reviewed files":"Alle geprüften Dateien auswählen",Margin=new Thickness(0,10,0,6)};box.Children.Add(selectAll);
        var choices=new List<CheckBox>();foreach(var f in files){var choice=new CheckBox{Content=f,IsChecked=false,Tag=f};choices.Add(choice);box.Children.Add(choice);}
        selectAll.Checked+=(_,_)=>{foreach(var c in choices)c.IsChecked=true;};selectAll.Unchecked+=(_,_)=>{foreach(var c in choices)c.IsChecked=false;};
        var state=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,10)};box.Children.Add(state);
        box.Children.Add(AsyncButton("Ausgewählte Dateien veröffentlichen",async()=>
        {
            var selected=choices.Where(c=>c.IsChecked==true).Select(c=>(string)c.Tag).ToArray();ProductPolicy.ValidateRepositoryName(name.Text);
            var summary=account+"/"+name.Text+"\n"+(privacy.IsChecked==true?"PRIVATE":"PUBLIC")+"\n"+selected.Length+" files\n\n"+(_settings.UiLanguage=="en"?"Create this NEW repository and upload exactly these files? Existing repositories will not be overwritten.":"Dieses NEUE Repository erstellen und genau diese Dateien hochladen? Vorhandene Repositories werden nicht überschrieben.");
            if(MessageBox.Show(dialog,summary,"AXON RC30",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
            state.Text="Upload …";var result=await new GitHubPublisher().PublishAsync(root,name.Text,description.Text,privacy.IsChecked==true,selected,_productLifetime.Token);state.Text=result;
        }));dialog.ShowDialog();
    }
}
