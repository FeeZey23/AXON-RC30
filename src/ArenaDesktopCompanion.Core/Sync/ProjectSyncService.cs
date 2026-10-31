using System.IO;
using ArenaDesktopCompanion.Core.Diagnostics;
using ArenaDesktopCompanion.Core.Import;
using ArenaDesktopCompanion.Core.Settings;

namespace ArenaDesktopCompanion.Core.Sync;

/// <summary>
/// Bindeglied zwischen Fenster und Core: hält Projektordner, Sync-Richtlinie, Inbox-Läufer
/// und den Watcher. Bewusst ohne jede WPF-API, damit das Verhalten auch ohne Fenster testbar bleibt.
/// </summary>
public sealed class ProjectSyncService : IDisposable
{
    private readonly FileLogger? _logger;
    private readonly Action<string> _log;
    private SyncPolicy _policy;
    private InboxWatcher? _watcher;

    public ProjectSyncService(string projectRoot, AppSettings settings, Action<string> log, FileLogger? logger = null)
    {
        Root = Path.GetFullPath(projectRoot);
        Settings = settings;
        _log = log;
        _logger = logger;
        Processor = new InboxProcessor(Root);
        Processor.EnsureLayout();
        _policy = BuildPolicy();
    }

    public string Root { get; }
    public AppSettings Settings { get; }
    public InboxProcessor Processor { get; }
    public SyncPolicy Policy => _policy;
    public string InboxDirectory => Processor.InboxDirectory;
    public bool IsWatching => _watcher?.IsWatching == true;

    /// <summary>Feuert nach jedem Import-Lauf – mit dem fertigen Berichtspfad für die UI.</summary>
    public event Action<InboxRunResult>? ImportFinished;

    private SyncPolicy BuildPolicy() => new(Root, new SyncPolicyOptions
    {
        ReportOnly = Settings.ReportOnly,
        ConflictResolution = Settings.ConflictResolution,
        KeepBackups = Settings.KeepBackups,
        BackupRunsToKeep = Math.Clamp(Settings.BackupRunsToKeep, 1, 200)
    });

    public void ApplySettings()
    {
        _policy = BuildPolicy();

        // Ein laufender Watcher muss die neue Richtlinie bekommen – sonst bleibt ein Umschalter
        // wie „Nur melden“ wirkungslos, solange er schon läuft.
        if (_watcher is not null)
        {
            _watcher.ActivePolicy = _policy;
        }

        // Der Import-Watcher gehört ausschließlich zur jeweiligen Sitzung.
        var automatisch = Settings.AutoImport;

        if (automatisch && !IsWatching)
        {
            StartWatching();
        }
        else if (!automatisch && IsWatching)
        {
            StopWatching();
        }
    }

    /// <summary>Die Richtlinie, mit der der Watcher gerade arbeitet (für Tests und Statusanzeige).</summary>
    public SyncPolicy? WatcherActivePolicy => _watcher?.ActivePolicy;

    public void StartWatching()
    {
        if (_watcher is not null)
        {
            _watcher.Dispose();
        }

        _watcher = new InboxWatcher(Processor);
        _watcher.ActivePolicy = _policy;
        _watcher.Status += _log;
        _watcher.ImportFinished += run =>
        {
            TryWriteReport(run, "Inbox-Überwachung");
            ImportFinished?.Invoke(run);
        };
        _watcher.Start();
        _log($"Inbox-Überwachung aktiv: {InboxDirectory}");
    }

    public void StopWatching()
    {
        _watcher?.Dispose();
        _watcher = null;
        _log("Inbox-Überwachung gestoppt.");
    }

    /// <summary>
    /// Der Bericht gehört zum Chat, nicht zum Lauf: alle Läufe desselben Chats stehen in einer Datei
    /// (siehe <see cref="ImportReportWriter"/>). Ohne bekannte Chat-Adresse ist es „inbox“.
    /// </summary>
    public string ReportTopic { get; set; } = ImportReportWriter.DefaultTopic;

    /// <summary>Ein Durchlauf über die Inbox (Knopf „Inbox jetzt prüfen“ oder nach einem Download).</summary>
    public async Task<InboxRunResult?> ScanNowAsync(string trigger)
    {
        // Erst der Watcher (dameläuft er, ist sein Lauf schon fertig), sonst direkt –
        // „nichts zu importieren“ melden, während etwas anfängt, wäre verwirrend.
        var run = _watcher is not null
            ? await _watcher.RunOnceAsync(_policy).ConfigureAwait(false)
            : null;
        run ??= await Task.Run(() => Processor.ProcessPending(_policy)).ConfigureAwait(false);

        if (run is not null && !run.IsEmpty)
        {
            if (_policy.IndexWarning is { } warning)
            {
                _log("" + warning);
            }
            TryWriteReport(run, trigger);
            ImportFinished?.Invoke(run);
        }
        else
        {
            _log("Inbox geprüft – nichts zu importieren.");
        }

        return run;
    }

    /// <summary>
    /// Verarbeitet eine Meldung des DOM-Beobachters. Der Bot liefert Rohtext und – wenn er eine
    /// Beschriftung erwischt – einen Namens-Kandidaten. Namensquelle, Pfadprüfung und das
    /// Entfernen der Kopfzeile kommen aus dem Core, damit dasselbe Regelwerk gilt wie beim Zip-Import.
    /// </summary>
    public async Task<string> ImportFromBotAsync(string? fileNameCandidate, string rawContent)
    {
        var hasHeader = CodeBlockParser.TryParse(rawContent, out var parsed, out var parseError);

        // Eine Beschriftung am Blockkopf ("app.py" über dem Code) ist die zuverlässigere Quelle
        // als ein im Text vermuteter Pfad – deshalb zuerst. Ein abgelehnter Kandidat wird nicht
        // stillschweigend durch den nächstbesten ersetzt, sondern gemeldet.
        var (fromCaption, captionError) = ReadCaption(fileNameCandidate);
        if (captionError is not null)
        {
            return "✕ Codeblock abgelehnt: " + captionError;
        }

        var relative = fromCaption ?? (hasHeader ? parsed!.RelativePath : null);

        if (relative is null)
        {
            return "Codeblock ignoriert: " + (parseError ?? "kein Dateiname erkennbar");
        }

        // Inhalt immer ohne Kopf-/Fence-Zeilen, egal woher der Name kam.
        var content = hasHeader ? parsed!.Content : rawContent;

        return await Task.Run(() =>
        {
            var result = _policy.Apply(relative, content, "dom-codeblock");
            var warning = _policy.IndexWarning;
            return (result.Outcome switch
            {
                SyncOutcome.Written => $"\u2714 {relative} neu geschrieben ({result.Bytes} Bytes)",
                SyncOutcome.UpdatedWithBackup => $"\u2714 {relative} aktualisiert" +
                    (result.BackupRelativePath is null ? "" : $" \u00b7 Sicherung: {result.BackupRelativePath}"),
                SyncOutcome.Unchanged => $"\u00b7 {relative} unver\u00e4ndert",
                SyncOutcome.ConflictPreserved => $"\u26a0 Konflikt {relative} \u2192 {result.ConflictRelativePath}",
                SyncOutcome.Planned => $"\U0001F4DD {relative}: {result.Message} (Nur melden)",
                SyncOutcome.Blocked => $"\u26d4 {relative}: {result.Message}",
                SyncOutcome.Failed => $"\u274c {relative}: {result.Message}",
                _ => $"{relative}: {result.Message}"
            }) + (warning is null ? string.Empty : " · " + warning);
        }).ConfigureAwait(false);
    }

    private static (string? Path, string? Error) ReadCaption(string? caption)
    {
        var candidate = PathSafety.CleanCandidate(caption);
        if (candidate is null)
        {
            return (null, null);
        }

        // Beschriftungen aus dem DOM sind oft keine Dateinamen, sondern Befehle oder Sätze
        // („node --check arena-bot.js“, „cmd/c ping -n 121 127.0.0.1“, „Dispatcher.Invoke“).
        // Solche Kandidaten werden still verworfen – dann entscheidet der Blockkopf im Text.
        // Nur ein Kandidat, der die Formfrage besteht, darf zur Zieldatei werden.
        // Ausnahme: Was wie ein Pfad aussieht, wird geprüft und gemeldet – ein Ausbruchsversuch
        // darf nie stillschweigend verschwinden.
        var siehtAusWiePfad = candidate.Contains("..", StringComparison.Ordinal)
                              || candidate.StartsWith('/')
                              || candidate.StartsWith('\\')
                              || (candidate.Length >= 2 && char.IsAsciiLetter(candidate[0]) && candidate[1] == ':');
        if (!siehtAusWiePfad && !PathSafety.LooksLikeFileName(candidate))
        {
            var erstesWort = candidate.Split(' ', '\t')[0];
            if (!PathSafety.LooksLikeFileName(erstesWort))
            {
                return (null, null);
            }
            candidate = erstesWort;
        }

        return PathSafety.TryValidateRelative(candidate, out var validated, out var error)
            ? (validated, null)
            : (null, $"'{candidate}' ist kein erlaubter Pfad – {error}");
    }

    /// <summary>Index-Eintrag nach einem Löschen entfernen (sonst meldet der nächste Import Konflikt).</summary>
    public void Forget(string relativePath) => _policy.Forget(relativePath);

    /// <summary>Einzelne Datei (manuelles Speichern, Drag&amp;Drop) über dasselbe Regelwerk laufen lassen.</summary>
    public SyncResult ImportSingle(string relativePath, string content, string source)
        => _policy.Apply(relativePath, content, source);

    private string? TryWriteReport(InboxRunResult run, string trigger)
    {
        // Ein Lauf, der nur nachgesehen hat (alles unverändert), bekommt keine Berichtsdatei – sonst
        // entsteht aus jedem Takt eine Datei, und die Anzeige dreht ein Karussell.
        if (!run.HatArbeit)
        {
            _log($"Inbox ({trigger}): {run.Summarize()}");
            return null;
        }

        try
        {
            var path = ImportReportWriter.Write(Root, run, trigger, ReportTopic);
            _log($"Bericht: {Path.GetFileName(path)} ({run.Written} neu, {run.Updated} aktualisiert · {trigger})");
            return path;
        }
        catch (Exception ex)
        {
            _logger?.Write("WARN", $"Bericht nicht schreibbar: {ex.Message}");
            return null;
        }
    }

    /// <summary>Erstellt Inbox-Ordnerstruktur (machte bisher der Start des Watchers) – auch für Tests nutzbar.</summary>
    public void EnsureInboxLayoutForTests() => Processor.EnsureLayout();

    public int CountPendingInboxItems()
    {
        try
        {
            return System.IO.Directory.Exists(InboxDirectory)
                ? System.IO.Directory.GetFiles(InboxDirectory).Length
                : 0;
        }
        catch (IOException)
        {
            return -1;
        }
    }

    public void Dispose() => _watcher?.Dispose();
}
