using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ArenaDesktopCompanion.Core.Bridge;

/// <summary>Eine Datei im Briefkasten, wie die Liste sie nennt.</summary>
public sealed record RelayFile(string Name, long Bytes, string? Sha256, string? UpdatedAt);

/// <summary>
/// Der Briefkasten: ein Ort im Netz, an dem beide Seiten lesen und schreiben können, ohne dass
/// eine von beiden von außen erreichbar sein muss.
/// </summary>
/// <remarks>
/// <para>
/// Warum so und nicht direkter: Der Assistent arbeitet in einer Umgebung ohne eingehende
/// Verbindungen; der Rechner des Nutzers ist ohne Portfreigabe ebenfalls nicht erreichbar. Beide
/// können aber ausgehend ins Netz. Der Briefkasten ist deshalb der Treffpunkt: Der Companion legt
/// dort Abbilder und Ergebnisse ab und holt Aufträge ab, der Assistent umgekehrt.
/// </para>
/// <para>
/// Umgesetzt ist das über filebin.net: kein Konto, kein Schlüssel, keine Kosten. Der Name des
/// Briefkastens ist das Geheimnis – er wird zufällig erzeugt und nur an den Assistenten
/// weitergegeben. Ein Briefkasten läuft nach sechs Tagen ab; legt eine Seite etwas hinein, entsteht
/// er neu. Inhalte werden vor dem Senden geprüft und die Größe je Datei begrenzt.
/// </para>
/// </remarks>
public sealed class RelayClient : IDisposable
{
    /// <summary>Größte Datei, die der Briefkasten annehmen soll (Abbild, Ergebnis, Log).</summary>
    public const long MaxDateiBytes = 4 * 1024 * 1024;

    private const string Basis = "https://filebin.net";

    /// <summary>
    /// Kennung, mit der sich der Companion beim Briefkasten meldet.
    /// </summary>
    /// <remarks>
    /// Der Zusatz „curl/8.5.0“ ist kein Zierrat, sondern notwendig: filebin liefert Downloads nur an
    /// Programme aus, die es als bekannten Abrufdienst erkennt (curl, Wget – geprüft am 21.09.2026).
    /// Ohne diesen Zusatz kommt statt der Datei eine HTML-Seite zurück, und die Brücke bliebe stumm,
    /// obwohl technisch alles stimmt. Der eigene Name steht bewusst davor.
    /// </remarks>
    public const string Kennung = "ArenaDesktopCompanion/0.2.0-rc30 (curl/8.5.0)";

    private readonly HttpClient _http;

    public RelayClient(HttpClient? http = null, string basisUrl = Basis)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
        if (_http.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", Kennung);
        }

        BasisUrl = basisUrl.TrimEnd('/');
    }

    public string BasisUrl { get; }
    public string? TransportSecret { get; set; }
    public bool TransportSecured => !string.IsNullOrWhiteSpace(TransportSecret);

    /// <summary>Name des Briefkastens – das Geheimnis, das beide Seiten teilen.</summary>
    public string? Bin { get; set; }

    /// <summary>Erzeugt einen neuen Briefkastennamen (24 Zeichen, zufällig, gut lesbar).</summary>
    public static string NeuerName()
    {
        const string alphabet = "abcdefghijkmnpqrstuvwxyz23456789"; // ohne l, o, 0, 1 – verwechselbar
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(24);
        var sb = new StringBuilder(24);
        foreach (var b in bytes)
        {
            sb.Append(alphabet[b % alphabet.Length]);
        }

        return "adc-" + sb;
    }

    /// <summary>
    /// Erzeugt ein Kennwort für die andere Seite: vier Blöcke zu vier Zeichen, aus demselben
    /// gut lesbaren Alphabet. Kein Konto, kein Schlüssel – nur etwas, das nur diese beiden Seiten
    /// kennen. Ohne dieses Kennwort führt der Companion keinen Auftrag aus.
    /// </summary>
    public static string NeuesKennwort()
    {
        const string alphabet = "abcdefghijkmnpqrstuvwxyz23456789";
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16);
        var sb = new StringBuilder(19);
        for (var i = 0; i < bytes.Length; i++)
        {
            if (i > 0 && i % 4 == 0)
            {
                sb.Append('-');
            }

            sb.Append(alphabet[bytes[i] % alphabet.Length]);
        }

        return sb.ToString();
    }

    /// <summary>Prüft einen Namen, bevor er benutzt wird (kein Pfad, keine Sonderzeichen).</summary>
    public static bool NameIstGueltig(string? name)
        => !string.IsNullOrWhiteSpace(name)
           && name.Length is >= 8 and <= 64
           && name.All(c => char.IsLetterOrDigit(c) || c is '-' or '_');

    /// <summary>Legt eine Datei in den Briefkasten (ersetzt eine vorhandene gleichen Namens).</summary>
    public async Task<bool> PutAsync(string name, string inhalt, CancellationToken cancellationToken = default)
    {
        RequireBin();
        if (Encoding.UTF8.GetByteCount(inhalt) > MaxDateiBytes)
            throw new InvalidOperationException("Datei zu gross für den Briefkasten.");
        var text = TransportSecured ? BridgeCipher.Encrypt(Bin!, name, inhalt, TransportSecret!) : inhalt;
        return await PutPublicBytesAsync(name, Encoding.UTF8.GetBytes(text), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Nur für das öffentliche, geheimnisfreie Installationsprogramm; SHA-256 steht im Briefing.</summary>
    public async Task<bool> PutPublicBytesAsync(string name, byte[] daten, CancellationToken cancellationToken = default)
    {
        RequireBin();
        if (daten.LongLength > 6 * 1024 * 1024) throw new InvalidOperationException("Transportdatei zu gross.");
        using var req = new HttpRequestMessage(HttpMethod.Put, $"{BasisUrl}/{Bin}/{Uri.EscapeDataString(name)}")
        { Content = new ByteArrayContent(daten) };
        req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var response = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return true;
    }

    private static async Task<string> ReadBoundedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        const int limit = 6 * 1024 * 1024;
        if (response.Content.Headers.ContentLength > limit) throw new InvalidOperationException("Antwort zu groß (6 MiB).");
        using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (output.Length + count > limit) throw new InvalidOperationException("Antwort beim Lesen zu groß (6 MiB).");
            output.Write(buffer, 0, count);
        }
        return Encoding.UTF8.GetString(output.ToArray());
    }

    /// <summary>Holt eine Datei. <c>null</c>, wenn es sie nicht gibt.</summary>
    public async Task<string?> GetAsync(string name, CancellationToken cancellationToken = default)
    {
        RequireBin();
        using var antwort = await _http
            .GetAsync($"{BasisUrl}/{Bin}/{Uri.EscapeDataString(name)}", HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (antwort.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        antwort.EnsureSuccessStatusCode();

        var text = await ReadBoundedAsync(antwort, cancellationToken).ConfigureAwait(false);

        // filebin liefert bei unbekannten Dateien eine HTML-Seite statt 404 – die erkennen wir am Anfang.
        if (text.TrimStart().StartsWith("<!doctype html", StringComparison.OrdinalIgnoreCase)
            || text.TrimStart().StartsWith("<html", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return TransportSecured ? BridgeCipher.Decrypt(Bin!, name, text, TransportSecret!) : text;
    }

    /// <summary>Was liegt im Briefkasten?</summary>
    public async Task<IReadOnlyList<RelayFile>> ListAsync(CancellationToken cancellationToken = default)
    {
        RequireBin();
        using var anfrage = new HttpRequestMessage(HttpMethod.Get, $"{BasisUrl}/{Bin}");
        anfrage.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var antwort = await _http.SendAsync(anfrage, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (antwort.StatusCode == System.Net.HttpStatusCode.NotFound) return [];
        antwort.EnsureSuccessStatusCode();

        var json = await ReadBoundedAsync(antwort, cancellationToken).ConfigureAwait(false);
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("files", out var dateien) || dateien.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var liste = new List<RelayFile>();
            foreach (var datei in dateien.EnumerateArray())
            {
                liste.Add(new RelayFile(
                    datei.TryGetProperty("filename", out var n) ? n.GetString() ?? string.Empty : string.Empty,
                    datei.TryGetProperty("bytes", out var b) && b.TryGetInt64(out var l) ? l : 0,
                    datei.TryGetProperty("sha256", out var s) ? s.GetString() : null,
                    datei.TryGetProperty("updated_at", out var u) ? u.GetString() : null));
            }

            return liste;
        }
        catch (JsonException)
        {
            // Kein Briefkasten (mehr) vorhanden: leere Liste statt Ausnahme.
            return [];
        }
    }

    /// <summary>Gibt es den Briefkasten überhaupt? (Ein leerer Briefkasten antwortet mit 404.)</summary>
    public async Task<bool> ExistsAsync(CancellationToken cancellationToken = default)
    {
        RequireBin();
        try
        {
            _ = await ListAsync(cancellationToken).ConfigureAwait(false);
            using var antwort = await _http
                .GetAsync($"{BasisUrl}/{Bin}", cancellationToken).ConfigureAwait(false);
            return antwort.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    public async Task DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        RequireBin();
        using var response = await _http.DeleteAsync($"{BasisUrl}/{Bin}/{Uri.EscapeDataString(name)}", cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != System.Net.HttpStatusCode.NotFound) response.EnsureSuccessStatusCode();
    }

    private void RequireBin()
    {
        if (!NameIstGueltig(Bin))
        {
            throw new InvalidOperationException("Kein gültiger Briefkastenname gesetzt.");
        }
    }

    public void Dispose() => _http.Dispose();
}
