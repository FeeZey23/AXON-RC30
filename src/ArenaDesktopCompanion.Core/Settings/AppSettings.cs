using System.Text.Json;
using System.Text.Json.Serialization;
using ArenaDesktopCompanion.Core.Bridge;
using ArenaDesktopCompanion.Core.Conflicts;

namespace ArenaDesktopCompanion.Core.Settings;

public sealed class AppSettings : IJsonOnDeserialized
{
    [JsonExtensionData] public Dictionary<string,JsonElement>? Extra { get; set; }
    void IJsonOnDeserialized.OnDeserialized(){if(Extra is not null)foreach(var key in Extra.Keys.Where(k=>k.Equals("notAus",StringComparison.OrdinalIgnoreCase)).ToArray())Extra.Remove(key);}
    [JsonPropertyName("version")] public int Version { get; set; } = 1;
    [JsonPropertyName("projectFolder")] public string? ProjectFolder { get; set; }
    /// <summary>Import aus der Inbox automatisch in den Projektordner übernehmen (ADR 002).</summary>
    [JsonPropertyName("autoImport")] public bool AutoImport { get; set; } = true;
    /// <summary>„Nur melden“: planen und protokollieren, aber nichts schreiben (TODO 2.12).</summary>
    [JsonPropertyName("reportOnly")] public bool ReportOnly { get; set; }
    [JsonPropertyName("keepBackups")] public bool KeepBackups { get; set; } = true;
    [JsonPropertyName("backupRunsToKeep")] public int BackupRunsToKeep { get; set; } = 20;
    /// <summary>Was bei abweichender lokaler Datei passieren soll (Standard: nachfragen).</summary>
    [JsonPropertyName("conflictResolution")] public int ConflictResolutionMode { get; set; }

    [JsonIgnore]
    public ConflictResolution ConflictResolution
    {
        get => Enum.IsDefined(typeof(ConflictResolution), ConflictResolutionMode) ? (ConflictResolution)ConflictResolutionMode : ConflictResolution.Ask;
        set => ConflictResolutionMode = (int)value;
    }

    /// <summary>Experimenteller Codeblock-Fanger im DOM – bewusst Standard aus (ADR 001).</summary>
    [JsonPropertyName("codeBlockBotEnabled")] public bool CodeBlockBotEnabled { get; set; }
    /// <summary>Downloads von arena.ai in die Inbox umleiten.</summary>
    [JsonPropertyName("routeDownloadsToInbox")] public bool RouteDownloadsToInbox { get; set; }
    [JsonPropertyName("lastWorkspaceFetchUtc")] public DateTimeOffset? LastWorkspaceFetchUtc { get; set; }
    /// <summary>Der "git init"-Hinweis wurde vom Nutzer bewusst ausgeblendet (Punkt 7.4).</summary>
    [JsonPropertyName("gitHintDismissed")] public bool GitHintDismissed { get; set; }

    // ---- Brücke zum Assistenten (Phase 8) ----------------------------------------------------
    // Der Companion legt Abbilder in einen Briefkasten im Netz und holt dort Aufträge ab.
    // Jeder Reiter hat eigenes Paar; Kenntnis des Namens allein ist keine Berechtigung.

    /// <summary>Name des Briefkastens (geteiltes Geheimnis, vom Companion erzeugt).</summary>
    [JsonPropertyName("bridgeBin")] public string? BridgeBin { get; set; }

    /// <summary>
    /// Zweiter Faktor: ohne dieses Kennwort führt der Companion keinen Auftrag aus. Liegt in
    /// <c>%LOCALAPPDATA%\AXON\RC30\settings.json</c> – also außerhalb des Projektordners.
    /// </summary>
    [JsonPropertyName("bridgeToken")] public string? BridgeToken { get; set; }

    /// <summary>Abstand zwischen zwei Takten in Sekunden (3–120).</summary>
    [JsonPropertyName("bridgeIntervalSeconds")] public int BridgeIntervalSeconds { get; set; } = 3;

    /// <summary>
    /// Die Reiter (Arbeitsbereiche): je einer mit eigenem Projektordner und eigenem Brücken-Paar.
    /// Die flachen Felder oben sind immer der **aktive** Reiter; beim Wechseln wird gesichert und
    /// geladen (siehe <see cref="InArbeitsbereichSpeichern"/>).
    /// </summary>
    [JsonPropertyName("workspaces")] public List<Arbeitsbereich> Arbeitsbereiche { get; set; } = [];

    /// <summary>Welcher Reiter gerade offen ist.</summary>
    [JsonPropertyName("aktiverWorkspace")] public int AktiverArbeitsbereich { get; set; }

    public List<ClosedWorkspace> ClosedWorkspaces { get; set; } = [];
    public List<string> PendingTabCleanup { get; set; } = [];
    public int Rc30PreferencesVersion { get; set; }

    public void ApplyRc30Preferences()
    {
        TabHistory.Normalize(this);
        if(Rc30PreferencesVersion>=3)return;
        RouteDownloadsToInbox=false;
        foreach(var t in Arbeitsbereiche.Concat(ClosedWorkspaces.Select(c=>c.Tab)))t.RouteDownloads=false;
        Rc30PreferencesVersion=3;
    }

    // ---- Reiter (Arbeitsbereiche) -------------------------------------------------------------

    /// <summary>
    /// Sorgt dafür, dass es mindestens einen Reiter gibt – und baut ihn beim ersten Start aus den
    /// bisherigen Feldern. So merkt der Bestandsnutzer vom Umbau nichts: sein Ordner und sein Paar
    /// liegen danach in Reiter 1.
    /// </summary>
    public void ArbeitsbereicheSicherstellen()
    {
        if (Arbeitsbereiche.Count == 0)
        {
            var vorhanden = new Arbeitsbereich();
            InArbeitsbereichSpeichernIn(vorhanden);
            Arbeitsbereiche.Add(vorhanden);
            AktiverArbeitsbereich = 0;
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < Arbeitsbereiche.Count; i++)
        {
            if (!Guid.TryParseExact(Arbeitsbereiche[i].Id, "N", out _) || !ids.Add(Arbeitsbereiche[i].Id))
            { Arbeitsbereiche[i].Id = Guid.NewGuid().ToString("N"); ids.Add(Arbeitsbereiche[i].Id); }
            var name = Arbeitsbereiche[i].Name;
            if (string.IsNullOrWhiteSpace(name) || name.Trim() == "Chat")
            {
                Arbeitsbereiche[i].Name = i == 0 ? "Chat 1" : $"Chat {i + 1}";
            }
        }

        AktiverArbeitsbereich = Math.Clamp(AktiverArbeitsbereich, 0, Arbeitsbereiche.Count - 1);
    }

    /// <summary>Den aktiven Reiter mit den flachen Feldern überschreiben (vor dem Wechseln/Beenden).</summary>
    public void InArbeitsbereichSpeichern() => InArbeitsbereichSpeichern(AktiverArbeitsbereich);

    /// <summary>Die flachen Felder in den Reiter Nummer <paramref name="index"/> schreiben.</summary>
    public void InArbeitsbereichSpeichern(int index)
    {
        if (index < 0 || index >= Arbeitsbereiche.Count)
        {
            return;
        }

        InArbeitsbereichSpeichernIn(Arbeitsbereiche[index]);
    }

    private void InArbeitsbereichSpeichernIn(Arbeitsbereich ziel)
    {
        ziel.ProjectFolder = ProjectFolder;
        ziel.BridgeBin = BridgeBin;
        ziel.BridgeToken = BridgeToken;
    }

    /// <summary>Die flachen Felder aus dem Reiter Nummer <paramref name="index"/> füllen.</summary>
    public void AusArbeitsbereichLaden(int index)
    {
        if (index < 0 || index >= Arbeitsbereiche.Count)
        {
            return;
        }

        var quelle = Arbeitsbereiche[index];
        ProjectFolder = quelle.ProjectFolder;
        BridgeBin = quelle.BridgeBin;
        BridgeToken = quelle.BridgeToken;
        AktiverArbeitsbereich = index;
    }

    /// <summary>
    /// Einen frischen Reiter anhängen. Wunsch vom 26.09.2026: Der neue Reiter ist **sofort
    /// verbindungsfähig** – sein Paar (Briefkasten + Kennwort) entsteht hier, und die Brücke läuft
    /// ohne Zutun. Nur der Projektordner bleibt leer; er wird beim Einrichten gewählt, und damit ist
    /// sichtbar, dass dieser Reiter noch kein Projekt hat.
    /// </summary>
    public int ArbeitsbereichAnlegen(string? name = null)
    {
        var nummer = Arbeitsbereiche.Count + 1;
        Arbeitsbereiche.Add(new Arbeitsbereich
        {
            Name = string.IsNullOrWhiteSpace(name) ? $"Chat {nummer}" : name!.Trim(),
            BridgeBin = RelayClient.NeuerName(),
            BridgeToken = RelayClient.NeuesKennwort(),
            BridgeEncrypted = true,
        });
        return Arbeitsbereiche.Count - 1;
    }

    /// <summary>
    /// Einen Reiter schließen. Der letzte bleibt immer stehen – ein Fenster ohne Reiter hätte
    /// keinen Ordner und keine Verbindung mehr. Rückgabe: der Reiter, der danach aktiv sein soll.
    /// </summary>
    public int ArbeitsbereichEntfernen(int index)
    {
        if (Arbeitsbereiche.Count <= 1 || index < 0 || index >= Arbeitsbereiche.Count)
        {
            return AktiverArbeitsbereich;
        }

        Arbeitsbereiche.RemoveAt(index);

        var neuerAktiver = Math.Clamp(
            AktiverArbeitsbereich >= index ? AktiverArbeitsbereich - 1 : AktiverArbeitsbereich,
            0,
            Arbeitsbereiche.Count - 1);
        AktiverArbeitsbereich = neuerAktiver;
        return neuerAktiver;
    }

    /// <summary>Einen Reiter umbenennen (leer oder zu lang wird abgewiesen).</summary>
    public bool ArbeitsbereichUmbenennen(int index, string? name)
    {
        var sauber = (name ?? string.Empty).Trim();
        if (index < 0 || index >= Arbeitsbereiche.Count || sauber.Length == 0 || sauber.Length > 24)
        {
            return false;
        }

        Arbeitsbereiche[index].Name = sauber;
        return true;
    }
}

/// <summary>
/// Einstellungen liegen unter <c>%LOCALAPPDATA%\AXON\RC30\settings.json</c>, damit sie
/// weder im Projektordner noch im Build-Ausgabeordner landen (Punkt P1 aus dem Audit).
/// Eine kaputte Datei wird nie zum Absturz: es gelten dann die Defaults, die alte Datei bleibt
/// als <c>settings.json.corrupt-&lt;zeitstempel&gt;</c> erhalten.
/// </summary>
public sealed class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public AppSettingsStore(string? directory = null)
    {
        Directory = directory ?? DefaultDirectory;
        FilePath = Path.Combine(Directory, "settings.json");
    }

    public string Directory { get; }
    public string FilePath { get; }
    public bool LastLoadWasCorrupt { get; private set; }
    public bool RestoredLastGood { get; private set; }
    public string? RecoveryNotice { get; private set; }
    public string LastGoodPath=>FilePath+".lastgood";
    public string ForwardPath=>Path.Combine(Directory,"settings.forward.json");

    public static string DefaultDirectory => Rc30Paths.DataRoot;

    private AppSettings Decode(string text)
    {
        var node=SettingsSafety.Parse(text);
        if(File.Exists(ForwardPath)){try{SettingsSafety.MergeMissing(node,SettingsSafety.Parse(File.ReadAllText(ForwardPath)));}catch(Exception ex)when(ex is JsonException or InvalidDataException or IOException or UnauthorizedAccessException){RecoveryNotice="Vorwärts-Sicherung nicht lesbar; Original bleibt erhalten.";}}
        return JsonSerializer.Deserialize<AppSettings>(node.ToJsonString(),JsonOptions)??throw new InvalidDataException("Einstellungen sind leer.");
    }
    public AppSettings Load()
    {
        LastLoadWasCorrupt=false;RestoredLastGood=false;RecoveryNotice=null;
        if(!File.Exists(FilePath))
        {
            if(File.Exists(LastGoodPath))
            {
                try{var restored=Decode(File.ReadAllText(LastGoodPath));RestoredLastGood=true;RecoveryNotice="Einstellungsdatei fehlt; letzte gültige Konfiguration wiederhergestellt.";return restored;}
                catch(Exception backup)when(backup is JsonException or IOException or UnauthorizedAccessException or ArgumentException){RecoveryNotice="Einstellungsdatei fehlt und Sicherung ist ungültig. Original-Sicherung bleibt erhalten; leere Einrichtung.";}
            }
            return new AppSettings();
        }
        try{return Decode(File.ReadAllText(FilePath));}
        catch(Exception ex)when(ex is JsonException or InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            LastLoadWasCorrupt=true;TryQuarantine();
            try{if(File.Exists(LastGoodPath)){var restored=Decode(File.ReadAllText(LastGoodPath));RestoredLastGood=true;RecoveryNotice="Beschädigte Einstellungen gesichert; letzte gültige Konfiguration wiederhergestellt.";return restored;}}
            catch(Exception backup)when(backup is JsonException or InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException){}
            RecoveryNotice="Einstellungen beschädigt und gesichert. Keine gültige Sicherung gefunden; leere Einrichtung, Originaldaten nicht gelöscht.";return new AppSettings();
        }
    }
    public void Save(AppSettings settings)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            var bytes=System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(settings,JsonOptions));SettingsSafety.Parse(System.Text.Encoding.UTF8.GetString(bytes));
            AtomicProjectFile.ReplaceBytes(FilePath,bytes);AtomicProjectFile.ReplaceBytes(LastGoodPath,bytes);
        }
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){throw new IOException("RC30-Einstellungen konnten nicht gespeichert werden.",ex);}
    }

    private void TryQuarantine()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                File.Move(FilePath, $"{FilePath}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss-fffffff}", overwrite: true);
            }
        }
        catch (IOException) { /* egal */ }
        catch (UnauthorizedAccessException) { /* egal */ }
    }

}