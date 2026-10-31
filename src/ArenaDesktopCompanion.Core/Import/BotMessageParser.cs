using System.Text.Json;

namespace ArenaDesktopCompanion.Core.Import;

public sealed record BotMessage(string Action, string FileName, string Content, long SizeBytes)
{
    public const string SyncFileAction = "SYNC_FILE";
}

/// <summary>
/// Übersetzt die JSON-Nachricht, die das injizierte Seitenskript an die App schickt.
/// Harte Grenze nach oben: Größe, Feldtyp, fehlende Felder – nichts davon darf eine
/// Ausnahme in den WebView-Eventkanal tragen.
/// </summary>
public static class BotMessageParser
{
    public const long MaxContentBytes = 5 * 1024 * 1024;

    public static bool TryParse(string? json, out BotMessage? message, out string? error, long maxContentBytes = MaxContentBytes)
    {
        message = null;
        error = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            error = "Leere Bot-Nachricht.";
            return false;
        }
        if (json.Length > maxContentBytes * 2)
        {
            error = $"Nachricht deutlich größer als das Content-Limit ({maxContentBytes} Bytes) – verworfen.";
            return false;
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            error = $"Kein gültiges JSON: {ex.Message}";
            return false;
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = $"JSON-Wurzel ist {doc.RootElement.ValueKind}, erwartet Object.";
                return false;
            }

            if (!doc.RootElement.TryGetProperty("action", out var action) || action.ValueKind != JsonValueKind.String)
            {
                error = "Feld 'action' fehlt oder ist keine Zeichenkette.";
                return false;
            }

            var actionName = action.GetString();
            if (!string.Equals(actionName, BotMessage.SyncFileAction, StringComparison.Ordinal))
            {
                error = $"Unbekannte Aktion '{actionName}' (nur '{BotMessage.SyncFileAction}').";
                return false;
            }

            // 'fileName' ist optional: Der Beobachter hat oft keinen Kopf-Text erwischt, dann
            // kommt der Pfad aus dem Inhalt (CodeBlockParser). Nur ein Fremdtyp ist ein Fehler.
            var suppliedName = string.Empty;
            if (doc.RootElement.TryGetProperty("fileName", out var fileName))
            {
                if (fileName.ValueKind != JsonValueKind.String)
                {
                    error = "Feld 'fileName' ist keine Zeichenkette.";
                    return false;
                }
                suppliedName = fileName.GetString() ?? string.Empty;
            }

            if (!doc.RootElement.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.String)
            {
                error = "Feld 'content' fehlt oder ist keine Zeichenkette.";
                return false;
            }

            var text = content.GetString() ?? string.Empty;

            var size = System.Text.Encoding.UTF8.GetByteCount(text);
            if (size > maxContentBytes)
            {
                error = $"Inhalt {size} Bytes über Limit {maxContentBytes} – verworfen.";
                return false;
            }

            message = new BotMessage(BotMessage.SyncFileAction, suppliedName, text, size);
            return true;
        }
    }
}
