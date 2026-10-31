using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls.Primitives;
using ArenaDesktopCompanion.Core.Settings;
using ArenaDesktopCompanion.Workspace;
namespace ArenaDesktopCompanion;
public partial class MainWindow
{
    private async Task RunFix4SmokeAsync(TabSession owner,TabSession other,List<string> checks)
    {
        void Check(bool ok,string label){if(!ok)throw new InvalidOperationException(label);checks.Add(label);}
        Window Float(DockCard c)=>(Window)typeof(DockCard).GetField("_floating",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(c)!;
        var card=owner.Cards.First(c=>c.Kind=="logs");card.ToggleFloat();await Task.Delay(100);var f=Float(card);
        f.Width=680;f.Height=420;f.Left=140;f.Top=150;await Task.Delay(100);Save();
        Check(Math.Abs(card.Layout.Width-f.Width)<1&&Math.Abs(card.Layout.Left-f.Left)<1,"Fix4: Floating-Geometrie vor Schließen im gespeicherten Layout aktuell");
        var persisted=App.Services.SettingsStore.Load().Arbeitsbereiche.Single(t=>t.Id==owner.Config.Id).Panels.Single(p=>p.Kind=="logs");
        Check(Math.Abs(persisted.Width-f.Width)<1&&Math.Abs(persisted.Top-f.Top)<1,"Fix4: Floating-Geometrie tatsächlich in settings.json gespeichert");
        Activate(other);Check(!f.IsVisible,"Fix4: Schwebendes Fenster beim Besitzer-Tabwechsel ausgeblendet");Activate(owner);Check(f.IsVisible,"Fix4: Dasselbe schwebende Fenster bei Rückkehr sichtbar");
        f.Left=30000;f.Top=30000;card.SetOwnerActive(false);card.SetOwnerActive(true);await Task.Delay(100);
        var area=MonitorPlacement.Area(f);Check(f.Left>=area.Left&&f.Top>=area.Top&&f.Left+f.Width<=area.Left+area.Width+1&&f.Top+f.Height<=area.Top+area.Height+1,"Fix4: Offscreen-Fenster innerhalb wirklicher Monitor-Arbeitsfläche wiederhergestellt");
        var dpi=System.Windows.Media.VisualTreeHelper.GetDpi(f).DpiScaleX;Check(double.IsFinite(dpi)&&dpi>0,"Fix4: Tatsächliche WPF-DPI-Skalierung "+dpi.ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach(var monitor in MonitorPlacement.WorkAreas())
        {
            MonitorPlacement.MoveForSmoke(f,monitor.Physical);await Task.Delay(250);card.SetOwnerActive(false);card.SetOwnerActive(true);await Task.Delay(150);
            Check(MonitorPlacement.PhysicallyInside(f,monitor.Handle,monitor.Physical),"Fix4: Floating-Fenster auf echtem Monitor vollständig innerhalb Arbeitsfläche: "+monitor.Physical+"; DPI="+System.Windows.Media.VisualTreeHelper.GetDpi(f).DpiScaleX.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        card.ToggleFloat();await Task.Delay(100);
        var thumb=FindVisualChildren<Thumb>(card).Single();var prior=card.Layout.Height;thumb.RaiseEvent(new DragDeltaEventArgs(0,35){RoutedEvent=Thumb.DragDeltaEvent});Check(card.Layout.Height>prior,"Fix4: Echter WPF-Thumb-DragDelta ändert und speichert Panelhöhe");
        var badRoot=Path.Combine(Rc30Paths.DataRoot,"fix4-bad-project");var goodRoot=Path.Combine(Rc30Paths.DataRoot,"fix4-good-project");Directory.CreateDirectory(badRoot);Directory.CreateDirectory(goodRoot);
        var badConfig=new Arbeitsbereich{ProjectFolder=badRoot,BridgeBin="adc-fix4-isolated-smoke",BridgeToken="synthetic-only"};
        using var bad=new TabSession(badConfig,App.Services);using var good=new TabSession(new Arbeitsbereich{ProjectFolder=goodRoot,BridgeBin="",BridgeToken=""},App.Services);
        var journal=Path.Combine(Rc30Paths.SessionFolder(badConfig.Id),"bridge-erledigt-"+badConfig.BridgeBin+".txt.journal.json");Directory.CreateDirectory(Path.GetDirectoryName(journal)!);File.WriteAllText(journal,"{broken");
        bad.StartProject();good.StartProject();
        Check(bad.ProjectError is not null&&bad.Host is null&&bad.Sync is null,"Fix4: Defektes Tabjournal isoliert; Ressourcen freigegeben, keine Aktionen gestartet");
        Check(File.ReadAllText(journal)=="{broken","Fix4: Defektes Journal unverändert erhalten, kein blindes Replay");
        Check(good.ProjectError is null&&good.Host is not null&&good.Sync is not null,"Fix4: Zweiter Tab startet trotz defektem ersten Tab unabhängig");
        var isolated=new DockCard(owner.Config.Id,"Fix4 Archiv-/Exitprobe",new PanelLayout{Kind="isolated",Detached=false,Width=460,Height=300},new System.Windows.Controls.TextBlock{Text="Nur synthetischer Test"});
        isolated.SetOwnerActive(false);isolated.ToggleFloat();Check(!Float(isolated).IsVisible,"Fix4: Inaktiver Besitzer zeigt auch neu wiederhergestelltes Floating-Fenster nicht");isolated.SetOwnerActive(true);Check(Float(isolated).IsVisible,"Fix4: Zuvor unsichtbares Floating-Fenster erst bei Besitzeraktivierung sichtbar");var sf=Float(isolated);sf.Width=610;sf.Left=170;isolated.DisposeCard();Check(isolated.Layout.Detached&&Math.Abs(isolated.Layout.Width-610)<1,"Fix4: App-Ende erhält Detached-Zustand und aktuelle Floating-Größe");
        var archivedIndex=_settings.ArbeitsbereichAnlegen("Fix4 Archivprobe");var archived=CreateSession(_settings.Arbeitsbereiche[archivedIndex]);_sessions.Add(archived.Config.Id,archived);Activate(archived);ShowPanel(archived,"logs");var ac=archived.Cards.Single();ac.ToggleFloat();var af=Float(ac);af.Width=620;af.Left=180;CloseTab(archived);
        var history=_settings.ClosedWorkspaces.Single(e=>e.Tab.Id==archived.Config.Id).Tab.Panels.Single();Check(history.Detached&&Math.Abs(history.Width-620)<1,"Fix4: Verlauf archiviert aktuelle Floating-Geometrie vor Dispose");
        Activate(owner);await RunNativePanelDragAsync(owner,other,checks);
    }
}
