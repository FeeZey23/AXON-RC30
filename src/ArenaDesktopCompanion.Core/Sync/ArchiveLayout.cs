namespace ArenaDesktopCompanion.Core.Sync;

/// <summary>
/// Workspace-zips aus arena.ai enthalten fast immer ein Top-Level-Verzeichnis
/// (Sitzungsname o. ä.). Ohne Behandlung würde der Import alles unter
/// <c>projekt/arena-session-12/src/main.py</c> statt <c>projekt/src/main.py</c> ablegen.
/// Entfernt wird der gemeinsame Vorspann nur, wenn *jede* Datei darin liegt –
/// sonst würde man versehentlich Projektdateien verschachtelt überschreiben.
/// </summary>
public static class ArchiveLayout
{
    /// <summary>
    /// Liefert den einzigen gemeinsamen Top-Ordner, oder <see langword="null"/> wenn nicht
    /// eindeutig. <paramref name="projectRoot"/> erlaubt die entscheidende Rückfrage:
    /// existiert der Ordner im Projekt schon („docs/“), ist er Teil des Projekts und darf
    /// NICHT weggestrichen werden – sonst landet docs/readme.md plötzlich im Wurzelverzeichnis.
    /// </summary>
    public static string? DetectCommonRoot(IEnumerable<string> entryNames, string? projectRoot = null)
    {
        string? candidate = null;
        var anyAtRoot = false;

        foreach (var name in entryNames)
        {
            // "C:/windows/x.dll" ist kein Vorspann, sondern ein Ausbruchsversuch:
            // der Name muss unangetastet bleiben, damit PathSafety ihn blockiert.
            if (LooksAbsolute(name))
            {
                return null;
            }

            var normalized = name.Replace('\\', '/').TrimStart('/');

            var slash = normalized.IndexOf('/');
            if (slash <= 0)
            {
                anyAtRoot = true;
                continue;
            }

            var first = normalized[..slash];
            if (candidate is null)
            {
                candidate = first;
            }
            else if (!string.Equals(candidate, first, StringComparison.Ordinal))
            {
                return null; // nicht einheitlich -> nichts entfernen
            }
        }

        if (anyAtRoot || candidate is null)
        {
            return null;
        }

        if (projectRoot is not null &&
            Directory.Exists(Path.Combine(projectRoot, candidate.Replace('/', Path.DirectorySeparatorChar))))
        {
            return null; // gehört zum Projekt, ist kein Export-Vorspann
        }

        return candidate;
    }

    /// <summary>Wahr für absolute und Laufwerks-/UNC-Namen in Archiven.</summary>
    public static bool LooksAbsolute(string entryName)
        => entryName.Length > 0
           && (entryName[0] == '/' || entryName[0] == '\\' || entryName.Contains(':'));

    public static string RemoveRoot(string entryName, string? commonRoot)
    {
        if (commonRoot is null)
        {
            return entryName;
        }

        var normalized = entryName.Replace('\\', '/').TrimStart('/');
        var prefix = commonRoot + '/';
        return normalized.StartsWith(prefix, StringComparison.Ordinal)
            ? normalized[prefix.Length..]
            : normalized;
    }
}
