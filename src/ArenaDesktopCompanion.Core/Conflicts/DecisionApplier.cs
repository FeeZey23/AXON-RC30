using System.Text.Json;
using System.Text.Json.Serialization;
using ArenaDesktopCompanion.Core.Sync;

namespace ArenaDesktopCompanion.Core.Conflicts;

public sealed class DecisionItem
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("kind")] public string Kind { get; set; } = "conflict";
    [JsonPropertyName("relative")] public string Relative { get; set; } = string.Empty;
    [JsonPropertyName("mine")] public string? Mine { get; set; }
    [JsonPropertyName("arena")] public string? Arena { get; set; }
    [JsonPropertyName("found")] public DateTimeOffset Found { get; set; } = DateTimeOffset.Now;
    [JsonPropertyName("answer")] public string? Answer { get; set; }
}

internal sealed class DecisionFile
{
    [JsonPropertyName("version")] public int Version { get; set; } = 1;
    [JsonPropertyName("hinweis")] public string Hinweis { get; set; } =
        "Antwort eintragen: \"arena\" = Arena-Version übernehmen (meine wird gesichert), " +
        "\"mine\" = meine behalten, \"both\" = meine behalten und Arena-Version zusätzlich als *.arena.<endung>. " +
        "Nach dem Speichern holt die App die Antworten beim nächsten Durchlauf ab.";
    [JsonPropertyName("items")] public List<DecisionItem> Items { get; set; } = new();
}

/// <summary>
/// Anfragen und Antworten für Konflikte. Eine einzige lesbare Datei (<c>.arena-conflicts/pending/decisions.json</c>)
/// ist die Wahrheit – sie funktioniert auch dann, wenn die UI gerade zu ist, und lässt sich
/// mit jedem Editor füllen. Die Inhalte liegen neben ihr als Kopien, damit nichts zurückgekürt wird.
/// </summary>
public static class DecisionApplier
{
    // Nullen bewusst NICHT unterdrücken: "answer": null ist die Einfüllstelle in der Vorlage.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>Legt eine Anfrage an (Kopf- und Nutzerseite als Kopien) und trägt sie in die Liste ein.</summary>
    public static void AddRequest(string root, string relative, string mineContent, string arenaContent,
                                  string mineRelative, string arenaRelative)
    {
        var id = Path.GetFileNameWithoutExtension(mineRelative);
        Write(root, mineRelative, mineContent);
        Write(root, arenaRelative, arenaContent);

        var items = ReadItems(root);
        items.RemoveAll(i => string.Equals(i.Relative, relative, StringComparison.Ordinal)
                             || string.Equals(i.Id, id, StringComparison.Ordinal));
        items.Add(new DecisionItem
        {
            Id = id,
            Kind = "conflict",
            Relative = relative,
            Mine = mineRelative,
            Arena = arenaRelative,
            Found = DateTimeOffset.Now,
            Answer = null
        });
        Save(root, items);
    }

    /// <summary>Bringt die Vorlage auf Stand: erzeugt sie, wenn etwas offen ist, löscht sie, wenn nicht.</summary>
    public static string? WriteTemplate(string root, Action<string>? log = null)
    {
        try
        {
            var items = ReadItems(root).Where(i => string.IsNullOrWhiteSpace(i.Answer)).ToList();
            var template = ConflictPolicy.TemplatePath(root);

            if (items.Count == 0)
            {
                TryDelete(template);
                return null;
            }

            Save(root, items);
            log?.Invoke($"{items.Count} Konflikt(e) warten auf eine Entscheidung: {Relative(root, template)}");
            return template;
        }
        catch (Exception ex)
        {
            log?.Invoke("Entscheidungsdatei nicht schreibbar: " + ex.Message);
            return null;
        }
    }

    /// <summary>Antwort für eine Anfrage eintragen (Benutzer oder Dialog). False, wenn die Id unbekannt ist.</summary>
    public static bool SetAnswer(string root, string id, string answer)
    {
        try
        {
            var items = ReadItems(root);
            var item = items.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.Ordinal));
            if (item is null)
            {
                return false;
            }

            item.Answer = answer.Trim().ToLowerInvariant();
            Save(root, items);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Zieht beantwortete Anfragen ab. Gibt die Anzahl umgesetzter Entscheidungen zurück.</summary>
    public static int ApplyPending(string root, SyncPolicy policy, Action<string>? log = null)
    {
        var template = ConflictPolicy.TemplatePath(root);
        if (!File.Exists(template))
        {
            return 0;
        }

        List<DecisionItem> items;
        try
        {
            items = ReadItems(root);
        }
        catch (Exception ex)
        {
            log?.Invoke("Entscheidungsdatei nicht lesbar – nichts geändert: " + ex.Message);
            return 0;
        }

        var applied = 0;
        var remaining = new List<DecisionItem>();

        foreach (var item in items)
        {
            var answer = item.Answer?.Trim().ToLowerInvariant();
            if (answer is not ("arena" or "mine" or "both"))
            {
                remaining.Add(item);
                continue;
            }

            if (!PathSafety.TryValidateRelative(item.Relative, out var relative, out var pathError))
            {
                log?.Invoke($"Entscheidung verworfen ({item.Relative}): {pathError}");
                CleanCopies(root, item);
                continue;
            }

            var arenaText = ReadIfExists(root, item.Arena) ?? string.Empty;
            try
            {
                switch (answer)
                {
                    case "arena":
                        policy.Apply(relative, arenaText, "entscheidung", ConflictResolution.ArenaWins);
                        log?.Invoke($"Entscheidung „arena“: {relative} übernommen");
                        break;
                    case "both":
                        var bothTarget = $"{SyncPolicy.ConflictDirName}/{ConflictPolicy.ArenaCopyName(relative)}";
                        policy.ApplyUnfiltered(bothTarget, arenaText, "entscheidung");
                        log?.Invoke($"Entscheidung „both“: {bothTarget} angelegt, deine {relative} bleibt unangetastet");
                        break;
                    default:
                        log?.Invoke($"Entscheidung „mine“: {relative} behalten");
                        break;
                }
                applied++;
                CleanCopies(root, item);
            }
            catch (Exception ex)
            {
                log?.Invoke($"Entscheidung für {relative} fehlgeschlagen: {ex.Message}");
                remaining.Add(item);
            }
        }

        try
        {
            if (remaining.Count == 0)
            {
                TryDelete(template);
                PruneEmpty(root);
            }
            else
            {
                Save(root, remaining);
            }
        }
        catch (Exception ex)
        {
            log?.Invoke("Entscheidungsdatei nicht aktualisierbar: " + ex.Message);
        }

        return applied;
    }

    public static IReadOnlyList<DecisionItem> ReadPending(string root)
    {
        try
        {
            return ReadItems(root);
        }
        catch (Exception)
        {
            return Array.Empty<DecisionItem>();
        }
    }

    private static List<DecisionItem> ReadItems(string root)
    {
        var template = ConflictPolicy.TemplatePath(root);
        if (!File.Exists(template))
        {
            return new List<DecisionItem>();
        }

        var text = File.ReadAllText(template);
        if (string.IsNullOrWhiteSpace(text))
        {
            return new List<DecisionItem>();
        }

        var trimmed = text.TrimStart();
        if (trimmed.StartsWith('['))
        {
            // handschriftlich: nacktes Array statt { "items": [...] }
            return JsonSerializer.Deserialize<List<DecisionItem>>(text, JsonOptions) ?? new List<DecisionItem>();
        }

        if (trimmed.StartsWith('{') && !text.Contains("\"items\"", StringComparison.Ordinal))
        {
            // handschriftlich: einzelnes Objekt
            var single = JsonSerializer.Deserialize<DecisionItem>(text, JsonOptions);
            return single is null ? new List<DecisionItem>() : new List<DecisionItem> { single };
        }

        var parsed = JsonSerializer.Deserialize<DecisionFile>(text, JsonOptions)
                     ?? throw new InvalidDataException("decisions.json ohne Inhalt.");
        return parsed.Items ?? new List<DecisionItem>();
    }

    private static void Save(string root, List<DecisionItem> items)
    {
        System.IO.Directory.CreateDirectory(ConflictPolicy.PendingDirectory(root));
        var temp = ConflictPolicy.TemplatePath(root) + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(new DecisionFile { Items = items }, JsonOptions));
        File.Move(temp, ConflictPolicy.TemplatePath(root), overwrite: true);
    }

    private static void Write(string root, string relative, string content)
    {
        var full = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private static void CleanCopies(string root, DecisionItem item)
    {
        TryDelete(Path.Combine(root, item.Mine?.Replace('/', Path.DirectorySeparatorChar) ?? string.Empty));
        TryDelete(Path.Combine(root, item.Arena?.Replace('/', Path.DirectorySeparatorChar) ?? string.Empty));
    }

    private static void PruneEmpty(string root)
    {
        TryDeleteDirectory(ConflictPolicy.FilesDirectory(root));
        TryDeleteDirectory(ConflictPolicy.PendingDirectory(root));
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (System.IO.Directory.Exists(path) && System.IO.Directory.GetFileSystemEntries(path).Length == 0)
            {
                System.IO.Directory.Delete(path);
            }
        }
        catch (IOException) { /* nur Aufräumen */ }
        catch (UnauthorizedAccessException) { /* nur Aufräumen */ }
    }

    private static string? ReadIfExists(string root, string? relative)
    {
        if (string.IsNullOrWhiteSpace(relative))
        {
            return null;
        }
        var full = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(full) ? File.ReadAllText(full) : null;
    }

    private static string Relative(string root, string full)
        => Path.GetRelativePath(root, full).Replace('\\', '/');

    private static void TryDelete(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException) { /* egal */ }
        catch (UnauthorizedAccessException) { /* egal */ }
    }
}
