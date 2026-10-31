namespace ArenaDesktopCompanion.Core.Sync;

/// <summary>
/// Beobachtet die Inbox und löst genau einen Durchlauf pro Ruhephase aus – nicht einen pro
/// Dateievent (sonst erzeugt ein einziger Download drei Neulade-Versuche und das UI flackert).
/// Der Watcher ist optional: <see cref="InboxProcessor.ProcessPending"/> kann auch per Knopf aufgerufen werden.
/// </summary>
public sealed class InboxWatcher : IDisposable
{
    private readonly InboxProcessor _processor;
    private readonly int _debounceMs;
    private FileSystemWatcher? _watcher;
    private DateTime _lastEventUtc = DateTime.MinValue;
    private Timer? _debounceTimer;
    private int _running;

    public InboxWatcher(InboxProcessor processor, int debounceMs = 400)
    {
        _processor = processor;
        _debounceMs = Math.Max(50, debounceMs);
    }

    /// <summary>Das Ergebnis eines Durchlaufs; der Abnehmer (UI) muss selbst auf den UI-Thread marshaln.</summary>
    public event Action<InboxRunResult>? ImportFinished;
    public event Action<string>? Status;

    public bool IsWatching => _watcher is not null;

    public void Start()
    {
        _processor.EnsureLayout();
        Stop();

        _watcher = new FileSystemWatcher(_processor.InboxDirectory)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            IncludeSubdirectories = false,
            InternalBufferSize = 64 * 1024
        };
        _watcher.Created += OnFileSystemEvent;
        _watcher.Changed += OnFileSystemEvent;
        _watcher.Renamed += OnFileSystemEvent;
        _watcher.Deleted += OnFileSystemEvent;
        _watcher.Error += OnWatcherError;
        _watcher.EnableRaisingEvents = true;

        Status?.Invoke($"Inbox-Überwachung startet: {_processor.InboxDirectory}");
        // Alles, was schon drin lag, sofort abarbeiten.
        _ = RunOnceAsync();
    }

    public void Stop()
    {
        _debounceTimer?.Dispose();
        _debounceTimer = null;
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }
    }

    private void OnFileSystemEvent(object sender, FileSystemEventArgs e) => ScheduleRun();

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        Status?.Invoke($"Watcher-Fehler ({e.GetException().Message}) – Überwachung wird neu gestartet.");
        _ = Task.Run(() =>
        {
            try
            {
                Start();
            }
            catch (Exception ex)
            {
                Status?.Invoke($"Watcher-Neustart fehlgeschlagen: {ex.Message}");
            }
        });
    }

    private void ScheduleRun()
    {
        _lastEventUtc = DateTime.UtcNow;
        var timer = _debounceTimer;
        if (timer is null)
        {
            _debounceTimer = new Timer(_ => _ = RunOnceAsync(), null, _debounceMs, Timeout.Infinite);
            return;
        }
        timer.Change(_debounceMs, Timeout.Infinite);
    }

    /// <summary>Führt einen Durchlauf aus, wenn gerade keiner läuft.</summary>
    public Task<InboxRunResult?> RunOnceAsync(SyncPolicy? policy = null, CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _running, 1) == 1)
        {
            return Task.FromResult<InboxRunResult?>(null);
        }

        return Task.Run(() =>
        {
            try
            {
                var activePolicy = policy ?? ActivePolicy;
                if (activePolicy is null)
                {
                    return (InboxRunResult?)null;
                }

                var run = _processor.ProcessPending(activePolicy, cancellationToken);
                if (!run.IsEmpty)
                {
                    ImportFinished?.Invoke(run);
                }

                // Eine Datei kann beim ersten Versuch noch gesperrt gewesen sein (der Browser
                // schreibt gerade noch). Ohne Nachlauf würde sie bis zum nächsten Event liegen bleiben.
                if (HasPendingFiles())
                {
                    ScheduleRun();
                }

                return run;
            }
            catch (Exception ex)
            {
                Status?.Invoke($"Inbox-Lauf fehlgeschlagen: {ex.Message}");
                return (InboxRunResult?)null;
            }
            finally
            {
                Interlocked.Exchange(ref _running, 0);
            }
        }, cancellationToken);
    }

    private bool HasPendingFiles()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(_processor.InboxDirectory))
            {
                if (!Path.GetFileName(file).StartsWith('.'))
                {
                    return true;
                }
            }
        }
        catch (Exception)
        {
            return false;
        }
        return false;
    }

    /// <summary>Vom Host gesetzt, damit der Watcher ohne UI-Referenz auskommt.</summary>
    public SyncPolicy? ActivePolicy { get; set; }

    public void Dispose() => Stop();
}
