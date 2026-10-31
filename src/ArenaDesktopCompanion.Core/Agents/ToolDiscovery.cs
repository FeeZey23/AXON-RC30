namespace ArenaDesktopCompanion.Core.Agents;

/// <summary>Ein gefundenes Werkzeug auf diesem Rechner.</summary>
/// <param name="Name">Der Name, unter dem es aufgerufen wird (z. B. <c>git</c>).</param>
/// <param name="Path">Vollständiger Pfad, sofern gefunden.</param>
public sealed record ToolInfo(string Name, string Path)
{
    public bool Found => Path.Length > 0;
    public string FileName => Path.Length == 0 ? string.Empty : System.IO.Path.GetFileName(Path);
}

/// <summary>
/// Sucht die Werkzeuge, die der Companion aufrufen kann.
/// </summary>
/// <remarks>
/// Bewusst ohne <c>PATH</c>-Änderung und ohne Prozessstart: gesucht wird in den übergebenen
/// Ordnern (im Betrieb das <c>PATH</c> des Nutzers) plus den üblichen Installationsorten, die
/// Windows-Programme gern wählen, ohne das <c>PATH</c> anzufassen. So weiß das Fenster vorher,
/// was es anbieten darf, statt blind etwas zu starten, das es nicht gibt.
/// </remarks>
public static class ToolDiscovery
{
    /// <summary>Werkzeuge, die der Companion kennt – Reihenfolge = Anzeige.</summary>
    public static readonly IReadOnlyList<string> Bekannt =
    [
        "dotnet", "git", "node", "npm", "opencode", "claude", "codex", "powershell", "pwsh", "wt",
    ];

    /// <summary>Endungen, unter denen Windows Programme ablegt; Linux/macOS brauchen keine.</summary>
    public static IReadOnlyList<string> Endungen(bool windows)
        => windows ? [".exe", ".cmd", ".bat", ".ps1"] : [];

    /// <summary>Übliche Orte, die nicht im <c>PATH</c> stehen müssen.</summary>
    public static IEnumerable<string> StandardOrdner(bool windows, string? userProfile = null, string? programFiles = null)
    {
        if (!windows)
        {
            yield return "/usr/local/bin";
            yield return "/usr/bin";
            yield break;
        }

        if (userProfile?.Length > 0)
        {
            yield return Path.Combine(userProfile, ".dotnet", "tools");
            yield return Path.Combine(userProfile, "AppData", "Local", "Programs", "Microsoft VS Code", "bin");
            yield return Path.Combine(userProfile, "AppData", "Roaming", "npm");
            yield return Path.Combine(userProfile, ".local", "bin");
        }

        if (programFiles?.Length > 0)
        {
            yield return Path.Combine(programFiles, "nodejs");
            yield return Path.Combine(programFiles, "Git", "cmd");
            yield return Path.Combine(programFiles, "dotnet");
        }
    }

    /// <summary>
    /// Findet ein einzelnes Werkzeug. <paramref name="ordner"/> ist die Suchliste (in der Regel die
    /// Ordner aus <c>PATH</c>), <paramref name="endungen"/> die Dateiendungen.
    /// Gibt einen Eintrag mit leerem Pfad zurück, wenn nichts gefunden wurde – so muss der Aufrufer
    /// nicht mit <c>null</c> hantieren.
    /// </summary>
    public static ToolInfo Find(string name, IEnumerable<string> ordner, IEnumerable<string> endungen)
    {
        var namen = new List<string> { name };
        foreach (var endung in endungen)
        {
            namen.Add(name + endung);
        }

        foreach (var ordnerName in ordner)
        {
            if (string.IsNullOrWhiteSpace(ordnerName))
            {
                continue;
            }

            foreach (var kandidat in namen)
            {
                try
                {
                    var pfad = Path.Combine(ordnerName.Trim().Trim('"'), kandidat);
                    if (File.Exists(pfad))
                    {
                        return new ToolInfo(name, pfad);
                    }
                }
                catch (ArgumentException)
                {
                    // Ein PATH-Eintrag kann Zeichen enthalten, die kein Pfad sein dürfen – überspringen.
                }
            }
        }

        return new ToolInfo(name, string.Empty);
    }

    /// <summary>Sucht alle bekannten Werkzeuge.</summary>
    public static IReadOnlyList<ToolInfo> FindeAlle(
        IEnumerable<string> ordner,
        IEnumerable<string> endungen,
        IEnumerable<string>? weitereNamen = null)
    {
        var suchordner = ordner.Where(o => !string.IsNullOrWhiteSpace(o)).ToList();
        var namen = Bekannt.Concat(weitereNamen ?? []).Distinct(StringComparer.OrdinalIgnoreCase);
        return namen.Select(n => Find(n, suchordner, endungen)).ToList();
    }

    /// <summary>Zerlegt den <c>PATH</c> so, wie es das jeweilige System tut.</summary>
    public static IReadOnlyList<string> SplitPath(string? path, bool windows)
        => string.IsNullOrWhiteSpace(path)
            ? []
            : path.Split(windows ? ';' : ':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
