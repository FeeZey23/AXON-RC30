using System.Text.Json;
using System.Text.Json.Serialization;

namespace ArenaDesktopCompanion.Core.Sync;

public sealed class SyncIndexEntry
{
    [JsonPropertyName("sha256")] public string Sha256 { get; set; } = string.Empty;
    [JsonPropertyName("size")] public long Size { get; set; }
    [JsonPropertyName("source")] public string Source { get; set; } = string.Empty;
    [JsonPropertyName("writtenUtc")] public DateTimeOffset WrittenUtc { get; set; }
}

/// <summary>
/// Gedächtnis des Syncs: welcher Inhalt wurde zuletzt bewusst geschrieben?
/// Nur damit ist zu unterscheiden zwischen „Arena hat die Datei geändert“
/// und „der Nutzer hat die Datei geändert“ (Konfliktfall, ADR 002).
/// </summary>
public sealed class SyncIndex
{
    public const string FileName = ".arena-sync-index.json";

    private readonly Dictionary<string, SyncIndexEntry> _entries;

    public SyncIndex() => _entries = new Dictionary<string, SyncIndexEntry>(StringComparer.Ordinal);

    /// <summary>Reines Speicherformat. Absichtlich ein separates DTO: eine <c>IReadOnlyDictionary</c>-
    /// Eigenschaft bindet beim Laden nicht an einen <c>Dictionary</c>-Konstruktorparameter
    /// (werfbarer Fehler von System.Text.Json, hier zuerst aufgetreten).</summary>
    private sealed class SyncIndexDto
    {
        public Dictionary<string, SyncIndexEntry> Entries { get; set; } = new();
    }

    public int Count => _entries.Count;
    public IReadOnlyDictionary<string, SyncIndexEntry> Entries => _entries;
    internal static string ToJson(SyncIndex index) => JsonSerializer.Serialize(new SyncIndexDto { Entries = new(index._entries) }, JsonOptions);
    internal static SyncIndex FromJson(string json)
    {
        var dto = JsonSerializer.Deserialize<SyncIndexDto>(json, JsonOptions) ?? new SyncIndexDto();
        var index = new SyncIndex();
        foreach (var (key, value) in dto.Entries)
        {
            index._entries[key] = value;
        }
        return index;
    }
    public bool WasCorrupt { get; private set; }

    public bool TryGet(string relativePath, out SyncIndexEntry entry) => _entries.TryGetValue(relativePath, out entry!);

    public void Set(string relativePath, string sha256, long size, string source)
        => _entries[relativePath] = new SyncIndexEntry
        {
            Sha256 = sha256,
            Size = size,
            Source = source,
            WrittenUtc = DateTimeOffset.UtcNow
        };

    public void Remove(string relativePath) => _entries.Remove(relativePath);

    public static SyncIndex Load(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return new SyncIndex();
            }

            var json = File.ReadAllText(filePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new SyncIndex { WasCorrupt = true };
            }

            return FromJson(json);
        }
        catch (Exception)
        {
            // Defekter Index darf den Import nicht verhindern: neu anfangen, aber sichtbar melden.
            return new SyncIndex { WasCorrupt = true };
        }
    }

    /// <summary>
    /// Atomar schreiben (Temp + Move), damit ein Abbruch nie einen halben Index hinterlässt.
    /// Ein Scheitern bricht den Import nicht ab: die Nutzdatei ist dann bereits geschrieben, und
    /// ein schreibgeschützter Projektordner soll nicht als Importfehler erscheinen.
    /// </summary>
    /// <returns>true, wenn der Index geschrieben wurde.</returns>
    public bool Save(string filePath)
    {
        try
        {
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var temp = filePath + ".tmp";
            File.WriteAllText(temp, ToJson(this));
            File.Move(temp, filePath, overwrite: true);
            LastError = null;
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return false;
        }
    }

    /// <summary>Letzter Schreibfehler des Index (fürs Protokoll); null, wenn alles glatt lief.</summary>
    public string? LastError { get; private set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
