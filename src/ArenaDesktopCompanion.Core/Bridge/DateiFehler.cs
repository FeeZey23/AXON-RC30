namespace ArenaDesktopCompanion.Core.Bridge;

/// <summary>
/// Übersetzt Betriebssystem-Fehler beim Lesen und Schreiben in einen Satz, mit dem man etwas
/// anfangen kann.
/// </summary>
/// <remarks>
/// <para>
/// Anlass (rc18, beim Abnahmelauf über die Brücke gemessen): Ein Schreibauftrag auf die laufende
/// Anwendungsdatei kam als <i>„The process cannot access the file 'C:\\…\\ArenaDesktopCompanion.dll'
/// because it is being used by another process."</i> zurück – englisch, ohne Hinweis, was zu tun
/// ist. Der Nutzer sitzt davor und soll das lesen. Das ist dieselbe Klasse wie der Fund 8.19, der
/// für <b>Befehle</b> schon eine Erklärung bekommen hat; für Lese- und Schreibaufträge fehlte sie.
/// </para>
/// <para>
/// Bewusst hier im Kern und nicht im Fenster: So ist es ohne Windows prüfbar, und jede Stelle, die
/// eine Auftragsmeldung baut, kann denselben Wortlaut benutzen.
/// </para>
/// </remarks>
public static class DateiFehler
{
    /// <summary>
    /// Ein deutscher Satz zu diesem Fehler – oder <c>null</c>, wenn es kein Fall ist, den wir
    /// erklären können (dann bleibt die ursprüngliche Meldung stehen).
    /// </summary>
    public static string? Klar(Exception? ex) => ex switch
    {
        UnauthorizedAccessException => "Zugriff verweigert – die Datei ist schreibgeschützt, oder ein "
                                       + "anderes Programm (oft der Virenschutz) hält sie fest.",

        DirectoryNotFoundException => "Der Zielordner gibt es nicht – er wurde nicht angelegt.",

        PathTooLongException => "Der Pfad ist zu lang (Windows-Grenze 260 Zeichen ohne Freischaltung).",

        IOException io when IstSperre(io.Message)
            => "Die Datei ist gerade durch ein anderes Programm gesperrt. Meist hält das Fenster des "
               + "Companions selbst seine Dateien offen: schließen, dann erneut schreiben.",

        _ => null,
    };

    /// <summary>Steckt in dieser Meldung eine Dateisperre?</summary>
    public static bool IstSperre(string? meldung)
        => !string.IsNullOrEmpty(meldung)
           && (meldung.Contains("being used by another process", StringComparison.OrdinalIgnoreCase)
               || meldung.Contains("MSB3027", StringComparison.Ordinal)
               || meldung.Contains("MSB3021", StringComparison.Ordinal));

    /// <summary>
    /// Die vollständige Meldung für die Gegenseite: die ursprüngliche Zeile bleibt vorn stehen
    /// (sie nennt Datei und Herkunft), dahinter kommt der Klartext.
    /// </summary>
    public static string Aufklaren(Exception? ex)
    {
        var roh = ex?.Message ?? string.Empty;
        var klar = Klar(ex);
        return klar is null || roh.Contains(klar, StringComparison.Ordinal)
            ? roh
            : $"{roh} — {klar}";
    }

    /// <summary>
    /// Dasselbe für eine schon vorliegende Zeile (etwa die Ausgabe eines Befehls), die nicht als
    /// Ausnahme bei uns angekommen ist.
    /// </summary>
    public static string Aufklaren(string? meldung)
        => IstSperre(meldung)
            ? $"{meldung} — Die Datei ist durch ein laufendes Programm gesperrt. Meist ist es der "
              + "Companion selbst: Fenster schließen, dann erneut versuchen."
            : meldung ?? string.Empty;
}
