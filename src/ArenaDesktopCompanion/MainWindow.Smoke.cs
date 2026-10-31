using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using ArenaDesktopCompanion.Core.Settings;
using ArenaDesktopCompanion.Workspace;
using Microsoft.Web.WebView2.Core;
using ArenaDesktopCompanion.WebView;
namespace ArenaDesktopCompanion;

/// <summary>Opt-in Windows UI integration. Dedicated RC30.Smoke data; never visits Arena.</summary>
public partial class MainWindow
{
    private async Task RunUiSmokeAsync()
    {
        var checks=new List<string>();string? error=null;
        void Check(bool value,string name){if(!value)throw new InvalidOperationException(name);checks.Add(name);}
        try
        {
            Check(Brush("#F27D81") is System.Windows.Media.SolidColorBrush,"Hex-Farbwerte und benannte Ressourcen getrennt aufgelöst");
            Check(IsLoaded&&IsVisible,"WPF-Fenster aus XAML sichtbar geladen");
            while(_sessions.Count<3){var index=_settings.ArbeitsbereichAnlegen();var tab=_settings.Arbeitsbereiche[index];var s=CreateSession(tab);_sessions.Add(tab.Id,s);}
            var selected=_sessions.Values.Take(3).ToArray();
            foreach(var s in selected)await s.EnsureBrowserAsync();
            Check(selected.All(s=>s.Browser?.CoreWebView2 is not null),"Drei WebView2-Sitzungen initialisiert");
            var origin=Path.Combine(Rc30Paths.DataRoot,"test-origin");Directory.CreateDirectory(origin);File.WriteAllText(Path.Combine(origin,"index.html"),"<!doctype html><html><body style='background:#0a0f16;color:#9fb2c6;font:20px Segoe UI;padding:80px'>AXON RC30 · Private Session Test<br><small>Eigene Profile · kein Arena-Zugriff</small><textarea aria-label='Message' oninput='window.inputs=(window.inputs||0)+1'></textarea><button id='send' onclick='window.sent=(window.sent||0)+1'>Send</button></body></html>");
            foreach(var s in selected)
            {
                var core=s.Browser!.CoreWebView2;core.SetVirtualHostNameToFolderMapping("sessions.axon.test",origin,CoreWebView2HostResourceAccessKind.DenyCors);
                var done=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void Complete(object? sender,CoreWebView2NavigationCompletedEventArgs e){core.NavigationCompleted-=Complete;if(e.IsSuccess)done.TrySetResult();else done.TrySetException(new Exception(e.WebErrorStatus.ToString()));}
                core.NavigationCompleted+=Complete;core.Navigate("https://sessions.axon.test/index.html");await done.Task.WaitAsync(TimeSpan.FromSeconds(30));
                await core.ExecuteScriptAsync("localStorage.setItem('owner',"+JsonSerializer.Serialize(s.Config.Id)+");sessionStorage.setItem('owner',"+JsonSerializer.Serialize(s.Config.Id)+");document.cookie='owner="+s.Config.Id+";path=/;SameSite=Strict'");
            }
            foreach(var s in selected)
            {
                Activate(s);await Task.Delay(150);
                var core=s.Browser!.CoreWebView2;var owner=JsonSerializer.Deserialize<string>(await core.ExecuteScriptAsync("localStorage.getItem('owner')"));
                Check(owner==s.Config.Id,"LocalStorage isoliert: "+s.Config.Id[..8]);
                var session=JsonSerializer.Deserialize<string>(await core.ExecuteScriptAsync("sessionStorage.getItem('owner')"));Check(session==s.Config.Id,"SessionStorage bleibt tabgebunden: "+s.Config.Id[..8]);
                var cookies=await core.CookieManager.GetCookiesAsync("https://sessions.axon.test");Check(cookies.Single(c=>c.Name=="owner").Value==s.Config.Id,"Cookies isoliert: "+s.Config.Id[..8]);
            }
            Activate(selected[0]);selected[0].Browser!.Focus();
            var ownerCore=selected[0].Browser!.CoreWebView2;var localUri=new Uri("https://sessions.axon.test/index.html");
            var draft="AXON Entwurf · äöü \" \nZweite Zeile\n";
            var insert=await DraftWriter.InsertAsync(ownerCore,draft,localUri);Check(insert.Ok,"Connect-Draft in echtes WebView2-Textarea eingesetzt und nachgeprüft");
            Check((await ownerCore.ExecuteScriptAsync("window.inputs>0"))=="true","Native Eingabewertänderung löst Input-Ereignisse aus");
            Check((await ownerCore.ExecuteScriptAsync("window.sent||0"))=="0","Draft-Einsetzen sendet nichts ab");
            await ownerCore.ExecuteScriptAsync("document.querySelector('textarea').value='Eigener ungesendeter Entwurf'");
            var preserve=await DraftWriter.InsertAsync(ownerCore,"nicht überschreiben",localUri);Check(!preserve.Ok&&preserve.Code=="draft_not_empty","Eigener vorhandener Entwurf wird nicht überschrieben");
            var wrong=await DraftWriter.InsertAsync(ownerCore,"nicht fremder Chat",new Uri("https://sessions.axon.test/anderer-chat"));Check(!wrong.Ok&&wrong.Code=="chat_changed","Navigationswechsel verweigert Schreiben in fremden Chat");
            await ownerCore.ExecuteScriptAsync("document.querySelector('textarea').value=''");
            await DraftWriter.InsertAsync(ownerCore,"Eigener AXON-Draft",localUri);var replace=await DraftWriter.InsertAsync(ownerCore,"Aktualisierter AXON-Draft",localUri);Check(replace.Ok,"Eigener unveränderter AXON-Draft kann gezielt aktualisiert werden");
            await ownerCore.ExecuteScriptAsync("document.querySelector('textarea').outerHTML='<div contenteditable=\"true\" role=\"textbox\" aria-label=\"Message\"></div>'");
            var rich=await DraftWriter.InsertAsync(ownerCore,draft,localUri);Check(rich.Ok,"Connect-Draft in Contenteditable-Editor eingesetzt und nachgeprüft: "+rich.Code+(rich.Ok?"":" / eigener Testeditor: "+await ownerCore.ExecuteScriptAsync("JSON.stringify({html:document.querySelector('[contenteditable]').innerHTML,text:document.querySelector('[contenteditable]').innerText})")));
            var actualPrompt=AXON.Connector.ConnectCapsule.Create(new Arbeitsbereich{ProjectFolder=origin,BridgeBin="adc-smoke-capsule123",BridgeToken="smoke-fake-key-only",BridgeEncrypted=true}).Prompt;
            var fullDraft=await DraftWriter.InsertAsync(ownerCore,actualPrompt,localUri);Check(fullDraft.Ok,"Vollständige komprimierte Connect-Capsule bleibt im Rich-Text-Editor unverändert: "+fullDraft.Code);
            Check((await ownerCore.ExecuteScriptAsync("window.sent||0"))=="0","Auch Rich-Text-Draft löst keinen Versand aus");
            // Only dedicated smoke-profile fake cookies; never visit Arena or copy a real login.
            var seed=ownerCore.CookieManager.CreateCookie("axon-smoke-auth",Guid.NewGuid().ToString("N"),".arena.ai","/");seed.IsHttpOnly=true;seed.IsSecure=true;seed.SameSite=CoreWebView2CookieSameSiteKind.Strict;seed.Expires=DateTime.UtcNow.AddHours(1);ownerCore.CookieManager.AddOrUpdateCookie(seed);
            ownerCore.CookieManager.AddOrUpdateCookie(ownerCore.CookieManager.CreateCookie("must-not-copy","no","outside.test","/"));
            Activate(selected[0]);var known=_sessions.Keys.ToHashSet();NewTab_Click(this,new RoutedEventArgs());var inherited=_sessions.Values.Single(t=>!known.Contains(t.Config.Id));await inherited.EnsureBrowserAsync();
            var inheritedCookies=await inherited.Browser!.CoreWebView2.CookieManager.GetCookiesAsync("https://arena.ai");var copied=inheritedCookies.Single(c=>c.Name=="axon-smoke-auth");
            Check(copied.Value==seed.Value,"Plus-Button übernimmt Arena-Cookies einmalig vor erster Navigation");
            Check(copied.IsHttpOnly&&copied.IsSecure&&copied.SameSite==CoreWebView2CookieSameSiteKind.Strict,"HttpOnly/Secure/SameSite bleiben bei Anmeldung erhalten");
            Check(!(await inherited.Browser.CoreWebView2.CookieManager.GetCookiesAsync("https://outside.test")).Any(),"Fremde Domains werden nicht als Anmeldung kopiert");
            seed.Value="source-changed";ownerCore.CookieManager.AddOrUpdateCookie(seed);Check((await inherited.Browser.CoreWebView2.CookieManager.GetCookiesAsync("https://arena.ai")).Single(c=>c.Name==seed.Name).Value==copied.Value,"Danach keine Synchronisierung: Profile bleiben unabhängig");
            CloseTab(inherited);
            var before=selected.ToDictionary(s=>s.Config.Id,s=>s.Browser);
            TabOrder.Move(_settings,selected[0].Config.Id,selected[2].Config.Id);selected[1].Config.Group="Testgruppe";DrawTabs();
            foreach(var s in selected)Activate(s);
            Check(selected.All(s=>ReferenceEquals(before[s.Config.Id],s.Browser)),"Verschieben/Gruppieren/Wechseln erzeugt keine neue Browserinstanz");
            var target=selected[0];Activate(target);ShowPanel(target,"settings");ShowPanel(target,"logs");ShowPanel(target,"files");ShowPanel(target,"info");UpdateLayout();
            Check(target.Cards.Count>=4,"Vier Werkzeuge als dockbare Panels geöffnet");
            await RunFix4SmokeAsync(target,selected[1],checks);
            var card=target.Cards.First(c=>c.Kind=="logs");card.ToggleFloat();await Task.Delay(200);Check(card.Layout.Detached,"Protokoll als eigenes Fenster ausgekoppelt");card.ToggleFloat();await Task.Delay(100);Check(!card.Layout.Detached&&card.Child is not null,"Protokoll wieder angedockt");
            Activate(selected[1]);Check(!Panels.Children.OfType<DockCard>().Any(c=>c.SessionId==target.Config.Id),"Werkzeugpanels wechseln nicht in fremde Sitzung");
            Activate(target);Check(Panels.Children.OfType<DockCard>().All(c=>c.SessionId==target.Config.Id),"Eigene Panels nach Rückwechsel wiederhergestellt");
            var beforeRoot=target.Config.ProjectFolder;var beforeHost=target.Host;var inboxRoot=Path.Combine(origin,".arena-inbox");Directory.CreateDirectory(inboxRoot);
            try{Rebind(target,inboxRoot);throw new Exception("Interner Inboxordner als Projekt gebunden");}catch(InvalidOperationException ex){Check(ex.Message.Contains("interner",StringComparison.Ordinal),"Ordnerbindung weist interne Inboxwurzel vor Zustandsänderung zurück");}
            Check(target.Config.ProjectFolder==beforeRoot&&ReferenceEquals(beforeHost,target.Host),"Abgewiesene Ordnerwahl erhält Projekt und laufenden Sitzungsdienst");
            var migration=selected[2];var oldMigrationRoot=migration.Config.ProjectFolder;migration.Config.ProjectFolder=inboxRoot;migration.StartProject();
            Check(migration.Host is null&&migration.Sync is null&&migration.Hint.Contains("interner",StringComparison.Ordinal),"Gespeicherte alte Inboxwurzel startet weder Projektbrücke noch verschachtelten Inboxdienst");migration.Config.ProjectFolder=oldMigrationRoot;migration.StartProject();
            var settingsCard=target.Cards.First(c=>c.Kind=="settings");
            Check(FindVisualChildren<CheckBox>(settingsCard).Single(c=>Equals(c.Content,"Downloads in Projekt-Inbox ablegen")).IsChecked==false,"Inbox-Downloadoption in echter Einstellungsoberfläche standardmäßig aus");
            Check(!FindVisualChildren<TextBox>(settingsCard).Any(),"Manueller Chat-Adressbereich aus Einstellungen entfernt");
            target.Config.ProjectFolder=null;target.StartProject();target.Config.AutoChatName=true;target.Config.ConnectedChatAddress="";
            const string chatA="https://arena.ai/agent/00000000-0000-4000-8000-000000000003";
            const string chatB="https://arena.ai/agent/00000000-0000-4000-8000-000000000002";
            const string html="<!doctype html><html><head><title>Arena | Benchmark &amp; Compare the Best AI Models</title></head><body><a id='current' href='/agent/00000000-0000-4000-8000-000000000003'>Aktuelles Arena-Projekt</a><a href='/agent/00000000-0000-4000-8000-000000000002'>NICHT fremden Namen übernehmen</a><main>Nachrichten werden nicht gelesen</main></body></html>";
            ownerCore.AddWebResourceRequestedFilter("https://arena.ai/*",CoreWebView2WebResourceContext.All);
            ownerCore.WebResourceRequested+=(_,request)=>{request.Response=ownerCore.Environment.CreateWebResourceResponse(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(html)),200,"OK","Content-Type: text/html; charset=utf-8");};
            async Task Navigate(CoreWebView2 c,string address)
            {
                var done=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);ulong? navigation=null;
                void Starting(object? sender,CoreWebView2NavigationStartingEventArgs ev){if(ev.Uri==address)navigation=ev.NavigationId;}
                void Complete(object? sender,CoreWebView2NavigationCompletedEventArgs ev){if(navigation!=ev.NavigationId)return;c.NavigationStarting-=Starting;c.NavigationCompleted-=Complete;if(ev.IsSuccess)done.TrySetResult();else done.TrySetException(new Exception(ev.WebErrorStatus.ToString()));}
                c.NavigationStarting+=Starting;c.NavigationCompleted+=Complete;c.Navigate(address);await done.Task.WaitAsync(TimeSpan.FromSeconds(15));
            }
            await Navigate(ownerCore,chatA);await Task.Delay(800);
            Check(target.Config.ChatAddress==chatA&&target.Config.LetzteAdresse==chatA,"Bestehender Chat wird beim Öffnen automatisch als URL gespeichert (lokal abgefangene Testseite)");
            Check(target.Config.Name=="Aktuelles Arena-Projekt","Arena-Name des exakt aktuellen Chatlinks übernommen, keine fremde Historie");
            await ownerCore.ExecuteScriptAsync("document.querySelector('#current').href='/agent/00000000-0000-4000-8000-000000000001'");target.RefreshChatName();await Task.Delay(600);
            Check(target.Config.Name=="Aktuelles Arena-Projekt","Allgemeiner Arena-Marketingtitel überschreibt bei unsichtbarem Chatlink keinen echten Namen");
            var otherAddress=selected[1].Config.ChatAddress;var otherName=selected[1].Config.Name;
            await ownerCore.ExecuteScriptAsync("history.pushState({},'',"+JsonSerializer.Serialize(new Uri(chatB).AbsolutePath)+");document.querySelector('#current').href="+JsonSerializer.Serialize(new Uri(chatB).AbsolutePath)+";document.querySelector('#current').textContent='Arena Projekt Beta';document.title='Arena Projekt Beta | Arena'");await Task.Delay(800);
            Check(target.Config.ChatAddress==chatB&&target.Config.Name=="Arena Projekt Beta","SPA-pushState und DocumentTitleChanged aktualisieren URL und Namen ohne Seitenneuladen");
            Check(selected[1].Config.ChatAddress==otherAddress&&selected[1].Config.Name==otherName,"Automatische Metadaten bleiben ihrem Hintergrund-/Vordergrundtab zugeordnet");
            await ownerCore.ExecuteScriptAsync("document.querySelector('#current').textContent='In Arena umbenannt'");target.RefreshChatName();await Task.Delay(700);
            Check(target.Config.Name=="In Arena umbenannt","Geänderter Arena-Seitenleistenname wird ohne Navigation nachgeführt");
            target.Config.AutoChatName=false;target.Config.Name="Eigener lokaler Name";target.RefreshChatName();await Task.Delay(450);
            Check(target.Config.Name=="Eigener lokaler Name","Manuell gewählter Name kann automatische Benennung gezielt deaktivieren");target.Config.AutoChatName=true;
            await ownerCore.ExecuteScriptAsync("history.pushState({},'','/agent')");await Task.Delay(150);
            Check(target.Config.ChatAddress=="https://arena.ai/agent","Neuer Chat erfasst sofort die Starter-URL ohne erfundene Chat-ID");
            await ownerCore.ExecuteScriptAsync("history.pushState({},'',"+JsonSerializer.Serialize(new Uri(chatB).AbsolutePath)+")");await Task.Delay(700);
            var closedId=target.Config.Id;var savedAddress=target.Config.ChatAddress;var savedRoot=target.Config.ProjectFolder;var savedKey=target.Config.BridgeToken;target.Config.ConnectionId=Guid.NewGuid().ToString("N");var savedConnect=target.Config.ConnectionId;
            var otherBrowser=selected[1].Browser;var profile=Rc30Paths.ProfileFolder(closedId);CloseTab(target);
            Check(target.Disposed&&_settings.ClosedWorkspaces.Any(c=>c.Tab.Id==closedId),"Tab-Schließen archiviert Identität statt sie zu verwerfen");
            Check(new AppSettingsStore(Rc30Paths.DataRoot).Load().ClosedWorkspaces.Any(c=>c.Tab.Id==closedId),"Geschlossener Tab vor UI-Freigabe dauerhaft gespeichert");
            Check(ReferenceEquals(otherBrowser,selected[1].Browser)&&Directory.Exists(profile),"Schließen erhält andere Browser und das wiederöffnungsfähige eigene Profil");
            History_Click(HistoryButton,new RoutedEventArgs());var historyMenu=HistoryButton.ContextMenu!;
            var historyEntry=historyMenu.Items.OfType<MenuItem>().Single(i=>Equals(i.Tag,closedId));
            Check(historyMenu.IsOpen&&historyEntry.Header is DockPanel,"Verlauf-Dropdown öffnet echten WPF-Menüeintrag mit Wiederöffnen und Entfernen");
            ((DockPanel)historyEntry.Header).Children.OfType<Button>().Single(b=>b.Content is string text&&text.StartsWith("↶",StringComparison.Ordinal)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var reopened=_sessions[closedId];await reopened.EnsureBrowserAsync();
            Check(reopened.Config.ChatAddress==savedAddress&&reopened.Config.ProjectFolder==savedRoot&&reopened.Config.ConnectionId==savedConnect&&reopened.Config.BridgeToken==savedKey,"Wiederöffnen erhält URL, Projekt, Connect-ID und Paarung unverändert");
            var restoredCore=reopened.Browser!.CoreWebView2;restoredCore.SetVirtualHostNameToFolderMapping("sessions.axon.test",origin,CoreWebView2HostResourceAccessKind.DenyCors);await Navigate(restoredCore,"https://sessions.axon.test/index.html");
            Check(JsonSerializer.Deserialize<string>(await restoredCore.ExecuteScriptAsync("localStorage.getItem('owner')"))==closedId,"Wiedergeöffneter Tab nutzt tatsächlich dasselbe persistente Browserprofil");
            Check(!_settings.ClosedWorkspaces.Any(c=>c.Tab.Id==closedId),"Wiederöffnen entfernt nur den passenden Verlaufseintrag");
            CloseTab(reopened);var sentinel=Path.Combine(origin,"user-project-sentinel.txt");File.WriteAllText(sentinel,"bleibt");_settings.ClosedWorkspaces.Single(c=>c.Tab.Id==closedId).Tab.ProjectFolder=origin;ForgetTab(closedId,false);
            Check(!_settings.ClosedWorkspaces.Any(c=>c.Tab.Id==closedId)&&!_sessions.ContainsKey(closedId),"Dauerhaft entfernen löscht nur den gewählten geschlossenen Tab aus dem Verlauf");
            Check(File.ReadAllText(sentinel)=="bleibt"&&Directory.Exists(Rc30Paths.ProfileFolder(selected[1].Config.Id)),"Entfernen lässt Projektdateien und fremde Tabprofile unberührt");
            Check(HistoryButton.IsVisible&&HistoryButton.Content.ToString()!.StartsWith("Verlauf",StringComparison.Ordinal),"Verlauf-Dropdown dauerhaft in der Tableiste sichtbar");
            Activate(target);UpdateLayout();
            var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,System.Windows.Media.PixelFormats.Pbgra32);bitmap.Render(this);
            var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using(var image=new MemoryStream()){encoder.Save(image);var outputBase=Environment.GetEnvironmentVariable("AXON_RC30_SMOKE_OUTPUT");if(outputBase is not null)File.WriteAllText(Path.ChangeExtension(outputBase,"png.b64"),Convert.ToBase64String(image.ToArray()));}
            var toClose=selected[2];var remaining=selected[1].Browser;CloseTab(toClose);Check(toClose.Disposed&&ReferenceEquals(remaining,selected[1].Browser),"Tab schließen lässt andere Browserinstanz unverändert");
            Check(!FindVisualChildren<Button>(this).Any(b=>Equals(b.Content,"Not-Aus")),"Keine Not-Aus-Oberfläche");
        }
        catch(Exception ex){error=ex.ToString();}
        var output=Environment.GetEnvironmentVariable("AXON_RC30_SMOKE_OUTPUT")??Path.Combine(Rc30Paths.DataRoot,"ui-smoke.json");Directory.CreateDirectory(Path.GetDirectoryName(output)!);File.WriteAllText(output,JsonSerializer.Serialize(new{Passed=error is null,Checks=checks,Error=error},new JsonSerializerOptions{WriteIndented=true}));
        if(error is not null)App.Services.Logger.Write("ERROR",error);
        Application.Current.Shutdown(error is null?0:2);
    }
}
