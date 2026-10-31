using System.Text.RegularExpressions;
using ArenaDesktopCompanion.Core.Sync;

namespace ArenaDesktopCompanion.Core.Import;

public sealed record CodeBlockFile(string RelativePath, string Content, bool HeaderLineRemoved);

/// <summary>
/// Erkennt in einem Text (Arena-Codeblock, Antwort-Absatz, Copy-Paste) eine Dateiangabe und
/// liefert den Inhalt ohne Kopf- und Fence-Zeilen.
/// Der bisherige Ansatz „die allererste Zeile muss ein Kommentar-Header sein“ ging an der
/// Realität vorbei (Code-Fences davor, Markdown-Fett, Unterordner, Unicode) – deshalb:
/// mehrzeilige Suche, mehrere Schreibweisen, explizite Gegenprüfung gegen Zeilen wie
/// „siehe README.md“.
/// </summary>
public static class CodeBlockParser
{
    /// <summary>Ab dieser Zeilenzahl wird nicht mehr nach einem Kopf gesucht.</summary>
    public const int HeaderScanLines = 6;

    public static bool TryParse(string? blockText, out CodeBlockFile? file, int scanLines = HeaderScanLines)
        => TryParse(blockText, out file, out _, scanLines);

    /// <summary> Gleiche Erkennung, aber mit Begründung – damit das Protokoll nicht nur
    /// „kein Dateiname“ sagt, wenn in Wahrheit ein Pfad blockiert wurde.</summary>
    public static bool TryParse(string? blockText, out CodeBlockFile? file, out string? error, int scanLines = HeaderScanLines)
    {
        file = null;
        error = null;
        if (string.IsNullOrWhiteSpace(blockText))
        {
            error = "Leerer Text – nichts zu speichern.";
            return false;
        }

        var lines = SplitLines(blockText);
        var content = new List<string>(lines.Count);
        string? relativePath = null;
        var headerRemoved = false;

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var isFence = IsFence(line);

            // In der Kopfzeile darf der Pfad auch hinter der Fence stehen: ```python path: app.py
            if (relativePath is null && i <= scanLines)
            {
                var probe = isFence ? StripFence(line) : line;
                var candidate = TryExtractPath(probe) ?? TryExtractPath(WithoutLanguageTag(probe));
                if (candidate is not null)
                {
                    relativePath = candidate;
                    headerRemoved = true;
                    continue;
                }
            }

            if (isFence)
            {
                continue;
            }

            content.Add(line);
        }

        if (relativePath is null)
        {
            error = "Kein Dateiname erkennbar (erwartet z. B. '# path: src/app.py', 'Datei: app.py' oder eine Zeile nur mit dem Pfad).";
            return false;
        }

        // Der Parser fällt keine Sicherheitsentscheidung über den Zielordner, aber er gibt einen
        // unbrauchbaren Kandidaten (absolut, "../x", Verbotenzeichen) gar nicht erst weiter.
        if (!Core.Sync.PathSafety.TryValidateRelative(relativePath, out var safeRelative, out var pathError))
        {
            error = $"Dateiname '{relativePath}' blockiert: {pathError}";
            return false;
        }

        var text = string.Join('\n', content).Trim('\n');
        if (text.Trim().Length == 0)
        {
            // Ein Dateikopf ohne Inhalt ist kein Dateiinhalt. Am 24.09.2026 hat der Codeblock-Fanger
            // damit reihenweise leere Dateien angelegt und eine gefüllte Datei geleert (BRUECKE.md).
            error = "Block enthält keinen Dateiinhalt – nichts gespeichert (Schutz vor leeren Dateien).";
            return false;
        }

        file = new CodeBlockFile(safeRelative, text + "\n", headerRemoved);
        return true;
    }

    /// <summary>Extrahiert einen Dateipfad aus einer Zeile, sonst <see langword="null"/>.</summary>
    public static string? TryExtractPath(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        var text = line.Trim();

        // 1) Kommentar + Label:  // path: src/app.py · # file: x.py · /* filename: y.css */ · <!-- file: index.html -->
        var match = CommentHeaderRegex.Match(text);
        if (match.Success)
        {
            return Core.Sync.PathSafety.CleanCandidate(match.Groups["name"].Value);
        }

        // 2) Label ohne Kommentar:  File: app.py · Save to: src/a.py · datei = main.py
        match = LabelValueRegex.Match(text);
        if (match.Success)
        {
            var value = Core.Sync.PathSafety.CleanCandidate(match.Groups["name"].Value);
            if (HasExtension(value))
            {
                return value;
            }
        }

        // 3) Inverse Schreibweise:  app.py (src) · main.py – Python  → Pfad am Zeilenanfang
        match = PathThenNoteRegex.Match(text);
        if (match.Success)
        {
            var value = Core.Sync.PathSafety.CleanCandidate(match.Groups["name"].Value);
            if (HasExtension(value))
            {
                return value;
            }
        }

        // 4) Nackter Pfad als ganze Zeile, mit oder ohne Markdown-Umrandung.
        //    Leerzeichen sind hier verboten, damit „siehe README.md“ kein Treffer ist – und die
        //    Formprüfung zusätzlich, damit „Dispatcher.Invoke“ (Mitgliedszugriff) keiner wird.
        match = BarePathRegex.Match(text);
        if (match.Success)
        {
            var value = Core.Sync.PathSafety.CleanCandidate(match.Groups["name"].Value);
            if (HasExtension(value) && Core.Sync.PathSafety.LooksLikeFileName(value))
            {
                return value;
            }
        }

        // 5) Inline-Code in einem Satz:  Speichere das als `src/app.py`.
        foreach (Match code in InlineCodeRegex.Matches(text))
        {
            var value = Core.Sync.PathSafety.CleanCandidate(code.Groups["name"].Value);
            if (HasExtension(value) && LooksLikeProse(text) && Core.Sync.PathSafety.LooksLikeFileName(value))
            {
                return value;
            }
        }

        return null;
    }

    private static bool HasExtension(string? candidate)
        => candidate is not null
           && candidate.IndexOf('.') > 0
           && ExtensionRegex.IsMatch(candidate);

    private static bool LooksLikeProse(string text)
        => text.Length > 8 && text.Contains(' ');

    /// <summary>
    /// Der Infostring einer Fence-Zeile: <c>app.py</c> aus <c>```app.py</c> oder
    /// <c>python path: app.py</c> aus <c>```python path: app.py</c> – mit und ohne schließende
    /// Fence auf derselben Zeile gleich behandelt. (Bis rc20 lieferte das nur bei einer schließenden
    /// Fence etwas, die im Hilfetext beworbene Schreibweise ```python path: app.py lief ins Leere.)
    /// </summary>
    private static string StripFence(string line)
    {
        var t = line.Trim();
        var laenge = 0;
        while (laenge < t.Length && (t[laenge] == '`' || t[laenge] == '~'))
        {
            laenge++;
        }
        if (laenge < 3)
        {
            return string.Empty;
        }

        var rest = t[laenge..];
        var schliessend = rest.IndexOfAny(new[] { '`', '~' });
        return (schliessend >= 0 ? rest[..schliessend] : rest).Trim();
    }

    /// <summary>Infostrings beginnen oft mit der Sprache: „python path: app.py“ → „path: app.py“.</summary>
    private static string WithoutLanguageTag(string infoString)
    {
        var sp = infoString.IndexOf(' ');
        if (sp <= 0)
        {
            return string.Empty;
        }

        var erstes = infoString[..sp];
        return erstes.All(char.IsAsciiLetter) ? infoString[(sp + 1)..].Trim() : string.Empty;
    }

    private static List<string> SplitLines(string text)
        => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n').ToList();

    private static bool IsFence(string line)
    {
        var t = line.TrimStart();
        return t.StartsWith("```", StringComparison.Ordinal) || t.StartsWith("~~~", StringComparison.Ordinal);
    }

    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(200);

    /// <summary>Kommentar + Label, z. B. <c>// path: src/app.py</c>, <c>/* file: a.css */</c>, <c>&lt;!-- file: index.html --&gt;</c>.</summary>
    private static readonly Regex CommentHeaderRegex = new(
        @"^(?:/{2,}|#+|/\*+|\*+|<!--|;{2,}|--+)\s*(?:filename|file|path|dateiname|datei|pfad|output|save\s*to|save\s*as|save|ziel|spec)\s*[:=]\s*(?<name>.+?)\s*(?:\*+/|-->)?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, Timeout);

    /// <summary>Label ohne Kommentar, z. B. <c>File: app.py</c>, <c>save to = src/a.py</c>.</summary>
    private static readonly Regex LabelValueRegex = new(
        @"^(?:file(?:name)?|path|target|datei|dateiname|pfad|save(?:\s*(?:to|as))?)\s*[:=]\s*(?<name>.+?)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, Timeout);

    /// <summary>Pfad am Zeilenanfang mit Anhängsel, z. B. <c>app.py – Hauptdatei</c> oder <c>src/a.py (Python)</c>.</summary>
    private static readonly Regex PathThenNoteRegex = new(
        @"^(?<name>[^\s:]{1,200}?)(?:\s+[–-]\s+\S.{0,80}|\s*\([^)]{1,40}\))\s*$",
        RegexOptions.CultureInvariant, Timeout);

    /// <summary>Nackter Pfad als ganze Zeile, optional in Markdown-Zeichen. Keine Leerzeichen –
    /// sonst wäre „siehe README.md“ ein Treffer.</summary>
    private static readonly Regex BarePathRegex = new(
        @"^[*_`]{0,3}(?<name>[\p{L}\p{N}_\-.]+(?:/[\p{L}\p{N}_\-.]+)?)[*_`]{0,3}$",
        RegexOptions.CultureInvariant, Timeout);

    private static readonly Regex ExtensionRegex = new(
        @"\.(?<ext>[A-Za-z0-9]{1,8})$", RegexOptions.CultureInvariant, Timeout);

    /// <summary>Inline-Code in einem Satz, z. B. Speichere das als `src/app.py`.</summary>
    private static readonly Regex InlineCodeRegex = new(
        @"`(?<name>[^`\r\n]{1,200})`", RegexOptions.CultureInvariant, Timeout);

}
