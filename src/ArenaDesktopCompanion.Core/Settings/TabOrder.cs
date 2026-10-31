namespace ArenaDesktopCompanion.Core.Settings;
public static class TabOrder
{
    public static bool Move(AppSettings settings, string sourceId, string targetId)
    {
        var source = settings.Arbeitsbereiche.FindIndex(x => x.Id == sourceId);
        var target = settings.Arbeitsbereiche.FindIndex(x => x.Id == targetId);
        if (source < 0 || target < 0 || source == target) return false;
        var active = settings.Arbeitsbereiche[settings.AktiverArbeitsbereich].Id;
        var item = settings.Arbeitsbereiche[source];
        settings.Arbeitsbereiche.RemoveAt(source);
        settings.Arbeitsbereiche.Insert(target, item);
        settings.AktiverArbeitsbereich = settings.Arbeitsbereiche.FindIndex(x => x.Id == active);
        return true;
    }
    public static string ShortPath(string path)
    {
        var parts = path.Replace('/', '\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length < 2 ? path : "..\\" + string.Join('\\', parts.TakeLast(2));
    }
}
