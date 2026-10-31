using System.Text.Json;
namespace ArenaDesktopCompanion.Core.Settings;
public sealed class ClosedWorkspace
{
    public Arbeitsbereich Tab { get; set; }=new();
    public DateTimeOffset ClosedAt { get; set; }
}
public static class TabHistory
{
    public static bool Close(AppSettings s,string id,DateTimeOffset now)
    {
        var index=s.Arbeitsbereiche.FindIndex(t=>t.Id==id);if(index<0)return false;
        var active=s.Arbeitsbereiche.ElementAtOrDefault(s.AktiverArbeitsbereich)?.Id;
        var snapshot=JsonSerializer.Deserialize<Arbeitsbereich>(JsonSerializer.Serialize(s.Arbeitsbereiche[index]))!;
        s.ClosedWorkspaces.RemoveAll(c=>c.Tab.Id==id);s.ClosedWorkspaces.Insert(0,new(){Tab=snapshot,ClosedAt=now});s.Arbeitsbereiche.RemoveAt(index);
        if(s.Arbeitsbereiche.Count==0)s.ArbeitsbereichAnlegen();
        var keep=s.Arbeitsbereiche.FindIndex(t=>t.Id==active);s.AktiverArbeitsbereich=keep>=0?keep:Math.Clamp(index-1,0,s.Arbeitsbereiche.Count-1);return true;
    }
    public static Arbeitsbereich? Restore(AppSettings s,string id)
    {
        var entry=s.ClosedWorkspaces.FirstOrDefault(c=>c.Tab.Id==id);if(entry is null||s.Arbeitsbereiche.Any(t=>t.Id==id)||!Guid.TryParseExact(id,"N",out _))return null;
        s.ClosedWorkspaces.Remove(entry);s.Arbeitsbereiche.Add(entry.Tab);s.AktiverArbeitsbereich=s.Arbeitsbereiche.Count-1;return entry.Tab;
    }
    public static bool Forget(AppSettings s,string id)=>!s.Arbeitsbereiche.Any(t=>t.Id==id)&&s.ClosedWorkspaces.RemoveAll(c=>c.Tab.Id==id)>0;
    public static void Normalize(AppSettings s)
    {
        s.ClosedWorkspaces??=[];s.PendingTabCleanup??=[];var used=s.Arbeitsbereiche.Select(t=>t.Id).ToHashSet(StringComparer.Ordinal);
        s.ClosedWorkspaces=s.ClosedWorkspaces.Where(c=>c?.Tab is not null&&Guid.TryParseExact(c.Tab.Id,"N",out _)&&used.Add(c.Tab.Id)).ToList();
        var retained=used;s.PendingTabCleanup=s.PendingTabCleanup.Where(id=>Guid.TryParseExact(id,"N",out _)&&!retained.Contains(id)).Distinct().ToList();
    }
}
