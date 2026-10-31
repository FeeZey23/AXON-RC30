using System.Text;

namespace ArenaDesktopCompanion.Core.Diagnostics;

/// <summary>Ein Logeintrag mit Zeitstempel – die UI malt daraus ihre Zeile.</summary>
public readonly record struct LogEntry(DateTimeOffset Time, string Level, string Message)
{
    public string ToDisplayLine() => $"[{Time.ToLocalTime():HH:mm:ss}] {Message}";
}

/// <summary>
/// Ringpuffer für die Live-Log-Anzeige im Fenster: begrenzt (Default 500 Zeilen), damit das
/// UI nach Stunden nicht zähe wird (Punkt P2-1 aus dem Fehlerbericht).
/// </summary>
public sealed class LogRing
{
    private readonly Queue<LogEntry> _items;
    private readonly object _gate = new();
    private LogEntry? _last;

    public LogRing(int capacity = 500)
    {
        Capacity = Math.Max(10, capacity);
        _items = new Queue<LogEntry>(Capacity);
    }

    public int Capacity { get; }

    public event Action<LogEntry>? EntryAdded;

    public int Count { get { lock (_gate) return _items.Count; } }

    /// <summary>Der eben eingefügte Eintrag – die UI hängt genau diese eine Zeile an.</summary>
    public LogEntry? Last()
    {
        lock (_gate) return _last;
    }

    public void Add(string level, string message)
    {
        var entry = new LogEntry(DateTimeOffset.Now, level, message);
        lock (_gate)
        {
            _last = entry;
            _items.Enqueue(entry);
            while (_items.Count > Capacity)
            {
                _items.Dequeue();
            }
        }
        EntryAdded?.Invoke(entry);
    }

    public IReadOnlyList<LogEntry> Snapshot()
    {
        lock (_gate) return _items.ToArray();
    }

    public string ToText()
    {
        var sb = new StringBuilder();
        foreach (var entry in Snapshot())
        {
            sb.Append(entry.ToDisplayLine()).Append('\n');
        }
        return sb.ToString();
    }
}

/// <summary>
/// Einfaches rollierendes Datei-Protokoll (5 MB, 3 Dateien). Parallele Aufrufe werden
/// über ein Lock serialisiert; ein Schreibfehler darf die App nie zum Absturz bringen.
/// </summary>
public sealed class FileLogger : IDisposable
{
    private readonly object _gate = new();
    private readonly int _maxBytes;
    private readonly int _keepFiles;
    private bool _disposed;

    public FileLogger(string directoryPath, string fileName = "companion.log", int maxBytes = 5_000_000, int keepFiles = 3)
    {
        DirectoryPath = directoryPath;
        FilePath = Path.Combine(directoryPath, fileName);
        _maxBytes = Math.Max(64 * 1024, maxBytes);
        _keepFiles = Math.Max(1, keepFiles);
    }

    public string DirectoryPath { get; }
    public string FilePath { get; }

    /// <summary>Mehrzeiligen Text unverändert anhängen (Absturzberichte, Abschnitte).</summary>
    public void WriteRaw(string text)
    {
        if (_disposed)
        {
            return;
        }
        try
        {
            lock (_gate)
            {
                System.IO.Directory.CreateDirectory(DirectoryPath);
                File.AppendAllText(FilePath, text + Environment.NewLine, new UTF8Encoding(false));
            }
        }
        catch (Exception)
        {
            // Loggen darf niemals der Grund sein, warum etwas abbricht.
        }
    }

    public void Write(string level, string message)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            lock (_gate)
            {
                System.IO.Directory.CreateDirectory(DirectoryPath);
                RotateIfNeeded();
                File.AppendAllText(FilePath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}",
                    new UTF8Encoding(false));
            }
        }
        catch (Exception)
        {
            // Loggen darf niemals der Grund sein, warum ein Sync abbricht.
        }
    }

    private void RotateIfNeeded()
    {
        var info = new FileInfo(FilePath);
        if (!info.Exists || info.Length < _maxBytes)
        {
            return;
        }

        var oldest = $"{FilePath}.{_keepFiles}";
        if (File.Exists(oldest))
        {
            File.Delete(oldest);
        }
        for (var i = _keepFiles - 1; i >= 1; i--)
        {
            var from = $"{FilePath}.{i}";
            if (File.Exists(from))
            {
                File.Move(from, $"{FilePath}.{i + 1}", overwrite: true);
            }
        }
        File.Move(FilePath, FilePath + ".1", overwrite: true);
    }

    public void Dispose() => _disposed = true;
}
