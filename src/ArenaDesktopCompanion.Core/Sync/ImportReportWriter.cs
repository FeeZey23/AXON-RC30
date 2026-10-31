using System.Text;

namespace ArenaDesktopCompanion.Core.Sync;

/// <summary>
/// Jeder Import-Lauf hinterlässt eine lesbare Datei, damit man auch nachher noch weiß,
/// was der Bot warum getan (oder warum er etwas NICHT getan) hat.
/// </summary>
/// <remarks>
/// <para>
/// <b>Eine Datei je Arena-Chat, nicht je Lauf.</b> Vorher entstand bei jedem Watcher-Lauf ein
/// eigener Bericht (<c>import-20260921-022016.md</c>, <c>…-022018.md</c>, …) – an einem Nachmittag
/// waren das über hundert Dateien mit je drei Zeilen Inhalt, und die eigentliche Frage („was ist in
/// diesem Chat passiert?“) beantwortete keine davon. Jetzt gibt es pro Chat <i>eine</i> Datei, die
/// der Lauf fortschreibt.
/// </para>
/// <para>
/// Der Name kommt aus dem Chat, wie ihn die Adresse der Seite nennt: Arena zeigt Chats unter
/// <c>/agent/&lt;id&gt;</c>. Ohne Chat-Bezug heißt die Datei <c>inbox.md</c>.
/// </para>
/// </remarks>
public static class ImportReportWriter
{
    public const string DefaultTopic = "inbox";
    private const string Title = "# Arena-Import – Bericht";

    public static string ReportsDirectory(string projectRoot)
        => Path.Combine(projectRoot, InboxProcessor.InboxFolderName, "reports");

    /// <summary>Dateiname eines Berichts (ohne Ordner) – auch für die Anzeige im Protokoll.</summary>
    public static string FileName(string? topic) => $"{Sanitize(topic)}.md";

    public static string PathFor(string projectRoot, string? topic)
        => Path.Combine(ReportsDirectory(projectRoot), FileName(topic));

    /// <summary>
    /// Schreibt den Lauf in den Bericht des Themas: gibt es ihn noch nicht, entsteht die Datei mit
    /// Kopf und erster Tabelle; gibt es ihn, wird der neue Abschnitt oben angehängt (neuester Stand
    /// zuerst). Die Datei wird atomar ersetzt, ein halb geschriebener Bericht ist damit ausgeschlossen.
    /// </summary>
    public static string Write(string projectRoot, InboxRunResult run, string trigger, string? topic = null, DateTime? zeitpunkt = null)
    {
        var dir = ReportsDirectory(projectRoot);
        Directory.CreateDirectory(dir);
        var path = PathFor(projectRoot, topic);
        var jetzt = zeitpunkt ?? DateTime.Now;

        var neuerAbschnitt = Abschnitt(run, trigger, projectRoot, jetzt);
        var inhalt = File.Exists(path)
            ? VorneAnfügen(File.ReadAllText(path, Encoding.UTF8), neuerAbschnitt)
            : Kopf(topic) + neuerAbschnitt;

        var temp = path + ".tmp";
        File.WriteAllText(temp, inhalt, IO.FileHash.Utf8NoBom);
        File.Move(temp, path, overwrite: true);
        return path;
    }

    private static string Kopf(string? topic)
        => $"{Title}\n\n"
         + $"- Thema: {Thema(topic)}\n"
         + "- Aufbau: neuester Lauf zuerst; jeder Lauf ein Abschnitt mit Zeitpunkt und Ergebnis\n\n";

    private static string Abschnitt(InboxRunResult run, string trigger, string projectRoot, DateTime jetzt)
    {
        var sb = new StringBuilder();
        sb.Append("## ").Append(jetzt.ToString("yyyy-MM-dd HH:mm:ss")).Append('\n').Append('\n')
          .Append("- Ausgelöst durch: ").Append(trigger).Append('\n')
          .Append("- Projektordner: `").Append(projectRoot).Append("`\n")
          .Append("- Ergebnis: ").Append(run.Summarize()).Append("\n\n")
          .Append("| Datei | Ergebnis | Notiz |\n|---|---|---|\n");

        foreach (var item in run.Items)
        {
            sb.Append("| `").Append(Escape(item.Result.RelativePath)).Append("` | ")
              .Append(item.Result.Outcome).Append(" | ")
              .Append(Escape(item.Result.Message));
            if (item.Result.BackupRelativePath is not null)
            {
                sb.Append(" · Backup: `").Append(Escape(item.Result.BackupRelativePath)).Append('`');
            }
            if (item.Result.ConflictRelativePath is not null)
            {
                sb.Append(" · Konflikt: `").Append(Escape(item.Result.ConflictRelativePath)).Append('`');
            }
            sb.Append(" |\n");
        }

        if (run.Items.Count == 0)
        {
            sb.Append("| – | – | Keine Dateien in diesem Lauf. |\n");
        }

        if (run.Errors.Count > 0)
        {
            sb.Append("\n### Fehler\n\n");
            foreach (var error in run.Errors)
            {
                sb.Append("- ").Append(Escape(error)).Append('\n');
            }
        }

        sb.Append('\n');
        return sb.ToString();
    }

    /// <summary>Neuer Abschnitt direkt hinter den Kopf, vor allem Älteren.</summary>
    private static string VorneAnfügen(string vorhanden, string abschnitt)
    {
        var grenze = vorhanden.IndexOf("\n## ", StringComparison.Ordinal);
        if (grenze < 0)
        {
            // Kein Abschnitt vorhanden (z. B. von Hand geleerte Datei): Kopf erhalten, Abschnitt dran.
            var kopfEnde = vorhanden.EndsWith('\n') ? vorhanden : vorhanden + "\n\n";
            return kopfEnde + abschnitt;
        }

        return vorhanden[..(grenze + 1)] + abschnitt + vorhanden[(grenze + 1)..];
    }

    private static string Thema(string? topic) => Sanitize(topic);

    /// <summary>
    /// Das Thema eines Berichts aus der Adresse der Seite: Arena zeigt Chats unter
    /// <c>/agent/&lt;id&gt;</c>. Alles andere (Startseite, Downloads) gehört zum Sammelbericht „inbox“.
    /// </summary>
    public static string TopicFromUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var adresse))
        {
            return DefaultTopic;
        }

        var teile = adresse.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < teile.Length - 1; i++)
        {
            if (teile[i].Equals("agent", StringComparison.OrdinalIgnoreCase)
                || teile[i].Equals("chat", StringComparison.OrdinalIgnoreCase)
                || teile[i].Equals("thread", StringComparison.OrdinalIgnoreCase))
            {
                var kennung = teile[i + 1];
                if (kennung.Length >= 6 && kennung.All(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.'))
                {
                    return "chat-" + kennung;
                }
            }
        }

        return DefaultTopic;
    }

    /// <summary>
    /// Aus „Chat: /agent/0f0…9ac3“ wird ein Dateiname. Erlaubt sind Buchstaben, Ziffern, Punkt,
    /// Binde- und Unterstrich – alles andere wird zu „-“, und bei leerem Ergebnis bleibt „inbox“.
    /// </summary>
    public static string Sanitize(string? topic)
    {
        if (string.IsNullOrWhiteSpace(topic))
        {
            return DefaultTopic;
        }

        var sb = new StringBuilder(topic.Length);
        foreach (var c in topic.Trim())
        {
            sb.Append(char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '-');
        }

        var name = sb.ToString().Trim('-', '.');
        while (name.Contains("--", StringComparison.Ordinal))
        {
            name = name.Replace("--", "-", StringComparison.Ordinal);
        }

        if (name.Length > 60)
        {
            name = name[..60].TrimEnd('-', '.');
        }

        return name.Length == 0 ? DefaultTopic : name;
    }

    private static string Escape(string? text)
        => (text ?? string.Empty).Replace("|", "\\|", StringComparison.Ordinal).Replace('\n', ' ');
}
