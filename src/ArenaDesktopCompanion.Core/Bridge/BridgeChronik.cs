using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ArenaDesktopCompanion.Core.Bridge;

/// <summary>Was mit einer Datei passiert ist.</summary>
public enum ChronikArt
{
    Neu,
    Geaendert,
    Weg,
}

/// <summary>Ein Eintrag: eine Datei, eine Änderung, ein Zeitpunkt.</summary>
public sealed record ChronikEintrag(
    [property: JsonPropertyName("s")] long Sequenz,
    [property: JsonPropertyName("t")] string Zeit,
    [property: JsonPropertyName("a")] string Art,
    [property: JsonPropertyName("p")] string Pfad,
    [property: JsonPropertyName("b")] long Bytes,
    [property: JsonPropertyName("h")] string? Hash);

/// <summary>
/// Der Änderungsverlauf des Projektordners – als <b>eine</b> Datei mit begrenzter Länge.
/// </summary>
/// <remarks>
/// <para>
/// Warum das der Kern des Durchblicks ist: Ein vollständiges Abbild kostet rund 114 Bytes je Datei –
/// bei 2 500 Dateien sind das 286 KB. Das bei jeder Änderung zu schicken wäre Verschwendung auf
/// beiden Seiten. Interessant ist aber fast immer nur das, <b>was sich seit dem letzten Blick
/// geändert hat</b>: 123 Änderungen sind 4 KB, also 98 % weniger.
/// </para>
/// <para>
/// Damit daraus kein wachsender Haufen wird, ist der Verlauf ein <b>Ring</b>: die letzten
/// <see cref="MaxEintraege"/> Änderungen, in einer Datei, die immer vollständig überschrieben wird.
/// Oben stehen fortlaufende Nummer (<c>seq</c>), die älteste noch enthaltene Nummer (<c>von</c>)
/// und der aktuelle Gesamtstand. Die andere Seite merkt sich ihre letzte Nummer: liegt die noch im
/// Ring, genügen die neuen Einträge; liegt sie davor, ist der Ring einmal überrundet worden und es
/// wird einmal der volle Stand geholt.
/// </para>
/// </remarks>
public sealed class BridgeChronik
{
    /// <summary>So viele Änderungen passen in den Ring. 400 Einträge ≈ 30 KB im schlimmsten Fall.</summary>
    public const int MaxEintraege = 400;

    private readonly List<ChronikEintrag> _eintraege = [];
    private readonly object _gate = new();

    public long Sequenz { get; private set; }

    /// <summary>Älteste noch enthaltene Nummer (0, solange nichts verdichtet wurde).</summary>
    public long Von { get; private set; }

    public IReadOnlyList<ChronikEintrag> Eintraege { get { lock (_gate) return _eintraege.ToArray(); } }

    /// <summary>Übernimmt die Änderungen eines Vergleichs und vergibt fortlaufende Nummern.</summary>
    public void Aufnehmen(
        IReadOnlyList<string> neu,
        IReadOnlyList<string> geaendert,
        IReadOnlyList<string> weg,
        DateTime zeit,
        Func<string, (long Bytes, string Hash)>? masse = null)
    {
        lock (_gate)
        {
        var stempel = zeit.ToString("yyyy-MM-dd HH:mm:ss");

        foreach (var pfad in neu)
        {
            Hinzu(ChronikArt.Neu, pfad, stempel, masse);
        }

        foreach (var pfad in geaendert)
        {
            Hinzu(ChronikArt.Geaendert, pfad, stempel, masse);
        }

        foreach (var pfad in weg)
        {
            Hinzu(ChronikArt.Weg, pfad, stempel, masse);
        }
        }
    }

    private void Hinzu(ChronikArt art, string pfad, string stempel, Func<string, (long, string)>? masse)
    {
        long bytes = 0;
        string? hash = null;
        if (art != ChronikArt.Weg && masse is not null)
        {
            (bytes, hash) = masse(pfad);
        }

        Sequenz++;
        var kurz = hash is { Length: > 8 } ? hash[..8] : hash;
        _eintraege.Add(new ChronikEintrag(Sequenz, stempel, Kuerzel(art), pfad, bytes, kurz));
        Verdichten();
    }

    /// <summary>Alte Einträge fallen oben heraus – der Ring wird nie größer.</summary>
    private void Verdichten()
    {
        if (_eintraege.Count <= MaxEintraege)
        {
            return;
        }

        var weg = _eintraege.Count - MaxEintraege;
        _eintraege.RemoveRange(0, weg);
        Von = _eintraege[0].Sequenz - 1;
    }

    private static string Kuerzel(ChronikArt art) => art switch
    {
        ChronikArt.Neu => "neu",
        ChronikArt.Geaendert => "geaendert",
        _ => "weg",
    };

    /// <summary>
    /// Schreibt die Chronik. Bewusst von Hand und in Kurzform: <c>["g","src/x.cs",3500,"1a2b3c4d",1731…]</c>
    /// statt <c>{"t":"geaendert","p":"src/x.cs",…}</c>. Die Feldnamen wären je Eintrag rund 45 Bytes
    /// Ballast – bei 400 Einträgen sind das 18 KB, die niemand braucht. Ein Eintrag wiegt so ~70 statt
    /// ~120 Bytes. Zeiten stehen als Sekunden seit 1970, der Hash gekürzt auf acht Zeichen; zum
    /// Erkennen von Änderungen genügt das.
    /// </summary>
    public string ToJson(BridgeStand stand)
    {
        lock (_gate)
        {
        var sb = new StringBuilder(4096);
        sb.Append("{\"v\":1,\"seq\":").Append(Sequenz)
          .Append(",\"von\":").Append(Von)
          .Append(",\"zeit\":").Append(Text(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")))
          .Append(",\"stand\":[").Append(stand.Dateien).Append(',').Append(stand.Bytes)
          .Append(',').Append(Text(stand.Host)).Append(',').Append(Text(stand.App))
          .Append(',').Append(Text(stand.Git)).Append(',').Append(stand.Geaendert).Append(',').Append(stand.Neu)
          .Append(',').Append(Text(stand.Inbox)).Append(']')
          .Append(",\"aenderungen\":[");

        for (var i = 0; i < _eintraege.Count; i++)
        {
            var e = _eintraege[i];
            if (i > 0)
            {
                sb.Append(',');
            }

            sb.Append('[').Append(Text(e.Art)).Append(',').Append(Text(e.Pfad)).Append(',').Append(e.Bytes);
            if (e.Hash is { Length: > 0 })
            {
                sb.Append(',').Append(Text(e.Hash));
            }

            sb.Append(']');
        }

        return sb.Append("]}").ToString();
        }
    }

    /// <summary>
    /// Der Ring in der Speicherform des Standspeichers – damit der Verlauf den Neustart übersteht.
    /// </summary>
    /// <remarks>
    /// Befund vom 21.09.2026 (Abnahmepunkt 8.9): Der Stand überlebte den Neustart, der Verlauf nicht.
    /// Im Betrieb sah das so aus: zwei neue und eine geänderte Datei waren noch vor dem Beenden
    /// erkannt worden, nach dem Neustart stand in <c>chronik.json</c> aber nur noch die eine Löschung,
    /// die danach erkannt wurde. Ein Verlauf, der bei jedem Start bei null anfängt, ist keiner.
    /// </remarks>
    public string ToSpeicher()
    {
        lock (_gate)
        {
        var sb = new StringBuilder(1024);
        sb.Append("{\"v\":1,\"seq\":").Append(Sequenz)
          .Append(",\"von\":").Append(Von)
          .Append(",\"e\":[");

        for (var i = 0; i < _eintraege.Count; i++)
        {
            var e = _eintraege[i];
            if (i > 0)
            {
                sb.Append(',');
            }

            sb.Append('[').Append(e.Sequenz).Append(',').Append(Text(e.Zeit)).Append(',').Append(Text(e.Art))
              .Append(',').Append(Text(e.Pfad)).Append(',').Append(e.Bytes);
            if (e.Hash is { Length: > 0 })
            {
                sb.Append(',').Append(Text(e.Hash));
            }

            sb.Append(']');
        }

        return sb.Append("]}").ToString();
        }
    }

    /// <summary>
    /// Nimmt einen gespeicherten Ring wieder auf. Alles Unerwartete wird übergangen: ein kaputter
    /// Speicher darf den Durchblick nicht verhindern (dann beginnt der Verlauf eben neu).
    /// </summary>
    /// <returns><c>true</c>, wenn ein Verlauf übernommen wurde.</returns>
    public bool AusSpeicher(string? json)
    {
        lock (_gate)
        {
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var dok = JsonDocument.Parse(json);
            var wurzel = dok.RootElement;
            if (wurzel.ValueKind != JsonValueKind.Object
                || !wurzel.TryGetProperty("e", out var liste)
                || liste.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var eintraege = new List<ChronikEintrag>();
            foreach (var e in liste.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() < 5)
                {
                    continue;
                }

                eintraege.Add(new ChronikEintrag(
                    e[0].GetInt64(),
                    e[1].GetString() ?? string.Empty,
                    e[2].GetString() ?? string.Empty,
                    e[3].GetString() ?? string.Empty,
                    e[4].GetInt64(),
                    e.GetArrayLength() > 5 ? e[5].GetString() : null));
            }

            _eintraege.Clear();
            _eintraege.AddRange(eintraege);

            Sequenz = wurzel.TryGetProperty("seq", out var s) && s.TryGetInt64(out var nummer)
                ? nummer
                : _eintraege.Count > 0 ? _eintraege[^1].Sequenz : 0;
            Von = wurzel.TryGetProperty("von", out var v) && v.TryGetInt64(out var alt) ? alt : 0;

            // Die Nummer darf nie hinter den Einträgen zurückbleiben – sonst gäbe es Dubletten.
            if (_eintraege.Count > 0 && Sequenz < _eintraege[^1].Sequenz)
            {
                Sequenz = _eintraege[^1].Sequenz;
            }

            Verdichten();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        }
    }

    private static string Text(string? wert) => JsonSerializer.Serialize(wert ?? string.Empty);
}

/// <summary>Kurzfassung des Gesamtstands – klein genug, um sie in der Chronik mitzuschicken.</summary>
public sealed record BridgeStand(
    [property: JsonPropertyName("dateien")] int Dateien,
    [property: JsonPropertyName("bytes")] long Bytes,
    [property: JsonPropertyName("host")] string? Host,
    [property: JsonPropertyName("app")] string? App,
    [property: JsonPropertyName("git")] string? Git,
    [property: JsonPropertyName("geaendert")] int Geaendert,
    [property: JsonPropertyName("neu")] int Neu,
    [property: JsonPropertyName("inbox")] string? Inbox);

/// <summary>Die Datei, wie sie im Briefkasten liegt.</summary>
public sealed record ChronikDatei
{
    [JsonPropertyName("v")] public int SchemaVersion { get; init; } = 1;
    [JsonPropertyName("seq")] public long Sequenz { get; init; }
    [JsonPropertyName("von")] public long Von { get; init; }
    [JsonPropertyName("zeit")] public string Zeit { get; init; } = string.Empty;
    [JsonPropertyName("stand")] public BridgeStand? Stand { get; init; }
    [JsonPropertyName("aenderungen")] public IReadOnlyList<ChronikEintrag> Eintraege { get; init; } = [];
}
