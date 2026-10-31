using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ArenaDesktopCompanion.Core.Bridge;

/// <summary>AES-256-GCM; Dateiname und Briefkasten sind authentifizierte Zusatzdaten.</summary>
public static class BridgeCipher
{
    private sealed record Envelope(
        [property: JsonPropertyName("v")] int Version,
        [property: JsonPropertyName("nonce")] string Nonce,
        [property: JsonPropertyName("data")] string Data);
    private sealed record Payload(
        [property: JsonPropertyName("ts")] long Time,
        [property: JsonPropertyName("body")] string Body);

    private static byte[] Key(string secret) => SHA256.HashData(Encoding.UTF8.GetBytes("AXON-GCM-v1\0" + secret));
    private static byte[] Aad(string bin, string name) => Encoding.UTF8.GetBytes("AXON1\0" + bin + "\0" + name);

    public static string Encrypt(string bin, string name, string body, string secret, DateTimeOffset? now = null)
    {
        var clear = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new Payload((now ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds(), body)));
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[clear.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(Key(secret), 16);
        aes.Encrypt(nonce, clear, cipher, tag, Aad(bin, name));
        var data = new byte[cipher.Length + tag.Length];
        cipher.CopyTo(data, 0); tag.CopyTo(data, cipher.Length);
        return JsonSerializer.Serialize(new Envelope(1, Convert.ToBase64String(nonce), Convert.ToBase64String(data)));
    }

    public static string Decrypt(string bin, string name, string text, string secret, DateTimeOffset? now = null)
    {
        try
        {
            var envelope = JsonSerializer.Deserialize<Envelope>(text) ?? throw new CryptographicException("Kein verschlüsselter Umschlag.");
            if (envelope.Version != 1) throw new CryptographicException("Unbekannte Transportversion; kein Klartext-Fallback.");
            var nonce = Convert.FromBase64String(envelope.Nonce);
            var data = Convert.FromBase64String(envelope.Data);
            if (nonce.Length != 12 || data.Length < 16) throw new CryptographicException("Ungültiger Umschlag.");
            var clear = new byte[data.Length - 16];
            using var aes = new AesGcm(Key(secret), 16);
            aes.Decrypt(nonce, data.AsSpan(0, clear.Length), data.AsSpan(clear.Length), clear, Aad(bin, name));
            var payload = JsonSerializer.Deserialize<Payload>(clear) ?? throw new CryptographicException("Ungültige Nutzlast.");
            if (name.StartsWith("job-", StringComparison.Ordinal))
            {
                var age = (now ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds() - payload.Time;
                if (age > 900 || age < -300) throw new CryptographicException("Auftrag abgelaufen oder Uhrabweichung größer als 5 Minuten.");
            }
            return payload.Body;
        }
        catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException)
        { throw new CryptographicException("Verschlüsselten Transport nicht verifiziert; nichts ausgeführt.", ex); }
    }
}
