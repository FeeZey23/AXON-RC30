namespace ArenaDesktopCompanion.Core.Settings;
public static class ProjectRootPolicy
{
    public static string Validate(string? root)
    {
        if(string.IsNullOrWhiteSpace(root)||!Directory.Exists(root))throw new InvalidOperationException("Zuerst einen vorhandenen Projektordner wählen.");
        var full=Path.GetFullPath(root);
        if(full.Split(['\\','/'],StringSplitOptions.RemoveEmptyEntries).Any(p=>new[]{".arena-inbox",".arena-backup",".arena-conflicts"}.Contains(p,StringComparer.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Dies ist ein interner Inbox-/Sicherungsordner. Bitte den eigentlichen Projektordner darüber wählen.");
        return full;
    }
}
