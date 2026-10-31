using System.Text.Json;
using System.Text.Json.Serialization;
using ArenaDesktopCompanion.Core.Agents;
using ArenaDesktopCompanion.Core.Sync;

namespace ArenaDesktopCompanion.Core.Bridge;

/// <summary>Was der Assistent den Companion tun lassen darf.</summary>
public enum JobKind
{
    /// <summary>Nur nachsehen: ein Verzeichnis- oder Dateiblick.</summary>
    Look,

    /// <summary>Authentifizierter Hin-/Rückweg ohne Dateiänderungen.</summary>
    Ping,

    /// <summary>Ein Befehl aus der Befehlsliste ausführen.</summary>
    Run,

    /// <summary>Eine Datei lesen (nur innerhalb des Projektordners).</summary>
    Read,

    /// <summary>Eine Datei schreiben – über dieselbe Regelmaschine wie der Import.</summary>
    Write,

    /// <summary>Ein Verzeichnis-Abbild schicken, ohne dass etwas ausgeführt wird.</summary>
    Manifest,

    /// <summary>
    /// Ein Vorschlag: ein einzelner Aufruf, den der Nutzer <b>vorher bestätigen muss</b>
    /// (PowerShell, cmd, git …). Der Companion führt ihn erst nach der Freigabe aus.
    /// </summary>
    Vorschlag,
}

/// <summary>Ein Auftrag, wie er im Briefkasten liegt.</summary>
public sealed record BridgeJob
{
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    [JsonPropertyName("kind")] public string Kind { get; init; } = string.Empty;
    [JsonPropertyName("command")] public string? Command { get; init; }
    [JsonPropertyName("path")] public string? Path { get; init; }
    [JsonPropertyName("content")] public string? Content { get; init; }

    /// <summary>
    /// Wie <see cref="Content"/> zu lesen ist: fehlend/<c>text</c> = Zeichen wie sie dastehen,
    /// <c>base64</c> = ein Binärinhalt (z. B. ein <c>.ico</c> oder <c>.png</c>).
    /// </summary>
    /// <remarks>
    /// Anlass (rc13): Ein Anwendungszeichen oder ein Bild lässt sich nicht als Text übertragen –
    /// ohne diesen Weg müsste der Nutzer jede Binärdatei von Hand einspielen, und genau das soll
    /// die Brücke ersparen.
    /// </remarks>
    [JsonPropertyName("kodierung")] public string? Kodierung { get; init; }

    [JsonPropertyName("projekt")] public string? Projekt { get; init; }

    [JsonPropertyName("note")] public string? Note { get; init; }
    [JsonPropertyName("token")] public string Token { get; init; } = string.Empty;

    /// <summary>
    /// „Ja, dieser Platzhalter steht absichtlich drin“ (TODO 8.20).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Normalerweise weist die Brücke jeden Schreibauftrag ab, dessen Inhalt den Redaktions-
    /// platzhalter <see cref="SecretRedaction.Platzhalter"/> enthält: so ein Text stammt meist aus
    /// einem <c>read</c>, und ihn zurückzuschreiben verstümmelt die Datei (Befund vom 21.09.2026).
    /// </para>
    /// <para>
    /// Die Sperre traf aber auch Dateien, die den Platzhalter nur <b>erwähnen</b> – die eigene
    /// Dokumentation, die Tests, <c>SecretRedaction.cs</c> selbst. Gefunden, als die Datei mit der
    /// Regel nicht hochgeladen werden konnte, die sie beschreibt. Deshalb dieses Feld: wer weiß,
    /// was er tut, sagt es ausdrücklich – ohne Feld bleibt die Sperre scharf.
    /// </para>
    /// </remarks>
    [JsonPropertyName("platzhalterErlaubt")] public bool PlatzhalterErlaubt { get; init; }

    /// <summary>Ist der Inhalt als Base64 verpackt?</summary>
    public bool IstBase64 => string.Equals(Kodierung?.Trim(), "base64", StringComparison.OrdinalIgnoreCase);

    /// <summary>Ist die Kodierung eine, die diese Fassung kennt? (Leer = Text.)</summary>
    public bool KodierungBekannt => string.IsNullOrWhiteSpace(Kodierung)
        || IstBase64
        || string.Equals(Kodierung!.Trim(), "text", StringComparison.OrdinalIgnoreCase);

    /// <summary>Programm eines Vorschlags (ohne Pfad) – nur aus <see cref="CommandCatalog.VorschlagErlaubt"/>.</summary>
    [JsonPropertyName("program")] public string? Program { get; init; }

    /// <summary>Die Argumente des Vorschlags, einzeln – nie eine Zeichenkette an eine Shell.</summary>
    [JsonPropertyName("argumente")] public IReadOnlyList<string>? Argumente { get; init; }

    /// <summary>Wozu der Aufruf gut ist: das steht im Freigabefenster auf dem Bildschirm des Nutzers.</summary>
    [JsonPropertyName("zweck")] public string? Zweck { get; init; }

    public JobKind? KindAlsEnum => Enum.TryParse<JobKind>(Kind, ignoreCase: true, out var kind) ? kind : null;

    public static string DateiName(string id) => $"job-{id}.json";
    public static string ErgebnisName(string id) => $"result-{id}.json";
}

/// <summary>Das Ergebnis eines Auftrags.</summary>
public sealed record BridgeResult
{
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    [JsonPropertyName("status")] public string Status { get; init; } = string.Empty;  // ok | fehler | abgelehnt
    [JsonPropertyName("kind")] public string Kind { get; init; } = string.Empty;
    [JsonPropertyName("ausgabe")] public string? Ausgabe { get; init; }
    [JsonPropertyName("exitCode")] public int ExitCode { get; init; }
    [JsonPropertyName("dauerSekunden")] public double DauerSekunden { get; init; }
    [JsonPropertyName("zeit")] public string Zeit { get; init; } = string.Empty;
    [JsonPropertyName("hinweis")] public string? Hinweis { get; init; }

    public bool Ok => string.Equals(Status, "ok", StringComparison.Ordinal);
}

/// <summary>Ein Lebenszeichen des Companions – damit die andere Seite weiß, wer da ist.</summary>
public sealed record BridgeHello
{
    [JsonPropertyName("transportOnly")] public bool TransportOnly { get; init; } = true;
    [JsonPropertyName("pruefung")] public string Pruefung { get; init; } = "Nur Transport-Lebenszeichen. Diese Abfrage bestätigt KEINEN Chat-Rückkanal. Für eine echte Verbindungsprüfung node .axon-rc30/bridge.mjs handshake ausführen; niemals allein hieraus verbunden/grün melden.";
    [JsonPropertyName("connectionId")] public string ConnectionId { get; init; } = "";
    [JsonPropertyName("app")] public string AppVersion { get; init; } = string.Empty;
    [JsonPropertyName("host")] public string Host { get; init; } = string.Empty;
    [JsonPropertyName("projekt")] public string Projekt { get; init; } = string.Empty;
    /// <summary>Namen der gefundenen Werkzeuge – die Pfade stehen im vollen Abbild.</summary>
    [JsonPropertyName("tools")] public IReadOnlyList<string> Tools { get; init; } = [];
    [JsonPropertyName("befehle")] public IReadOnlyList<string> Befehle { get; init; } = [];
    [JsonPropertyName("zeit")] public string Zeit { get; init; } = string.Empty;

    /// <summary>Höchste vergebene Änderungsnummer – daran erkennt die Gegenseite „nichts Neues“.</summary>
    [JsonPropertyName("seq")] public long Sequenz { get; init; }

    /// <summary>Kurzstand: Dateien, Bytes, Git – damit ein Blick genügt.</summary>
    [JsonPropertyName("stand")] public BridgeStand? Stand { get; init; }
}

/// <summary>
/// Prüft Aufträge, bevor irgendetwas ausgeführt wird.
/// </summary>
/// <remarks>
/// Hier liegt die eigentliche Sicherheit der Brücke. Ein Auftrag kommt aus dem Netz; er darf
/// ausschließlich:
/// <list type="bullet">
/// <item>das richtige Kennwort tragen,</item>
/// <item>eine der vier Auftragsarten sein,</item>
/// <item>einen Befehl nennen, der in <see cref="CommandCatalog"/> steht,</item>
/// <item>einen Pfad nennen, der innerhalb des Projektordners liegt.</item>
/// </list>
/// Alles andere wird abgelehnt – und die Ablehnung geht als Ergebnis zurück, damit die andere Seite
/// weiß, warum nichts passiert ist.
/// </remarks>
public static class BridgeProtocol
{
    public const string ManifestDatei = "manifest.json";
    public const string ManifestLesbarDatei = "workspace.md";
    public const string HelloDatei = "hello.json";

    /// <summary>Größter erlaubter Dateiinhalt eines Schreibauftrags – Text wie Binär (4 MB).</summary>
    public const int MaxInhaltBytes = 4 * 1024 * 1024;

    /// <summary>
    /// Der Änderungsverlauf: eine Datei mit den letzten <see cref="BridgeChronik.MaxEintraege"/>
    /// Änderungen und dem Kurzstand. Sie ist der billige Weg zum genauen Blick – ein volles Abbild
    /// kostet rund 114 Bytes je Datei, ein Eintrag hier rund 70 Bytes.
    /// </summary>
    public const string ChronikDatei = "chronik.json";
    public const string LogDatei = "log.txt";

    /// <summary>Auftragsdateien heißen <c>job-*.json</c>, Ergebnisse <c>result-*.json</c>.</summary>
    public const string JobPraefix = "job-";
    public const string ResultPraefix = "result-";

    public static readonly IReadOnlyList<string> JobArten =
        Enum.GetNames<JobKind>().Select(n => n.ToLowerInvariant()).ToList();

    /// <param name="vorschlaegeErlaubt">
    /// Das Häkchen <i>Vorschläge vorlegen lassen</i>. Ein Vorschlag ist ein <b>eigener</b> Weg mit
    /// eigener Zustimmung: er hängt nicht an der Befehlsliste (der Nutzer entscheidet ohnehin im
    /// Freigabefenster, und ohne Fenster läuft nichts). Bis rc9 verlangte er zusätzlich die
    /// Befehlsfreigabe – wer nur Vorschläge erlauben wollte, bekam „Vorschläge sind in den
    /// Einstellungen nicht freigegeben“ zu sehen, obwohl er genau das angekreuzt hatte.
    /// </param>
    public static (bool Ok, string Fehler) Pruefe(
        BridgeJob job,
        string erwartetesToken,
        string projektOrdner,
        bool befehleErlaubt,
        bool schreibendErlaubt,
        bool vorschlaegeErlaubt)
    {
        if (string.IsNullOrWhiteSpace(job.Id) || job.Id.Length > 96
            || !job.Id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
        {
            return (false, "id fehlt");
        }

        if (string.IsNullOrWhiteSpace(erwartetesToken) || !string.Equals(job.Token, erwartetesToken, StringComparison.Ordinal))
        {
            return (false, "Kennwort stimmt nicht");
        }

        if (!string.IsNullOrWhiteSpace(job.Projekt) && !string.Equals(job.Projekt.TrimEnd('\\', '/'), projektOrdner.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
            return (false, "Auftrag gehört zu einem anderen Projekt");
        var art = job.KindAlsEnum;
        if (art is null)
        {
            return (false, $"unbekannte Auftragsart »{job.Kind}«");
        }

        switch (art)
        {
            case JobKind.Run:
                if (!befehleErlaubt)
                {
                    return (false, "Befehle sind in den Einstellungen nicht freigegeben");
                }

                if (string.IsNullOrWhiteSpace(job.Command))
                {
                    return (false, "command fehlt");
                }

                var befehl = CommandCatalog.Find(job.Command!);
                if (befehl is null)
                {
                    return (false, $"Befehl »{job.Command}« steht nicht in der Liste");
                }

                if (befehl.Kind == CommandKind.Write && !schreibendErlaubt)
                {
                    return (false, $"»{befehl.Id}« schreibt und ist nicht freigegeben");
                }

                return (true, string.Empty);

            case JobKind.Vorschlag:
                if (!vorschlaegeErlaubt)
                {
                    return (false, "Vorschläge sind in den Einstellungen nicht freigegeben");
                }

                if (!CommandCatalog.ProgrammErlaubt(job.Program))
                {
                    return (false, $"Programm »{job.Program}« ist für Vorschläge nicht zugelassen");
                }

                var vorschlagArg = job.Argumente ?? [];
                if (vorschlagArg.Count > CommandCatalog.VorschlagMaxArgumente)
                {
                    return (false, $"zu viele Argumente ({vorschlagArg.Count})");
                }

                if (vorschlagArg.Any(a => a is null || a.Length > CommandCatalog.VorschlagMaxArgumentLaenge))
                {
                    return (false, "ein Argument ist zu lang");
                }

                // Fund aus dem Master-Check (24.09.2026): »cmd« und »pwsh« sind selbst eine Shell,
                // »python« und »node« nehmen Code an. Ein Argument wie »a && del x« wäre dort kein
                // Argument mehr, sondern ein zweiter Befehl – und das Freigabefenster, das den Aufruf
                // bestätigt, kann das nicht auffangen. Deshalb: Zeichen, die verketten, werden abgelehnt.
                if (CommandCatalog.KannSelbstBefehleAusfuehren(job.Program)
                    && vorschlagArg.Any(a => !CommandCatalog.ArgumentIstUnbedenklich(a)))
                {
                    return (false, $"Programm »{job.Program}« führt Befehle selbst aus – ein Argument enthält "
                                 + "Zeichen, die weitere Befehle verketten (eines von & | > < ^ $ ; ` \" oder ein "
                                 + "Zeilenumbruch). Bitte den Aufruf einzeln schicken, ohne Verkettung.");
                }

                if (string.IsNullOrWhiteSpace(job.Zweck))
                {
                    return (false, "zweck fehlt – der Nutzer soll wissen, was er da freigibt");
                }

                if (job.Zweck!.Length > 400)
                {
                    return (false, "zweck ist zu lang");
                }

                return (true, string.Empty);

            case JobKind.Read:
            case JobKind.Write:
                if (string.IsNullOrWhiteSpace(job.Path))
                {
                    return (false, "path fehlt");
                }

                if (!SichererPfad(projektOrdner, job.Path!, out var fehler))
                {
                    return (false, fehler);
                }

                if (art == JobKind.Write)
                {
                    // Befund vom 21.09.2026: Bis rc10 prüfte die Schreibfreigabe nur die feste
                    // Befehlsliste (»publish«). Ein Datei-Schreibauftrag lief auch dann durch, wenn
                    // »Schreibende Befehle erlauben« aus war – das Häkchen war also schwächer als sein
                    // eigener Hilfetext (»sowie das Ablegen einzelner Dateien im Projektordner«).
                    if (!schreibendErlaubt)
                    {
                        return (false, "Dateien schreiben ist nicht freigegeben");
                    }

                    if (job.Content is null)
                    {
                        return (false, "content fehlt");
                    }

                    if (!job.KodierungBekannt)
                    {
                        return (false, $"unbekannte Kodierung »{job.Kodierung}« (erlaubt: text, base64)");
                    }

                    if (job.Content.Length > MaxInhaltBytes * 2)
                    {
                        return (false, "content zu gross");
                    }

                    if (job.IstBase64)
                    {
                        // Base64 braucht rund 4/3 des Platzes; geprüft wird die echte Größe,
                        // nicht die Länge der Verpackung.
                        var platz = (job.Content.Length / 4 * 3) + 3;
                        if (!Convert.TryFromBase64String(job.Content, new byte[platz], out var bytes))
                        {
                            return (false, "content ist kein gültiges Base64");
                        }

                        if (bytes > MaxInhaltBytes)
                        {
                            return (false, $"Inhalt zu gross ({bytes / 1024 / 1024} MB, Grenze 4 MB)");
                        }
                    }
                    else
                    {
                        if (System.Text.Encoding.UTF8.GetByteCount(job.Content) > MaxInhaltBytes)
                        {
                            return (false, "content zu gross (Grenze 4 MB)");
                        }

                        // Befund vom 21.09.2026 (TODO 8.18): Ein Inhalt, in dem der Redaktions-
                        // platzhalter steht, stammt mit hoher Wahrscheinlichkeit aus einem „read“.
                        // Wer ihn zurückschreibt, macht die Datei kaputt (so geschehen: fünf Zeilen
                        // weg, vier Compilerfehler). Deshalb: Ablehnung statt Datenverlust.
                        //
                        // Und seit rc18 (TODO 8.20): Wer den Platzhalter bewusst mitschickt – etwa in
                        // dieser Dokumentation, die ihn erklären muss –, setzt `platzhalterErlaubt`.
                        // Ohne das Feld bleibt die Sperre scharf.
                        if (!job.PlatzhalterErlaubt
                            && job.Content.Contains(SecretRedaction.Platzhalter, StringComparison.Ordinal))
                        {
                            return (false,
                                $"content enthält den Platzhalter {SecretRedaction.Platzhalter} – das ist ein beim Lesen " +
                                "redigierter Text. Er darf nicht zurückgeschrieben werden: Die Datei würde dabei verstümmelt. " +
                                "Soll der Platzhalter bewusst im Text stehen (z. B. in einer Erklärung), dann den Auftrag mit " +
                                "»platzhalterErlaubt« schicken.");
                        }
                    }
                }

                return (true, string.Empty);

            default:
                return (true, string.Empty);
        }
    }

    /// <summary>Liegt der Pfad wirklich im Projektordner? Kein Ausbruch, keine Laufwerke, keine UNC-Pfade.</summary>
    public static bool SichererPfad(string projektOrdner, string relativOderAbsolut, out string fehler)
    {
        fehler = string.Empty;
        if (string.IsNullOrWhiteSpace(relativOderAbsolut))
        {
            fehler = "Pfad ist leer";
            return false;
        }

        if (string.IsNullOrWhiteSpace(projektOrdner))
        {
            fehler = "kein Projektordner gewählt";
            return false;
        }

        // Master-Check 24.09.2026 (M1): Hier stand eine zweite, eigene Pfadprüfung. Sie war
        // schwächer als die des Imports (PathSafety) und ließ durch, was dort seit Phase 1
        // verboten ist: den Doppelpunkt (Windows-Datenstrom »datei.txt:geheim«), reservierte
        // Gerätenamen (CON, NUL, COM1), Segmente mit abschließendem Punkt, Steuerzeichen und
        // Überlängen. Zwei Prüfungen für dieselbe Frage sind eine zu viel – jetzt gilt für
        // jeden Weg dieselbe.
        if (!PathSafety.TryResolveInside(projektOrdner, relativOderAbsolut, out _, out _, out var grund))
        {
            fehler = "nur Pfade innerhalb des Projektordners – " + grund;
            return false;
        }

        var full = Path.GetFullPath(Path.Combine(projektOrdner, relativOderAbsolut.Replace('\\', Path.DirectorySeparatorChar)));
        var current = full;
        var rootFull = Path.GetFullPath(projektOrdner).TrimEnd(Path.DirectorySeparatorChar);
        while (current.Length >= rootFull.Length)
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                fehler = "Symbolischer Link/Junction im Projektpfad – Zugriff blockiert.";
                return false;
            }
            if (string.Equals(current.TrimEnd(Path.DirectorySeparatorChar), rootFull, StringComparison.OrdinalIgnoreCase)) break;
            current = Path.GetDirectoryName(current) ?? string.Empty;
        }
        return true;
    }

    /// <summary>Baut das JSON eines Auftrags (für die andere Seite).</summary>
    public static string ToJson<T>(T wert) => JsonSerializer.Serialize(wert, new JsonSerializerOptions
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    });

    public static T? FromJson<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
