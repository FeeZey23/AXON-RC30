namespace ArenaDesktopCompanion.Core.Sync;

/// <summary>Muster, die beim Import immer übersprungen werden (Build-Ordner, Systemmüll, eigener Sync-Kram).</summary>
public static class WorkspaceFileFilter
{
    private static readonly string[] SkippedDirectoryParts =
    {
        ".git", ".svn", ".hg", "node_modules", "bin", "obj", ".vs", ".idea",
        ".next", ".nuxt", "dist", "target", "__pycache__", ".venv", "venv",
        ".arena-inbox", "Backup", ".arena-backup", ".arena-conflicts", ".arena-staging"
    };

    private static readonly string[] SkippedFileNames =
    {
        ".DS_Store", "Thumbs.db", "desktop.ini", ".gitignore.tmp"
    };

    public static bool ShouldSkip(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return true;
        }

        var normalized = relativePath.Replace('\\', '/');
        foreach (var part in normalized.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var skip in SkippedDirectoryParts)
            {
                if (string.Equals(part, skip, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        var name = Path.GetFileName(normalized);
        if (name.Length == 0)
        {
            return true;
        }

        foreach (var skip in SkippedFileNames)
        {
            if (string.Equals(name, skip, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        // macOS-Ressourcengabeln in Zip-Archiven
        if (normalized.StartsWith("__MACOSX/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return name.StartsWith(".arena-", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Sinnvolle Reihenfolge: Verzeichnisse zuerst anlegen, Dateien danach.</summary>
    public static IEnumerable<string> InImportOrder(IEnumerable<string> relativePaths)
        => relativePaths
            .Select(p => (Path: p, Depth: p.Count(c => c == '/')))
            .OrderBy(x => x.Depth)
            .ThenBy(x => x.Path, StringComparer.Ordinal)
            .Select(x => x.Path);
}
