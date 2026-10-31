using System.Text.Json;

namespace ArenaDesktopCompanion.Core.Bridge;

/// <summary>
/// Der Bericht, den das Einfüge-Skript aus der Webseite zurückgibt – und der Satz, den der Nutzer
/// daraus liest.
/// </summary>
/// <remarks>
/// <para>
/// Anlass (Fund vom 24.09.2026, live): Die App meldete „Text steht im Eingabefeld von arena.ai",
/// im Chat stand aber nichts. Das Skript gab nämlich <c>ok</c> zurück, sobald es **irgendein** Feld
/// gefunden hatte – ohne nachzusehen, ob der Text dort angekommen ist. Genau diese Prüfung ist
/// hier verpflichtend: <see cref="Bestaetigt"/> und <see cref="Nachpruefung"/> dürfen nicht
/// wegoptimiert werden, sonst lügt die Meldung wieder.
/// </para>
/// <para>
/// Warum im Kern und nicht im Fenster: Der Satz ist das Einzige, was der Nutzer von dem Vorgang
/// sieht. Er muss stimmen – und ist hier ohne Browser prüfbar.
/// </para>
/// </remarks>
public sealed record EinfuegeBericht
{
    /// <summary>Wie viele Eingabefelder die Seite überhaupt angeboten hat.</summary>
    public int Kandidaten { get; init; }

    /// <summary>Beschreibung des gewählten Feldes, z. B. <c>DIV[contenteditable]</c>.</summary>
    public string? Feld { get; init; }

    /// <summary>Welche Strategie den Text hineinbrachte: <c>nativ</c>, <c>execCommand</c>, <c>direkt</c>.</summary>
    public string? Strategie { get; init; }

    /// <summary>Stand der Text unmittelbar nach dem Schreiben drin?</summary>
    public bool Bestaetigt { get; init; }

    /// <summary>Und kurz danach noch? (Leer, wenn nicht nachgesehen wurde.)</summary>
    public bool? Nachpruefung { get; init; }

    /// <summary>Länge des abgelegten Textes, wie die Seite sie zählt.</summary>
    public int Zeichen { get; init; }

    /// <summary>
    /// Liest den Bericht aus der Antwort der Seite. Fehlerhafte oder fehlende Antworten ergeben
    /// einen Bericht mit <see cref="Kandidaten"/> 0 – also „nicht bestätigt".
    /// </summary>
    public static EinfuegeBericht AusJson(string? roh)
    {
        if (string.IsNullOrWhiteSpace(roh))
        {
            return new EinfuegeBericht();
        }

        try
        {
            using var dokument = JsonDocument.Parse(roh);
            var w = dokument.RootElement;

            return new EinfuegeBericht
            {
                Kandidaten = Zahl(w, "kandidaten"),
                Feld = Text(w, "feld"),
                Strategie = Text(w, "strategie"),
                Bestaetigt = w.TryGetProperty("sofort", out var sofort) && sofort.ValueKind == JsonValueKind.True,
                Zeichen = Zahl(w, "zeichen"),
            };
        }
        catch (JsonException)
        {
            return new EinfuegeBericht();
        }
    }

    /// <summary>
    /// Liest die <em>Rohantwort der WebView</em> (<c>ExecuteScriptAsync</c>), nicht nur den Bericht.
    /// Die Antwort ist normalerweise ein JSON-Text in Anführungszeichen; sie kann aber auch
    /// <c>null</c> (Skript ohne Rückgabe) oder ein Objekt sein – dann kommt der Bericht später aus
    /// dem Fenster (<c>window.__axonEinfuegeBericht</c>).
    /// </summary>
    /// <remarks>
    /// Anlass (Fund vom 24.09.2026, live, Punkt 8.27): Das Einfüge-Skript endete mit
    /// <c>return (async () =&gt; { … })();</c> – also mit einem <b>Promise</b>. Die WebView liefert
    /// dafür <c>{}</c>, und <c>Deserialize&lt;string&gt;</c> warf daraufhin
    /// <i>„The JSON value could not be converted to System.String. Path: $ | LineNumber: 0 |
    /// BytePositionInLine: 1."</i> Der Vorgang brach ab, obwohl der Text im Feld stand – das Blatt
    /// schloss sich nie. <see cref="Brauchbar"/> macht diesen Fall sichtbar, statt ihn zu verschweigen.
    /// </remarks>
    public static EinfuegeBericht AusWebViewAntwort(string? rohAntwort, out bool brauchbar)
    {
        brauchbar = false;
        if (string.IsNullOrWhiteSpace(rohAntwort))
        {
            return new EinfuegeBericht();
        }

        try
        {
            using var dokument = JsonDocument.Parse(rohAntwort);
            var wurzel = dokument.RootElement;

            if (wurzel.ValueKind == JsonValueKind.String)
            {
                brauchbar = true;
                return AusJson(wurzel.GetString());
            }

            // Alles andere (null, {}, false, Zahlen) ist keine Antwort – und darf keine Ausnahme geben.
            return new EinfuegeBericht();
        }
        catch (JsonException)
        {
            return new EinfuegeBericht();
        }
    }

    /// <summary>
    /// Die Nachprüfung (kurz danach) nachtragen – <c>"ja"</c>, <c>"nein"</c> oder <c>"keins"</c>
    /// aus dem zweiten Blick der Seite.
    /// </summary>
    public EinfuegeBericht MitNachpruefung(string? antwort) => this with
    {
        Nachpruefung = antwort?.StartsWith("ja", StringComparison.OrdinalIgnoreCase) == true,
    };

    /// <summary>Der Satz für den Nutzer. Sagt die Wahrheit, auch wenn sie unangenehm ist.</summary>
    public string Satz()
    {
        var ziel = Feld is { Length: > 0 } ? Feld : "kein Feld";

        if (Bestaetigt && Nachpruefung is not false)
        {
            return "Der Text steht im Eingabefeld von arena.ai ("
                   + (Nachpruefung == true ? "geprüft, " : string.Empty)
                   + $"{Zeichen} Zeichen, Feld {ziel}). Blatt schließt sich – Enter sendet die Nachricht.";
        }

        if (Bestaetigt && Nachpruefung == false)
        {
            return $"Der Text war kurz im Feld {ziel}, ist aber wieder verschwunden – arena.ai hat ihn "
                   + "verworfen. Der Text liegt in der Zwischenablage: ins Eingabefeld klicken, dann Strg+V.";
        }

        if (Kandidaten == 0)
        {
            return "Auf der Seite war kein Eingabefeld zu finden (ist arena.ai offen?). Der Text liegt in "
                   + "der Zwischenablage: ins Eingabefeld klicken, dann Strg+V.";
        }

        return $"Keine der drei Schreibarten hat im Feld {ziel} gegriffen ({Kandidaten} Felder geprüft). "
               + "Der Text liegt in der Zwischenablage: ins Eingabefeld klicken, dann Strg+V.";
    }

    private static int Zahl(JsonElement w, string name) =>
        w.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.Number ? e.GetInt32() : 0;

    private static string? Text(JsonElement w, string name) =>
        w.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
}
