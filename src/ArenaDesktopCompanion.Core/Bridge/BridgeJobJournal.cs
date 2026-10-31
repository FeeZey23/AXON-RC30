using System.Text;
using System.Text.Json;

namespace ArenaDesktopCompanion.Core.Bridge;

/// <summary>Dauerhaftes Ergebnis-/Unterbrechungsgedächtnis. Ergebnisse werden vor dem Senden gesichert.</summary>
public sealed class BridgeJobJournal
{
    private sealed record Entry(string State, string? Result, DateTimeOffset Time);
    private readonly string _path;
    private readonly object _gate = new();
    private Dictionary<string, Entry> _entries;

    public BridgeJobJournal(string path)
    {
        _path = path;
        _entries = File.Exists(path)
            ? JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(path))
                ?? throw new InvalidDataException("Auftragsjournal ist leer oder beschädigt.")
            : new(StringComparer.Ordinal);
    }

    public string? Result(string id)
    { lock (_gate) return _entries.GetValueOrDefault(id)?.Result; }

    public string? State(string id)
    { lock (_gate) return _entries.GetValueOrDefault(id)?.State; }

    public void Begin(string id) => Save(id, new Entry("läuft", null, DateTimeOffset.UtcNow));
    public void Complete(string id, string result) => Save(id, new Entry("abgeschlossen", result, DateTimeOffset.UtcNow));

    public void Reconcile(IReadOnlySet<string> activeJobs)
    {
        lock (_gate)
        {
            var next = new Dictionary<string, Entry>(_entries, StringComparer.Ordinal);
            var changed = false;
            foreach (var entry in next.ToList())
            {
                if (entry.Value.State == "abgeschlossen" && entry.Value.Result is not null && !activeJobs.Contains(entry.Key))
                {
                    next[entry.Key] = entry.Value with { State = "zugestellt", Result = null };
                    changed = true;
                }
            }
            if (!changed) return;
            AtomicProjectFile.ReplaceBytes(_path, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(next)));
            _entries = next;
        }
    }

    private void Save(string id, Entry entry)
    {
        lock (_gate)
        {
            var next = new Dictionary<string, Entry>(_entries, StringComparer.Ordinal) { [id] = entry };
            // Ergebnisse bleiben mindestens 24 Stunden für Übertragungswiederholungen erhalten.
            foreach (var old in next.Where(e => e.Value.Time < DateTimeOffset.UtcNow.AddDays(-2)).ToList()) next.Remove(old.Key);
            if (next.Count > 10000) throw new IOException("Auftragsjournal voll – erst Ergebnisse abholen, keine weiteren Aktionen.");
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(next));
            if (bytes.Length > 16 * 1024 * 1024) throw new IOException("Ergebnispuffer voll (16 MiB) – Übertragung zuerst abschließen.");
            AtomicProjectFile.ReplaceBytes(_path, bytes);
            _entries = next;
        }
    }
}
