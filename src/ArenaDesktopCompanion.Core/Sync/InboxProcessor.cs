using System.IO.Compression;

namespace ArenaDesktopCompanion.Core.Sync;

public sealed record InboxItemResult(string SourceName, SyncResult Result, bool MovedToProcessed);

public sealed record InboxRunResult(IReadOnlyList<InboxItemResult> Items, IReadOnlyList<string> Errors)
{
    public int Processed => Items.Count;
    public int Written => Items.Count(i => i.Result.Outcome is SyncOutcome.Written);
    public int Updated => Items.Count(i => i.Result.Outcome is SyncOutcome.UpdatedWithBackup);
    public int Unchanged => Items.Count(i => i.Result.Outcome is SyncOutcome.Unchanged);
    public int Conflicts => Items.Count(i => i.Result.Outcome is SyncOutcome.ConflictPreserved);
    public int Blocked => Items.Count(i => i.Result.Outcome is SyncOutcome.Blocked);
    public bool IsEmpty => Items.Count == 0 && Errors.Count == 0;

    /// <summary>
    /// Hat dieser Lauf den Projektordner wirklich verändert? Nur dann gehört ein Bericht dazu.
    /// </summary>
    /// <remarks>
    /// Anlass (24.09.2026, live): Ein Archiv mit 2 385 Einträgen wurde im Sekundentakt erneut geprüft –
    /// alles unverändert, aber jeder Lauf schrieb einen Bericht („2385 Dateien verarbeitet, 0 neu").
    /// Ein Lauf, der nur nachsieht und nichts ändert, ist keine Berichtsdatei wert; er steht als eine
    /// Zeile im Protokoll.
    /// </remarks>
    public bool HatArbeit => Written + Updated + Conflicts + Blocked > 0 || Errors.Count > 0;

    public string Summarize()
        => Items.Count == 0
            ? "Inbox leer – nichts zu tun."
            : $"{Processed} {(Processed == 1 ? "Datei" : "Dateien")} geprüft: {Written} neu, {Updated} aktualisiert, " +
              $"{Unchanged} unverändert, {Conflicts} {(Conflicts == 1 ? "Konflikt" : "Konflikte")}, {Blocked} blockiert"
              + (Errors.Count > 0 ? $" · {Errors.Count} {(Errors.Count == 1 ? "Fehler" : "Fehler")}" : string.Empty);
}

/// <summary>
/// Die einzige Stelle, die vom Rechner ins Projekt schreibt. Ablauf pro Datei:
/// warten bis der Schreibvorgang abgeschlossen ist → prüfen → (zip?) importieren → Quelle nach
/// <c>processed</c> wegräumen, damit derselbe Download nicht zweimal zählt.
/// </summary>
public sealed class InboxProcessor
{
    public const string InboxFolderName = SyncPolicy.InboxDirName;
    public const string ProcessedFolderName = "processed";
    public const string FailedFolderName = "failed";

    private readonly string _root;
    private readonly int _readyRetryCount;
    private readonly int _readyDelayMs;

    public InboxProcessor(string projectRoot, int readyRetryCount = 40, int readyDelayMs = 150)
    {
        _root = Path.GetFullPath(projectRoot);
        _readyRetryCount = Math.Max(1, readyRetryCount);
        _readyDelayMs = Math.Max(0, readyDelayMs);
    }

    public string InboxDirectory => Path.Combine(_root, InboxFolderName);
    public string ProcessedDirectory => Path.Combine(InboxDirectory, ProcessedFolderName);
    public string FailedDirectory => Path.Combine(InboxDirectory, FailedFolderName);

    public void EnsureLayout()
    {
        System.IO.Directory.CreateDirectory(InboxDirectory);
        System.IO.Directory.CreateDirectory(ProcessedDirectory);
        System.IO.Directory.CreateDirectory(FailedDirectory);
    }

    private readonly SemaphoreSlim _singleRun = new(1, 1);

    /// <summary>
    /// Alle wartenden Dateien abarbeiten. Ein Aufruf = ein Durchlauf (kein Timer im Kern).
    /// Läuft bereits ein Durchlauf – z. B. der des Watchers, während der Nutzer auf „Prüfen“ klickt –,
    /// kommt sofort ein leerer Ergebnislauf zurück statt einer halbfertigen Doppelverarbeitung.
    /// </summary>
    public InboxRunResult ProcessPending(SyncPolicy policy, CancellationToken cancellationToken = default)
    {
        if (!TryEnterSingleRun())
        {
            // Warten statt überspringen: sonst würde ein Knopfdruck mitten im Watcher-Lauf
            // „nichts zu importieren“ melden, während die Dateien noch unbearbeitet daliegen.
            return new InboxRunResult(Array.Empty<InboxItemResult>(),
                new[] { "Ein anderer Import läuft noch – dieser Durchlauf wurde nicht wiederholt." });
        }
        try
        {
            return ProcessPendingCore(policy, cancellationToken);
        }
        finally
        {
            _singleRun.Release();
        }
    }

    /// <summary>Ein Durchlauf nach dem anderen; wer nicht dran ist, wartet kurz und schaut dann selbst.</summary>
    private bool TryEnterSingleRun() => _singleRun.Wait(TimeSpan.FromSeconds(30));

    private InboxRunResult ProcessPendingCore(SyncPolicy policy, CancellationToken cancellationToken)
    {
        var items = new List<InboxItemResult>();
        var errors = new List<string>();

        if (!System.IO.Directory.Exists(InboxDirectory))
        {
            return new InboxRunResult(items, errors);
        }

        var pending = System.IO.Directory
            .EnumerateFiles(InboxDirectory)
            .Select(f => new FileInfo(f))
            .Where(f => !f.Name.StartsWith('.'))
            .OrderBy(f => f.CreationTimeUtc)
            .ThenBy(f => f.Name, StringComparer.Ordinal)
            .ToList();

        foreach (var file in pending)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (!WaitUntilReadable(file.FullName, cancellationToken))
            {
                errors.Add($"{file.Name}: Datei war nach {_readyRetryCount} Versuchen noch gesperrt – nächster Durchlauf versucht es erneut.");
                continue;
            }

            try
            {
                if (IsArchive(file.Name))
                {
                    foreach (var r in ImportArchive(file, policy))
                    {
                        items.Add(r);
                    }
                }
                else
                {
                    var result = policy.ApplyFromFile(file.Name, file.FullName, "inbox:" + file.Name);
                    items.Add(new InboxItemResult(file.Name, result, MoveAway(file, ProcessedDirectory, keepName: true)));
                }
            }
            catch (Exception ex)
            {
                errors.Add($"{file.Name}: {ex.Message}");
                MoveAway(file, FailedDirectory, keepName: true);
            }
        }

        return new InboxRunResult(items, errors);
    }

    private IEnumerable<InboxItemResult> ImportArchive(FileInfo archive, SyncPolicy policy)
    {
        var results = new List<InboxItemResult>();

        using var zip = ZipFile.OpenRead(archive.FullName);
        var fileEntries = zip.Entries
            .Where(e => !e.FullName.EndsWith('/'))
            .Select(e => (Entry: e, Name: e.FullName.Replace('\\', '/').TrimStart('/')))
            .Where(x => x.Name.Length > 0)
            .ToList();
        var commonRoot = policy.StripCommonArchiveRoot
            ? ArchiveLayout.DetectCommonRoot(fileEntries.Select(x => x.Name), policy.Root)
            : null;

        foreach (var (entry, nameBeforeStrip) in fileEntries)
        {
            var rawName = ArchiveLayout.RemoveRoot(nameBeforeStrip, commonRoot);
            if (WorkspaceFileFilter.ShouldSkip(rawName) || ArchiveLayout.LooksAbsolute(nameBeforeStrip))
            {
                if (ArchiveLayout.LooksAbsolute(nameBeforeStrip))
                {
                    results.Add(new InboxItemResult(nameBeforeStrip,
                        new SyncResult(SyncOutcome.Blocked, nameBeforeStrip, "blockiert: absoluter Pfad im Archiv"),
                        MovedToProcessed: false));
                }
                continue;
            }
            if (!PathSafety.TryResolveInside(policy.Root, rawName, out var relative, out _, out var error))
            {
                results.Add(new InboxItemResult(rawName,
                    new SyncResult(SyncOutcome.Blocked, rawName, $"blockiert: {error}"), MovedToProcessed: false));
                continue;
            }

            using var stream = entry.Open();
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            var result = policy.Apply(relative, buffer.ToArray(), "archive:" + archive.Name);
            results.Add(new InboxItemResult(relative, result, MovedToProcessed: false));
        }

        MoveAway(archive, ProcessedDirectory, keepName: true);
        return results;
    }

    private static bool IsArchive(string fileName)
        => fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

    /// <summary>Der Browser hält die Datei noch, während wir sie lesen wollen: kurz warten, bis sie frei ist.</summary>
    private bool WaitUntilReadable(string path, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < _readyRetryCount; attempt++)
        {
            try
            {
                using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    return true;
                }
            }
            catch (IOException)
            {
                if (_readyDelayMs > 0)
                {
                    cancellationToken.WaitHandle.WaitOne(_readyDelayMs);
                }
            }
            catch (UnauthorizedAccessException)
            {
                if (_readyDelayMs > 0)
                {
                    cancellationToken.WaitHandle.WaitOne(_readyDelayMs);
                }
            }
        }
        return false;
    }

    private bool MoveAway(FileInfo file, string targetDirectory, bool keepName)
    {
        try
        {
            System.IO.Directory.CreateDirectory(targetDirectory);
            var name = keepName
                ? $"{DateTime.Now:yyyyMMdd-HHmmss}-{file.Name}"
                : file.Name;
            var target = Path.Combine(targetDirectory, name);
            File.Move(file.FullName, target, overwrite: true);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
