namespace ArenaDesktopCompanion.Core.Agents;

/// <summary>Wie viel ein Befehl anrichten kann – entscheidet, ob er angeboten wird.</summary>
public enum CommandKind
{
    /// <summary>Nur ansehen (Status, Unterschiede, Version).</summary>
    Read,

    /// <summary>Bauen und prüfen; schreibt nur in <c>bin</c>/<c>obj</c>.</summary>
    Build,

    /// <summary>Schreibt Artefakte oder Papiere (Publish, Bericht erzeugen).</summary>
    Write,
}

/// <summary>Ein Befehl, den der Companion ausführen darf.</summary>
/// <param name="Id">Kurzname, wird in der Oberfläche und in der Ablage benutzt.</param>
/// <param name="Title">Was der Nutzer liest.</param>
/// <param name="Description">Was passiert – in einem Satz.</param>
/// <param name="FileName">Programm (ohne Pfad, wird über das <c>PATH</c> gesucht).</param>
/// <param name="Arguments">Argumente einzeln; es wird keine Zeichenkette an eine Shell übergeben.</param>
/// <param name="Timeout">Nach dieser Zeit wird abgebrochen.</param>
public sealed record CommandSpec(
    string Id,
    string Title,
    string Description,
    string FileName,
    IReadOnlyList<string> Arguments,
    CommandKind Kind,
    TimeSpan Timeout)
{
    public bool NurWindows { get; init; }

    /// <summary>
    /// Programme, die dasselbe können und genommen werden, wenn <see cref="FileName"/> auf diesem
    /// Rechner fehlt. Anlass (Befund vom 21.09.2026): Auf dem Zielrechner ist nur <b>Windows
    /// PowerShell</b> installiert, kein <c>pwsh</c> – die drei Skriptbefehle endeten deshalb sofort mit
    /// „Das System kann die angegebene Datei nicht finden", obwohl sie in der Liste standen.
    /// </summary>
    public IReadOnlyList<string> Ersatz { get; init; } = [];

    /// <summary>Anzeigetext der Zeile, die im Protokoll landet.</summary>
    public string ToCommandLine() => FileName + " " + string.Join(' ', Arguments);
}

/// <summary>
/// Die Liste der Befehle, die der Companion ausführen darf – mehr nicht.
/// </summary>
/// <remarks>
/// <para>
/// Das ist die wichtigste Entscheidung dieser Stufe: Es gibt <b>keine</b> freie Kommandozeile im
/// Fenster. Ein Feld, in das man beliebige Befehle tippen kann, wäre ein Fernsteuerungszugang zum
/// Rechner – und der wäre genau das, was dieses Programm bewusst nicht ist (ADR 001). Der Nutzer
/// wählt aus dieser Liste, jeder Eintrag ist im Quelltext nachlesbar, und was hier nicht steht,
/// kann die App nicht.
/// </para>
/// <para>
/// Zweite Entscheidung: Aufruf ohne Shell. Programm und Argumente werden getrennt übergeben
/// (<c>ProcessStartInfo.ArgumentList</c>), damit Zeichen wie <c>&amp;</c>, <c>|</c> oder <c>%</c>
/// nie als Befehl wirken können – auch nicht, wenn ein Pfad sie enthält.
/// </para>
/// </remarks>
public static class CommandCatalog
{
    public const string Solution = "ArenaDesktopCompanion.sln";

    public static IReadOnlyList<CommandSpec> Alle { get; } =
    [
        new("git-status", "git status", "Was ist im Projektordner geändert? (kurze Liste)",
            "git", ["status", "--short", "--branch"], CommandKind.Read, TimeSpan.FromSeconds(30)),
        new("git-diff-stat", "git diff --stat", "Welche Dateien unterscheiden sich, in Zahlen",
            "git", ["diff", "--stat"], CommandKind.Read, TimeSpan.FromSeconds(30)),
        new("git-log", "git log", "Die letzten fünf Einträge der Versionsgeschichte",
            "git", ["log", "--oneline", "-5"], CommandKind.Read, TimeSpan.FromSeconds(30)),
        new("git-stash-list", "git stash list", "Zwischengelagerte Änderungen anjagen",
            "git", ["stash", "list"], CommandKind.Read, TimeSpan.FromSeconds(30)),
        new("dotnet-version", "dotnet --version", "Welche .NET-Version auf diesem Rechner gilt",
            "dotnet", ["--version"], CommandKind.Read, TimeSpan.FromSeconds(30)),
        // Zwölf statt fünf Minuten: Ein kalter Lauf (erster Restore, Pakete aus dem Netz, WPF)
        // kann auf einem fremden Rechner deutlich länger dauern als in meiner Werkstatt.
        new("build", "Bauen (Debug)", "Baue die Lösung mit Warnungen als Fehler",
            "dotnet", ["build", Solution, "-c", "Debug", "-p:TreatWarningsAsErrors=true"], CommandKind.Build, TimeSpan.FromMinutes(12)),
        new("test", "Tests laufen lassen", "Alle Core-Tests (brauchen kein Windows)",
            "dotnet", ["test", "tests/ArenaDesktopCompanion.Core.Tests/ArenaDesktopCompanion.Core.Tests.csproj", "--nologo"],
            CommandKind.Build, TimeSpan.FromMinutes(12)),
        new("restore", "Pakete wiederherstellen", "dotnet restore – wenn ein Bauen an fehlenden Paketen scheitert",
            "dotnet", ["restore", Solution], CommandKind.Build, TimeSpan.FromMinutes(12)),
        new("selftest", "Selbsttest", "Umgebung, Struktur, Bauen, Tests, Artefakte – das große Programm",
            "pwsh", ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "scripts/selftest.ps1"], CommandKind.Build, TimeSpan.FromMinutes(15))
        {
            NurWindows = true,
            Ersatz = ["powershell"],
        },
        new("publish", "Artefakte bauen (publish)", "Erzeugt die zwei Zip-Pakete in publish/<version>/",
            "pwsh", ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "scripts/publish.ps1"], CommandKind.Write, TimeSpan.FromMinutes(20))
        {
            NurWindows = true,
            Ersatz = ["powershell"],
        },
        new("crash-report", "Letzter Fehlerblock", "Zeigt den letzten Absturz samt Fundstelle",
            "pwsh", ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "scripts/crash-report.ps1"], CommandKind.Read, TimeSpan.FromMinutes(2))
        {
            NurWindows = true,
            Ersatz = ["powershell"],
        },
    ];

    /// <summary>
    /// Programme, die in einem Vorschlag stehen dürfen. Mehr nicht – ein Vorschlag ist kein Freibrief,
    /// sondern genau ein Aufruf, den der Nutzer vorher auf dem Bildschirm sieht und bestätigt.
    /// </summary>
    public static readonly IReadOnlyList<string> VorschlagErlaubt =
    [
        "pwsh", "powershell", "cmd", "git", "dotnet", "node", "npm", "python", "py",
    ];

    /// <summary>So viele Argumente darf ein Vorschlag haben – mehr ist kein Befehl mehr, sondern ein Skript.</summary>
    public const int VorschlagMaxArgumente = 24;

    /// <summary>Längstes Einzelargument; alles darüber wird abgelehnt statt still gekürzt.</summary>
    public const int VorschlagMaxArgumentLaenge = 600;

    /// <summary>Höchstdauer eines freigegebenen Aufrufs.</summary>
    public static readonly TimeSpan VorschlagTimeout = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Baut aus einem Vorschlag einen ausführbaren Befehl – ohne Shell, mit fester Höchstdauer.
    /// Das Programm bleibt das Programm, jedes Argument bleibt ein Argument; Zeichen wie
    /// <c>&amp;</c>, <c>|</c> oder <c>%</c> können nichts anrichten, weil keine Shell dazwischen ist.
    /// </summary>
    public static CommandSpec Vorschlag(string program, IReadOnlyList<string> argumente, string zweck)
        => new("vorschlag", "Vorschlag aus dem Chat", zweck, program, argumente, CommandKind.Write, VorschlagTimeout);

    /// <summary>
    /// Programme, die <b>selbst</b> Befehle ausführen: eine Shell (<c>cmd</c>, <c>pwsh</c>,
    /// <c>powershell</c>) oder eine Laufzeitumgebung, die Code annimmt (<c>python</c>, <c>py</c>,
    /// <c>node</c>, <c>npm</c>).
    /// </summary>
    /// <remarks>
    /// Fund aus dem Master-Check (24.09.2026): Im Freigabefenster stand für <i>jeden</i> Vorschlag
    /// „Der Aufruf läuft ohne Shell“ – bei diesen Programmen ist das falsch, denn die Argumente
    /// <i>sind</i> dort ein Programm. Zwei Vorkehrungen hängen seitdem daran: ein Hinweis über dem
    /// Aufruf (<see cref="FreigabeHinweis"/>) und die Zeichenprüfung
    /// <see cref="ArgumentIstUnbedenklich"/> in der Auftragsprüfung.
    /// </remarks>
    public static readonly IReadOnlyList<string> ProgrammKannBefehleAusfuehren =
        ["cmd", "pwsh", "powershell", "python", "py", "node", "npm"];

    /// <summary>Führt dieses Programm selbst Befehle aus (Shell oder Laufzeitumgebung)?</summary>
    public static bool KannSelbstBefehleAusfuehren(string? program)
        => ProgrammSchluessel(program) is { } schluessel
           && ProgrammKannBefehleAusfuehren.Contains(schluessel, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Der Name eines zugelassenen Programms. Die Windows-Schreibweise <c>powershell.exe</c> ist
    /// derselbe Werkzeugname wie <c>powershell</c>; vollständige oder relative Pfade bleiben aber
    /// absichtlich ausgeschlossen.
    /// </summary>
    private static string? ProgrammSchluessel(string? program)
    {
        if (string.IsNullOrWhiteSpace(program))
        {
            return null;
        }

        var name = program.Trim();
        if (name.Contains('/') || name.Contains('\\'))
        {
            return null;
        }

        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        return name.Length == 0 ? null : name;
    }

    /// <summary>
    /// Die Warnzeile über einem solchen Aufruf – oder <see langword="null"/> bei einem gewöhnlichen
    /// Werkzeug. Sie steht im Freigabefenster <i>und</i> im Protokoll, damit ein Ja eine bewusste
    /// Entscheidung bleibt.
    /// </summary>
    public static string? FreigabeHinweis(string? program)
        => KannSelbstBefehleAusfuehren(program)
            ? $"ACHTUNG: »{program}« ist kein einzelnes Werkzeug, sondern führt selbst Befehle aus. " +
              "Die Argumente darunter sind sein Programm — lies sie Zeichen für Zeichen, bevor du Ja klickst."
            : null;

    /// <summary>
    /// Der Satz, der im Freigabefenster erklärt, <i>wie</i> der Aufruf läuft. Für gewöhnliche
    /// Werkzeuge stimmt „ohne Shell“; bei einer Shell oder Laufzeitumgebung wäre genau dieser Satz
    /// eine Beruhigung an der falschen Stelle.
    /// </summary>
    public static string Laufweise(string? program)
        => KannSelbstBefehleAusfuehren(program)
            ? "Der Aufruf geht ohne Umweg über eine Shell direkt an dieses Programm — aber das Programm selbst kann Befehle ausführen."
            : "Der Aufruf läuft ohne Shell";

    public static bool ProgrammErlaubt(string? program)
        => ProgrammSchluessel(program) is { } schluessel
           && VorschlagErlaubt.Contains(schluessel, StringComparer.OrdinalIgnoreCase);

    public static CommandSpec? Find(string id)
        => Alle.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Die Befehle, die auf diesem System angeboten werden dürfen.</summary>
    public static IReadOnlyList<CommandSpec> Verfuegbar(bool windows, bool befehleErlaubt, bool schreibendErlaubt)
    {
        if (!befehleErlaubt)
        {
            return [];
        }

        return Alle
            .Where(c => windows || !c.NurWindows)
            .Where(c => c.Kind != CommandKind.Write || schreibendErlaubt)
            .ToList();
    }

    /// <summary>
    /// Zeichen, die in einem Argument nichts zu suchen haben. Sie sind der Grund, warum der Aufruf
    /// ohne Shell erfolgt; kommt eines vor, ist der Befehl falsch gebaut – nicht der Nutzer.
    /// </summary>
    public static bool ArgumentIstUnbedenklich(string argument)
        => !argument.Any(c => c is '&' or '|' or '>' or '<' or '^' or '\n' or '\r' or '"' or '`' or '$' or ';');
}
