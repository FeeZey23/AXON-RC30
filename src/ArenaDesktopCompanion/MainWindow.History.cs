using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using ArenaDesktopCompanion.Core.Settings;
namespace ArenaDesktopCompanion;
public partial class MainWindow
{
    private bool ChangeHistory(Func<bool> change)
    {
        var tabs=_settings.Arbeitsbereiche.ToList();var closed=_settings.ClosedWorkspaces.ToList();var pending=_settings.PendingTabCleanup.ToList();var active=_settings.AktiverArbeitsbereich;
        try{if(!change())return false;App.Services.SettingsStore.Save(_settings);return true;}
        catch(Exception ex){_settings.Arbeitsbereiche=tabs;_settings.ClosedWorkspaces=closed;_settings.PendingTabCleanup=pending;_settings.AktiverArbeitsbereich=active;Report(ex);return false;}
    }
    private void RestoreTab(string id)
    {
        Arbeitsbereich? tab=null;if(!ChangeHistory(()=>{tab=TabHistory.Restore(_settings,id);return tab is not null;})||tab is null)return;
        var session=CreateSession(tab);_sessions.Add(tab.Id,session);session.StartProject();Activate(session);session.Hint="Tab samt Projekt, Chat und Browserprofil wieder geöffnet.";
    }
    private void ForgetTab(string id,bool confirm=true)
    {
        var entry=_settings.ClosedWorkspaces.FirstOrDefault(c=>c.Tab.Id==id);if(entry is null)return;
        if(confirm&&MessageBox.Show(this,"AXON-Tab „"+entry.Tab.Beschriftung()+"“ samt lokalem Browserprofil und AXON-Sitzungsdaten endgültig entfernen?\n\nArena-Chat und Projektdateien bleiben unverändert.","Tab dauerhaft entfernen",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
        if(!ChangeHistory(()=>{if(!TabHistory.Forget(_settings,id))return false;_settings.PendingTabCleanup.Add(id);return true;}))return;
        CleanupForgottenTabs();DrawTabs();
    }
    private void CleanupForgottenTabs()
    {
        foreach(var id in _settings.PendingTabCleanup.ToList())
        {
            if(_settings.Arbeitsbereiche.Any(t=>t.Id==id)||_settings.ClosedWorkspaces.Any(c=>c.Tab.Id==id))continue;
            try
            {
                foreach(var dir in new[]{Rc30Paths.ProfileFolder(id),Rc30Paths.SessionFolder(id)})
                {
                    if(!Directory.Exists(dir))continue;
                    if((File.GetAttributes(dir)&FileAttributes.ReparsePoint)!=0)throw new IOException("Profilpfad ist eine Verknüpfung; nicht gelöscht.");
                    Directory.Delete(dir,true);
                }
                _settings.PendingTabCleanup.Remove(id);
            }
            catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){App.Services.Logger.Write("WARN","Geschlossenes AXON-Profil wird beim nächsten Start bereinigt: "+id);}
        }
        Save();
    }
    private void History_Click(object sender,RoutedEventArgs e)
    {
        var menu=new ContextMenu{PlacementTarget=HistoryButton,Placement=PlacementMode.Bottom};
        foreach(var entry in _settings.ClosedWorkspaces.OrderByDescending(c=>c.ClosedAt))
        {
            var id=entry.Tab.Id;var row=new DockPanel();var trash=new Button{Content=new System.Windows.Shapes.Path{Data=System.Windows.Media.Geometry.Parse("M1,3 L13,3 M5,3 L5,1 L9,1 L9,3 M3,3 L4,15 L10,15 L11,3 M6,6 L6,12 M8,6 L8,12"),Stroke=Brush("Brush.TextMuted"),StrokeThickness=1.5,Width=14,Height=16},Padding=new Thickness(8,4,8,4),ToolTip="Dauerhaft entfernen – Projekt und Arena-Chat bleiben erhalten"};DockPanel.SetDock(trash,Dock.Right);row.Children.Add(trash);
            var reopen=new Button{Content="↶ "+entry.Tab.Beschriftung(),MinWidth=160,MaxWidth=360,HorizontalContentAlignment=HorizontalAlignment.Left,Padding=new Thickness(8,4,8,4),ToolTip=entry.Tab.ChatAddress+"\n"+entry.Tab.ProjectFolder+"\nGeschlossen: "+entry.ClosedAt.ToLocalTime().ToString("g")};row.Children.Add(reopen);
            var item=new MenuItem{Header=row,Tag=id,StaysOpenOnClick=true};reopen.Click+=(_,args)=>{args.Handled=true;menu.IsOpen=false;RestoreTab(id);};trash.Click+=(_,args)=>{args.Handled=true;menu.IsOpen=false;ForgetTab(id);};
            var context=new ContextMenu();var restore=new MenuItem{Header="Wieder öffnen"};restore.Click+=(_,_)=>{menu.IsOpen=false;RestoreTab(id);};var remove=new MenuItem{Header="Dauerhaft entfernen …"};remove.Click+=(_,_)=>{menu.IsOpen=false;ForgetTab(id);};context.Items.Add(restore);context.Items.Add(remove);item.ContextMenu=context;menu.Items.Add(item);
        }
        if(menu.Items.Count==0)menu.Items.Add(new MenuItem{Header="Keine geschlossenen Tabs",IsEnabled=false});
        HistoryButton.ContextMenu=menu;menu.IsOpen=true;
    }
}
