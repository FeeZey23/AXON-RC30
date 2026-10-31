using System.Text.RegularExpressions;

namespace ArenaDesktopCompanion.Core.Agents;

/// <summary>
/// Entfernt Schlüssel und Kennwörter aus allem, was ausgeführt wurde und danach angezeigt,
/// protokolliert oder kopiert wird.
/// </summary>
/// <remarks>
/// <para>
/// Die Regel „kein Schlüssel in Dateien oder Protokollen“ (TODO 8.4) reicht nicht, wenn sie nur
/// beim Schreiben gilt: Ein <c>git diff</c> oder eine Fehlermeldung eines Agenten kann einen
/// Schlüssel <i>enthalten</i>, der ganz woanders herkommt – aus einer Konfigurationsdatei im
/// Projekt, aus einer Umgebungsvariablen in einer Ausgabezeile. Deshalb läuft jede Ausgabe durch
/// diese Stelle, bevor sie irgendwohin geht.
/// </para>
/// <para>
/// Bewusst nicht entfernt werden lange Hex-Zeichenfolgen ohne Beschriftung: Das sind meistens
/// Hashes aus Diagnose-Ausgaben, und die braucht man, um einen Fund wiederzufinden. Wo ein
/// Schlüssel gemeint ist, steht in aller Regel ein Name daneben (KEY, TOKEN, SECRET, PASSWORD).
/// </para>
/// </remarks>
public static partial class SecretRedaction
{
    public const string Platzhalter = "«entfernt»";

    /// <summary>Namen, die auf einen Schlüssel hindeuten – auch als Umgebungsvariablenname.</summary>
    private const string Label = @"[A-Za-z0-9_.\-]*(?:API[_-]?KEY|APIKEY|SECRET|TOKEN|PASSWORD|PASSWORT|CREDENTIAL|AUTH)[A-Za-z0-9_.\-]*";

    [GeneratedRegex(@"\bsk-(?:ant-)?[A-Za-z0-9_\-]{12,}", RegexOptions.IgnoreCase)]
    private static partial Regex BekannteVorsaetze();

    [GeneratedRegex(@"(?i)\bBearer\s+[A-Za-z0-9._\-]{12,}")]
    private static partial Regex Bearer();

    [GeneratedRegex(@"-----BEGIN [A-Z ]*PRIVATE KEY-----[\s\S]*?-----END [A-Z ]*PRIVATE KEY-----")]
    private static partial Regex PrivaterSchluessel();

    // Befund vom 21.09.2026 (beim Schreiben des rc13-Tests): Der Name wurde nur in Großschreibung
    // erkannt – `OPENAI_API_KEY=…` fiel auf, `api_key=…` in einer .env-Datei nicht. Für die Zusage
    // „kein Kennwort entkommt“ ist genau das die Lücke, auf die es ankommt.
    [GeneratedRegex("(?<label>" + Label + @")(?<trenner>\s*[=:]\s*|""\s*:\s*"")(?<wert>""?[^\s"";,]{8,}""?)",
        RegexOptions.IgnoreCase)]
    private static partial Regex BeschrifteterWert();

    [GeneratedRegex(@"\b(?:gh[pousr]_[A-Za-z0-9]{20,}|AIza[A-Za-z0-9_\-]{20,}|xox[baprs]-[A-Za-z0-9\-]{10,})")]
    private static partial Regex WeitereAnbieter();

    /// <summary>Ersetzt alles, was wie ein Schlüssel aussieht. Mehrfach anwendbar (idempotent).</summary>
    public static string Clean(string? text) => CleanMitAnzahl(text).Text;

    /// <summary>
    /// Wie <see cref="Clean"/>, nennt aber zusätzlich, <b>wie viele</b> Stellen ersetzt wurden.
    /// </summary>
    /// <remarks>
    /// Befund vom 21.09.2026 (TODO 8.18): Wer eine über die Brücke gelesene Datei zurückschrieb,
    /// bekam eine verstümmelte Fassung – der Platzhalter stand mitten im Code, fünf Zeilen waren
    /// weg, vier Compilerfehler die Folge. Die Zählung hier erlaubt die ehrliche Auskunft
    /// „N Stellen sehen wie ein Schlüssel aus“, <i>ohne</i> den Inhalt zu verändern.
    /// </remarks>
    public static (string Text, int Anzahl) CleanMitAnzahl(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return (string.Empty, 0);
        }

        var anzahl = 0;
        var ergebnis = Ersetze(PrivaterSchluessel(), text,
            "-----BEGIN PRIVATE KEY----- «entfernt» -----END PRIVATE KEY-----", ref anzahl);
        ergebnis = Ersetze(BekannteVorsaetze(), ergebnis, Platzhalter, ref anzahl);
        ergebnis = Ersetze(Bearer(), ergebnis, "Bearer " + Platzhalter, ref anzahl);
        ergebnis = Ersetze(WeitereAnbieter(), ergebnis, Platzhalter, ref anzahl);
        ergebnis = ErsetzeBeschriftet(ergebnis, ref anzahl);
        return (ergebnis, anzahl);
    }

    private static string Ersetze(System.Text.RegularExpressions.Regex regel, string text, string ersatz,
        ref int anzahl)
    {
        var treffer = regel.Matches(text).Count;
        if (treffer == 0)
        {
            return text;
        }

        anzahl += treffer;
        return regel.Replace(text, ersatz);
    }

    private static string ErsetzeBeschriftet(string text, ref int anzahl)
    {
        // Die Zählung darf nicht im Lambda stehen: eine ref-Variable lässt sich dort nicht
        // verändern (CS1628). Also im Lambda zählen und danach addieren.
        var treffer = 0;
        var ergebnis = BeschrifteterWert().Replace(text, m =>
        {
            treffer++;
            return m.Groups["label"].Value + m.Groups["trenner"].Value + Platzhalter;
        });
        anzahl += treffer;
        return ergebnis;
    }

    /// <summary>Enthält der Text überhaupt etwas, das entfernt würde? (Für Hinweise im UI.)</summary>
    public static bool EnthaeltVerdaechtiges(string? text)
        => !string.IsNullOrEmpty(text) && !string.Equals(Clean(text), text, StringComparison.Ordinal);
}
