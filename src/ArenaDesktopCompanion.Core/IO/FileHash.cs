using System.Security.Cryptography;
using System.Text;

namespace ArenaDesktopCompanion.Core.IO;

/// <summary>
/// Byte-genauer Inhaltsvergleich über SHA-256. Es wird immer die exakte Bytefolge
/// gehasht, die auch auf die Platte geschrieben würde – keine Zeilenende-Normalisierung.
/// </summary>
public static class FileHash
{
    public static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public static byte[] ToBytes(string text) => Utf8NoBom.GetBytes(text);

    public static string OfBytes(ReadOnlySpan<byte> bytes)
    {
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string OfText(string text) => OfBytes(Utf8NoBom.GetBytes(text));

    /// <returns>Hash des Dateiinhalts, oder <see langword="null"/> wenn die Datei nicht gelesen werden kann.</returns>
    public static string? OfFile(string fullPath)
    {
        try
        {
            using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var hash = SHA256.HashData(stream);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}
