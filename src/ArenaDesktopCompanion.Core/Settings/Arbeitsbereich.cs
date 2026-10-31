using System.Text.Json.Serialization;

namespace ArenaDesktopCompanion.Core.Settings;

/// <summary>
/// Ein Arbeitsbereich – das, was der Nutzer als Reiter (Tab) sieht: ein eigener Chat mit eigenem
/// Projektordner und eigenem Brücken-Paar.
/// </summary>
/// <remarks>
/// <para>
/// Wunsch vom 24.09.2026: „Möchte zwischen mehreren Chats und Projekten wechseln können … zweiten
/// Tab im Companion öffnen mit anderem Chat und anderem Projektordner." Entschieden wurde dabei:
/// **jeder Arbeitsbereich hat sein eigenes Paar** (Briefkasten + Kennwort), damit ein zweiter
/// Arena-Chat sein eigenes Projekt steuern kann, während der erste weiterläuft.
/// </para>
/// <para>
/// RC30: stabile Sitzung mit eigenem Browserprofil, Projekt, Connector und Panel-Layout.
/// Browserprofile sind keine Betriebssystem-Sandbox. Lebenszyklus-Abbruch bleibt pro Sitzung.
/// </para>
/// </remarks>
public sealed class Arbeitsbereich
{
    [JsonExtensionData] public Dictionary<string,System.Text.Json.JsonElement>? Extra { get; set; }
    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [JsonPropertyName("group")] public string Group { get; set; } = "";
    [JsonPropertyName("panels")] public List<PanelLayout> Panels { get; set; } = [];
    [JsonPropertyName("connectionId")] public string ConnectionId { get; set; } = "";
    [JsonPropertyName("sideWidth")] public double SideWidth { get; set; } = 360;

    public string ConnectedChatAddress { get; set; } = "";
    public bool AutoImport { get; set; } = true;
    public bool RouteDownloads { get; set; }
    public bool AutoChatName { get; set; } = true;
    public string ChatAddress { get; set; } = "";
    public int BackupCount { get; set; } = 20;

    [JsonPropertyName("name")] public string Name { get; set; } = "Chat";

    [JsonPropertyName("projectFolder")] public string? ProjectFolder { get; set; }

    /// <summary>Briefkasten dieses Reiters (filebin-Name).</summary>
    [JsonPropertyName("bridgeBin")] public string? BridgeBin { get; set; }

    /// <summary>Kennwort dieses Reiters.</summary>
    [JsonPropertyName("bridgeToken")] public string? BridgeToken { get; set; }

    [JsonPropertyName("bridgeEncrypted")] public bool BridgeEncrypted { get; set; }

    /// <summary>Der zuletzt geöffnete Chat (Adresse) – beim nächsten Wechsel steht er wieder da.</summary>
    [JsonPropertyName("letzteAdresse")] public string? LetzteAdresse { get; set; }

    /// <summary>Was auf dem Reiter steht: der eigene Name, sonst der Ordnername, sonst „Chat“.</summary>
    public string Beschriftung()
    {
        if (!string.IsNullOrWhiteSpace(Name))
        {
            return Name.Trim();
        }

        if (!string.IsNullOrWhiteSpace(ProjectFolder))
        {
            var ordner = ProjectFolder!.TrimEnd('\\', '/');
            var letzter = ordner[(ordner.LastIndexOfAny(['\\', '/']) + 1)..];
            if (!string.IsNullOrWhiteSpace(letzter))
            {
                return letzter;
            }
        }

        return "Chat";
    }

    /// <summary>Ist für diesen Reiter eine Verbindung eingetragen?</summary>
    public bool Verbunden => !string.IsNullOrWhiteSpace(BridgeBin) && !string.IsNullOrWhiteSpace(BridgeToken);
}

public sealed class PanelLayout
{
    public string Kind { get; set; } = "";
    public double Height { get; set; } = 300;
    public bool Detached { get; set; }
    public double Width { get; set; } = 460;
    public double Left { get; set; } = 100;
    public double Top { get; set; } = 100;
}
