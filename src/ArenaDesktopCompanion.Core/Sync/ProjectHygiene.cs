namespace ArenaDesktopCompanion.Core.Sync;

public sealed record GitReadiness(bool HasGitRepository, bool HasGitignore, string Root, string? Error = null)
{
    public bool NeedsGitInit => !HasGitRepository;
    public bool NeedsGitignore => !HasGitignore;
    public bool IsClean => HasGitRepository && HasGitignore;
}

/// <summary>
/// Punkt 7.4 / ADR 006 g: Der Companion überschreibt nichts still – aber die einzige
/// wirklich rückholbare Sicherheit für Code, an dem du selbst arbeitest, ist eine
/// Versionsgeschichte. Dieser Helfer erkennt den Stand und legt die .gitignore-Vorlage an
/// (die der App-eigene Ordnermüll sonst unnötig mitversionieren würde).
/// </summary>
public static class ProjectHygiene
{
    /// <summary>.gitignore für Projektordner: Sync-Ordner raus, Build-Ordner raus.</summary>
    public const string RecommendedGitignoreContent = """
        # Vom Arena.ai Desktop Companion angelegt – nicht versionieren (Backup = Sicherungen).
        .arena-inbox/
        Backup/
        .arena-conflicts/
        .arena-sync-index.json

        # Übliche Bau- und Umgebungsordner.
        bin/
        obj/
        node_modules/
        __pycache__/
        .venv/
        venv/
        dist/
        .DS_Store
        Thumbs.db
        """;

    public static GitReadiness Inspect(string? projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            return new GitReadiness(false, false, string.Empty, "kein Projektordner gewählt");
        }

        var root = Path.GetFullPath(projectRoot);
        if (!Directory.Exists(root))
        {
            return new GitReadiness(false, false, root, "Ordner existiert nicht");
        }

        try
        {
            // .git kann eine Datei sein (worktrees, Submodule) – beides zählt.
            var hasGit = Directory.Exists(Path.Combine(root, ".git")) || File.Exists(Path.Combine(root, ".git"));
            var hasIgnore = File.Exists(Path.Combine(root, ".gitignore"));
            return new GitReadiness(hasGit, hasIgnore, root);
        }
        catch (Exception ex)
        {
            return new GitReadiness(false, false, root, ex.Message);
        }
    }

    /// <summary>
    /// Reihenfolge der Kandidaten für <c>git init</c>: erst mit <c>-b main</c> (ab Git 2.28),
    /// sonst ohne – damit die App auf alten Git-Versionen nicht mit einem Fehler dasteht.
    /// </summary>
    public static IReadOnlyList<string[]> GitInitCommandCandidates() => new[]
    {
        new[] { "init", "-b", "main" },
        new[] { "init" }
    };

    /// <summary>Schreibt die .gitignore-Vorlage nur, wenn noch keine existiert. Gibt den Pfad oder null zurück.</summary>
    public static string? EnsureRecommendedGitignore(string projectRoot, bool overwriteExisting = false)
    {
        if (string.IsNullOrWhiteSpace(projectRoot) || !Directory.Exists(projectRoot))
        {
            return null;
        }

        var path = Path.Combine(projectRoot, ".gitignore");
        if (File.Exists(path) && !overwriteExisting)
        {
            return null;   // vorhandene Datei anfassen wäre ein Übergriff
        }

        try
        {
            File.WriteAllText(path, RecommendedGitignoreContent + Environment.NewLine);
            return path;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Ergänzt fehlende Zeilen in einer vorhandenen .gitignore, ohne Bestehendes zu löschen.</summary>
    public static bool AppendMissingIgnoreEntries(string projectRoot)
    {
        var path = Path.Combine(projectRoot, ".gitignore");
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            var existing = File.ReadAllText(path);
            var missing = RecommendedGitignoreContent
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(l => !l.StartsWith('#') && !existing.Contains(l, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (missing.Count == 0)
            {
                return false;
            }

            var append = string.Concat(missing.Select(m => "\n" + m));
            File.AppendAllText(path, Environment.NewLine + append.TrimStart() + Environment.NewLine);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Ob der Seitenleisten-Hinweis gezeigt wird: nur bei echtem Bedarf, nie nach Ausblenden,
    /// nie bei einem Ordner, den wir gerade nicht lesen können.
    /// </summary>
    public static bool ShouldShowHint(GitReadiness readiness, bool dismissedByUser)
        => !dismissedByUser
           && readiness.Error is null
           && (readiness.NeedsGitInit || readiness.NeedsGitignore);
}
