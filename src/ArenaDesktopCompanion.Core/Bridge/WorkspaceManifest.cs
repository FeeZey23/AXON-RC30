using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ArenaDesktopCompanion.Core.Agents;

namespace ArenaDesktopCompanion.Core.Bridge;

/// <summary>Ein Eintrag des Verzeichnis-Abbilds: Pfad, Größe, Kurz-Hash, Änderungszeit.</summary>
/// <param name="Path">Pfad relativ zum Projektordner, immer mit »/«.</param>
/// <param name="Bytes">Größe in Bytes.</param>
/// <param name="Hash">Erste 16 Zeichen des SHA-256 (genug, um Änderungen zu erkennen).</param>
/// <param name="ModifiedUtc">Änderungszeit in UTC, ISO-8601.</param>
public sealed record ManifestEntry(string Path, long Bytes, string Hash, string ModifiedUtc);

/// <summary>Der Zustand der Versionsgeschichte im Projektordner.</summary>
public sealed record GitState(string? Head, string? Branch, int Dirty, int Untracked, string? Remote);

/// <summary>
/// Das kompakte Abbild des Projektordners.
/// </summary>
/// <remarks>
/// <para>
/// Warum ein eigenes Format statt „schick mir den Ordner“: Der Assistent arbeitet in einer Umgebung
/// mit begrenztem Platz und begrenzter Dateizahl. Ein vollständiges Abbild wäre nach wenigen Minuten
/// zu groß. Dieses Abbild beschreibt den Ordner vollständig, ohne Inhalte zu übertragen – Pfad,
/// Größe, Kurz-Hash und Zeitpunkt je Datei. Damit ist jede Frage nach dem Aufbau, jeder Vergleich
/// zwischen zwei Ständen und jede Änderungserkennung möglich; Inhalte werden nur angefordert, wo sie
/// wirklich gebraucht werden.
/// </para>
/// <para>
/// Für 10 000 Dateien sind das rund 700 KB – ein einzelner Eintrag, keine Dateiflut.
/// </para>
/// </remarks>
public sealed record WorkspaceManifest
{
    public const int Version = 1;

    [JsonPropertyName("v")] public int SchemaVersion { get; init; } = Version;
    [JsonPropertyName("time")] public string Time { get; init; } = string.Empty;
    [JsonPropertyName("root")] public string Root { get; init; } = string.Empty;
    [JsonPropertyName("host")] public string Host { get; init; } = string.Empty;
    [JsonPropertyName("app")] public string AppVersion { get; init; } = string.Empty;
    [JsonPropertyName("tools")] public Dictionary<string, string> Tools { get; init; } = new();
    [JsonPropertyName("git")] public GitState? Git { get; init; }
    [JsonPropertyName("files")] public IReadOnlyList<ManifestEntry> Files { get; init; } = [];
    [JsonPropertyName("counts")] public Dictionary<string, long> Counts { get; init; } = new();
    [JsonPropertyName("inbox")] public Dictionary<string, int> Inbox { get; init; } = new();
    [JsonPropertyName("log")] public IReadOnlyList<string> Log { get; init; } = [];
    [JsonPropertyName("stat")] public string Stat { get; init; } = string.Empty;
}

/// <summary>Baut das Abbild. Reine Dateisystemarbeit – kein Windows nötig, deshalb prüfbar.</summary>
/// <summary>
/// Gedächtnis für Datei-Hashes: Pfad → (Größe, Änderungszeit, Hash).
/// </summary>
/// <remarks>
/// Anlass (24.09.2026, live): Der Companion stand still, weil ein entpackter PostgreSQL-Baum
/// (~21 900 Dateien, ~1,4 GB) im Projektordner lag. Das Abbild rechnete bei <b>jedem</b> Takt
/// (alle 20 s) für <b>jede</b> Datei einen SHA-256 – der Takt dauerte dadurch Minuten, und weil der
/// Takt die Schleife ist, gab es so lange kein Lebenszeichen und keine Auftragsannahme. Die
/// Gegenseite sah eine tote Brücke, obwohl das Programm lief.
/// Jetzt wird nur noch gehasht, was sich geändert hat (gleiche Größe <i>und</i> gleiche
/// Änderungszeit = gleicher Inhalt): aus Minuten werden Millisekunden.
/// </remarks>
public sealed class HashCache
{
    private readonly Dictionary<string, Eintrag> _eintraege = new(StringComparer.OrdinalIgnoreCase);

    private readonly record struct Eintrag(long Laenge, DateTime MtimeUtc, string Hash);

    /// <summary>Wie oft ein bereits bekannter Hash wiederverwendet wurde.</summary>
    public int Treffer { get; private set; }

    /// <summary>Wie oft wirklich gerechnet werden musste.</summary>
    public int NeuBerechnet { get; private set; }

    public int Count => _eintraege.Count;

    public string Hole(string pfad, long laenge, DateTime mtimeUtc, Func<string, string> berechnen)
    {
        if (_eintraege.TryGetValue(pfad, out var alt)
            && alt.Laenge == laenge
            && alt.MtimeUtc == mtimeUtc)
        {
            Treffer++;
            return alt.Hash;
        }

        var hash = berechnen(pfad);
        _eintraege[pfad] = new Eintrag(laenge, mtimeUtc, hash);
        NeuBerechnet++;
        return hash;
    }
}

public static class WorkspaceManifestBuilder
{
    /// <summary>
    /// Ordner, die nie ins Abbild gehören (dieselben Regeln wie beim Import – plus die Ablagen der
    /// Werkzeuge). Befund vom 26.09.2026, live: Ein Projektordner mit <b>347 715</b> Dateien
    /// (Werkzeug-Ablagen, Bauausgaben, Crawl-Reste) sprengte das Abbild. Was hier fehlt, wird
    /// mitgezählt – deshalb steht die Liste jetzt genau so da wie im
    /// <see cref="ArenaDesktopCompanion.Core.Sync.WorkspaceFileFilter"/>.
    /// </summary>
    private static readonly string[] Uebersprungen =
    [
        ".git", ".svn", ".hg", "bin", "obj", ".vs", ".idea", ".next", ".nuxt", "dist", "target",
        "__pycache__", ".venv", "venv", "node_modules", "publish", "packages", "TestResults",
        ".dotnet", ".nuget", ".templateengine",
        ".arena-inbox", ".arena-staging", "Backup", ".arena-backup", ".arena-conflicts",
    ];

    /// <summary>So viele Protokollzeilen wandern mit (redigiert).</summary>
    public const int LogZeilen = 40;

    /// <summary>Obergrenze für Dateien im Abbild; darüber wird abgeschnitten und im Kopf vermerkt.</summary>
    public const int MaxDateien = 40_000;

    /// <summary>
    /// Obergrenze für die <b>Größe</b> des Abbilds. Der Briefkasten nimmt 4 MB je Datei auf
    /// (<see cref="RelayClient.MaxDateiBytes"/>); bis rc24 wurde ein größeres Abbild einfach nicht
    /// abgelegt – die Brücke stand dann dauerhaft auf „gestört: Datei zu groß für den Briefkasten"
    /// und die Gegenseite bekam gar nichts. 1,5 MB lassen Luft für Kopf, Werkzeuge, Protokollzeilen
    /// und Verlauf und halten den Takt schnell.
    /// </summary>
    public const long MaxBytes = 1_500_000;

    public static WorkspaceManifest Build(
        string projectRoot,
        string? host = null,
        string appVersion = "",
        IReadOnlyDictionary<string, string>? tools = null,
        GitState? git = null,
        IEnumerable<string>? logZeilen = null,
        IReadOnlyDictionary<string, int>? inbox = null,
        string? gitStat = null,
        DateTime? jetzt = null,
        HashCache? cache = null,
        long maxBytes = MaxBytes)
    {
        var wurzel = Path.GetFullPath(projectRoot);
        var eintraege = new List<ManifestEntry>();
        // Zählwerte je Lauf, nicht kumulativ: „dieser Scan hat X Hashes gespart / Y gerechnet".
        var trefferVorher = cache?.Treffer ?? 0;
        var neuVorher = cache?.NeuBerechnet ?? 0;
        long gesamt = 0;
        long budget = 0;
        var abgeschnitten = false;

        if (Directory.Exists(wurzel))
        {
            foreach (var datei in Aufzaehlen(wurzel))
            {
                if (eintraege.Count >= MaxDateien)
                {
                    abgeschnitten = true;
                    break;
                }

                var relativ = Path.GetRelativePath(wurzel, datei).Replace('\\', '/');
                try
                {
                    var info = new FileInfo(datei);
                    var laenge = info.Length;

                    // Platz schätzen, bevor gerechnet wird: Pfad + Größe, Hash, Zeit, Anführungszeichen.
                    // Die Reihenfolge der Aufzählung ist stabil (derselbe Ordner liefert dieselbe Folge),
                    // damit der Vergleich zweier Stände nicht bei jedem Takt alles als „neu" meldet.
                    var platz = relativ.Length + 96;
                    if (budget + platz > maxBytes)
                    {
                        abgeschnitten = true;
                        break;
                    }

                    budget += platz;
                    gesamt += laenge;
                    // Nur rechnen, wenn Größe ODER Änderungszeit abweichen (siehe HashCache).
                    var hash = cache is null
                        ? KurzHash(datei)
                        : cache.Hole(datei, laenge, info.LastWriteTimeUtc, KurzHash);
                    eintraege.Add(new ManifestEntry(
                        relativ,
                        laenge,
                        hash,
                        info.LastWriteTimeUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Eine Datei, die gerade gesperrt ist, darf das Abbild nicht verhindern.
                }
            }
        }

        eintraege.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));

        var logs = (logZeilen ?? [])
            .TakeLast(LogZeilen)
            .Select(z => SecretRedaction.Clean(z))
            .ToList();

        return new WorkspaceManifest
        {
            Time = (jetzt ?? DateTime.Now).ToString("yyyy-MM-dd'T'HH:mm:ssK", CultureInfo.InvariantCulture),
            Root = wurzel,
            Host = host ?? Environment.MachineName,
            AppVersion = appVersion,
            Tools = tools is null ? new Dictionary<string, string>() : new Dictionary<string, string>(tools),
            Git = git,
            Files = eintraege,
            Counts = new Dictionary<string, long>
            {
                ["files"] = eintraege.Count,
                ["bytes"] = gesamt,
                ["truncated"] = abgeschnitten ? 1 : 0,
                ["hashTreffer"] = (cache?.Treffer ?? 0) - trefferVorher,
                ["hashNeu"] = (cache?.NeuBerechnet ?? 0) - neuVorher,
            },
            Inbox = inbox is null ? new Dictionary<string, int>() : new Dictionary<string, int>(inbox),
            Log = logs,
            Stat = SecretRedaction.Clean(gitStat ?? string.Empty),
        };
    }

    /// <summary>
    /// Aufzählung mit den Regeln des Imports: Systemmüll und die Ordner des Companions bleiben draußen.
    /// Wichtig für die Vergleichbarkeit – ein Abbild, das <c>bin/</c> enthält, ändert sich bei jedem Bauen.
    /// </summary>
    private static IEnumerable<string> Aufzaehlen(string wurzel)
    {
        var stapel = new Stack<string>();
        stapel.Push(wurzel);

        while (stapel.Count > 0)
        {
            var ordner = stapel.Pop();
            string[] unterordner;
            string[] dateien;
            try
            {
                unterordner = Directory.GetDirectories(ordner);
                dateien = Directory.GetFiles(ordner);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var datei in dateien)
            {
                yield return datei;
            }

            foreach (var unter in unterordner)
            {
                var name = Path.GetFileName(unter);
                if (Uebersprungen.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                stapel.Push(unter);
            }
        }
    }

    /// <summary>Erste 16 Zeichen des SHA-256 – genug zur Änderungserkennung, spart die Hälfte der Zeichen.</summary>
    /// <summary>
    /// Fingerabdruck des <b>Dateibestands</b>: Dateien, Zählwerte, Git. Zeitstempel des Abbilds und
    /// Protokollzeilen bleiben absichtlich draußen – sonst wäre der Fingerabdruck bei jedem Takt ein
    /// anderer und das schwere Abbild (bei 2 500 Dateien 286 KB) würde ständig neu geschickt, obwohl
    /// sich am Bestand nichts geändert hat. Genau das war der Fehler in der ersten Fassung.
    /// </summary>
    public static string StandHash(WorkspaceManifest manifest)
    {
        var ohneZeit = manifest with { Time = string.Empty, Log = [] };
        return Fingerabdruck(ToJson(ohneZeit));
    }

    /// <summary>
    /// Fingerabdruck der <b>kleinen Dinge</b>: Protokollzeilen, Inbox-Zahlen, Git-Stand. Ändert er
    /// sich, wird die Kurzfassung und der Änderungsverlauf nachgeschickt – ein paar Kilobyte.
    /// </summary>
    public static string KleinHash(WorkspaceManifest manifest)
        => Fingerabdruck(string.Join('\n',
            manifest.Log.Concat([manifest.Inbox.Count.ToString(CultureInfo.InvariantCulture),
                manifest.Counts.GetValueOrDefault("files").ToString(CultureInfo.InvariantCulture),
                manifest.Git?.Head ?? string.Empty, manifest.Git?.Branch ?? string.Empty,
                manifest.Git?.Dirty.ToString(CultureInfo.InvariantCulture) ?? string.Empty])));

    private static string Fingerabdruck(string text)
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(text)));

    public static string KurzHash(string datei)
    {
        try
        {
            using var strom = File.OpenRead(datei);
            var hash = SHA256.HashData(strom);
            return Convert.ToHexString(hash)[..16].ToLowerInvariant();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "0000000000000000";
        }
    }

    public static string ToJson(WorkspaceManifest manifest, bool kompakt = true)
        => JsonSerializer.Serialize(manifest, new JsonSerializerOptions
        {
            WriteIndented = !kompakt,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        });

    /// <summary>
    /// Vergleicht zwei Stände: Was kam dazu, was ist weg, was hat sich geändert?
    /// Das ist die Grundlage für „was hat sich seit meinem letzten Blick getan?“.
    /// </summary>
    public static (List<string> Neu, List<string> Geaendert, List<string> Entfernt) Diff(
        WorkspaceManifest alt, WorkspaceManifest neu)
    {
        var vorher = alt.Files.ToDictionary(f => f.Path, f => f.Hash, StringComparer.Ordinal);
        var nachher = neu.Files.ToDictionary(f => f.Path, f => f.Hash, StringComparer.Ordinal);

        var dazu = nachher.Keys.Where(p => !vorher.ContainsKey(p)).OrderBy(p => p, StringComparer.Ordinal).ToList();
        var weg = vorher.Keys.Where(p => !nachher.ContainsKey(p)).OrderBy(p => p, StringComparer.Ordinal).ToList();
        var anders = nachher.Keys
            .Where(p => vorher.TryGetValue(p, out var altHash) && !string.Equals(altHash, nachher[p], StringComparison.Ordinal))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        return (dazu, anders, weg);
    }

    /// <summary>
    /// Eine lesbare Fassung des Abbilds für Mensch und Assistent: Kennzahlen, Aufbau, die größten
    /// Dateien, letzte Änderungen und der Zustand der Versionsgeschichte – kurz genug, um sie in
    /// einem Zug zu lesen, und ohne den ganzen Dateibaum zu wiederholen.
    /// </summary>
    public static string ToMarkdown(WorkspaceManifest m, WorkspaceManifest? vorher = null, int maxZeilen = 200)
    {
        var sb = new StringBuilder();
        sb.Append("# Arbeitsordner-Abbild\n\n")
          .Append($"- Zeitpunkt: {m.Time}\n")
          .Append($"- Rechner: {m.Host}   ·   Programm: {m.AppVersion}\n")
          .Append($"- Ordner: `{m.Root}`\n")
          .Append($"- Umfang: {m.Counts.GetValueOrDefault("files")} Dateien, ")
          .Append($"{m.Counts.GetValueOrDefault("bytes") / 1024.0:0.#} KB")
          .Append(m.Counts.GetValueOrDefault("truncated") == 1
              ? "  (**Liste gekürzt – der Ordner ist größer, als in den Briefkasten passt;"
                + " nenne beim Fragen einen Unterordner**)"
              : string.Empty)
          .Append('\n');

        if (m.Git is { } version)
        {
            sb.Append($"- Version: {version.Branch ?? "?"} @ {Kurz(version.Head ?? "unbekannt")}")
              .Append(version.Dirty > 0 ? $"  ·  **{version.Dirty} geänderte**" : "  ·  nichts geändert")
              .Append(version.Untracked > 0 ? $", {version.Untracked} neue Dateien" : string.Empty)
              .Append(version.Remote is not null ? $"  ·  {version.Remote}" : string.Empty)
              .Append('\n');
        }

        if (m.Tools.Count > 0)
        {
            sb.Append("- Werkzeuge: ").Append(string.Join(" · ",
                m.Tools.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => $"{k.Key} {k.Value}"))).Append('\n');
        }

        if (m.Inbox.Count > 0)
        {
            sb.Append("- Inbox: ").Append(string.Join(" · ", m.Inbox.Select(k => $"{k.Key}={k.Value}"))).Append('\n');
        }

        if (vorher is not null)
        {
            var (dazu, anders, weg) = Diff(vorher, m);
            sb.Append("\n## Seit dem letzten Abbild\n\n");
            sb.Append($"- neu: {dazu.Count} · geändert: {anders.Count} · entfernt: {weg.Count}\n");
            foreach (var p in anders.Take(15)) sb.Append($"  - geändert: `{p}`\n");
            foreach (var p in dazu.Take(15)) sb.Append($"  - neu: `{p}`\n");
            foreach (var p in weg.Take(15)) sb.Append($"  - entfernt: `{p}`\n");
        }

        // Aufbau: Ordner mit Anzahl und Summe – der Baum selbst bleibt in der JSON-Datei.
        sb.Append("\n## Ordner\n\n| Ordner | Dateien | KB |\n|---|---:|---:|\n");
        var gruppen = m.Files
            .GroupBy(f => f.Path.Contains('/') ? f.Path[..f.Path.IndexOf('/')] : ".")
            .Select(g => (Ordner: g.Key, Anzahl: g.Count(), Bytes: g.Sum(x => x.Bytes)))
            .OrderByDescending(g => g.Bytes)
            .Take(30);
        foreach (var g in gruppen)
        {
            sb.Append($"| `{g.Ordner}` | {g.Anzahl} | {g.Bytes / 1024.0:0.#} |\n");
        }

        sb.Append("\n## Größte Dateien\n\n");
        foreach (var f in m.Files.OrderByDescending(f => f.Bytes).Take(15))
        {
            sb.Append($"- `{f.Path}` ({f.Bytes / 1024.0:0.#} KB)\n");
        }

        sb.Append("\n## Zuletzt geändert\n\n");
        foreach (var f in m.Files.OrderByDescending(f => f.ModifiedUtc, StringComparer.Ordinal).Take(15))
        {
            sb.Append($"- {f.ModifiedUtc}  `{f.Path}`\n");
        }

        if (!string.IsNullOrWhiteSpace(m.Stat))
        {
            sb.Append("\n## Änderungen im Einzelnen (git diff --stat)\n\n```\n").Append(m.Stat.Trim()).Append("\n```\n");
        }

        if (m.Log.Count > 0)
        {
            sb.Append("\n## Protokoll (Auszug)\n\n```\n").Append(string.Join('\n', m.Log)).Append("\n```\n");
        }

        var text = sb.ToString();
        var zeilen = text.Split('\n');
        return zeilen.Length <= maxZeilen ? text : string.Join('\n', zeilen.Take(maxZeilen)) + "\n… (gekürzt)\n";
    }

    private static string Kurz(string hash) => hash.Length <= 7 ? hash : hash[..7];
}
