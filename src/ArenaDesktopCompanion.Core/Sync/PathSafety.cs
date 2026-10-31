namespace ArenaDesktopCompanion.Core.Sync;

/// <summary>
/// Der Sicherheitsanker des Projekts: wandelt einen beliebigen, aus dem Web stammenden Pfad in
/// einen garantiert Within-The-Root-Pfad um. Alles, was nicht zweifelsfrei sicher ist, wird
/// abgelehnt – nicht „repariert“.
/// </summary>
public static class PathSafety
{
    public const int MaxTotalLength = 240;
    public const int MaxSegmentLength = 120;

    /// <summary>Windows-Verbotenzeichen, hartcodiert, damit die Prüfung auf jedem System identisch ist
    /// (<see cref="Path.GetInvalidFileNameChars"/> liefert unter Linux eine andere Menge).</summary>
    private static readonly HashSet<char> ForbiddenChars = new(new[]
    {
        '<', '>', ':', '"', '/', '\\', '|', '?', '*', '\0'
    });

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    /// <summary>
    /// Prüft einen relativen Pfad und gibt die normalisierte Form (`/`-Trenner) zurück.
    /// Verwirft u. a.: absolute Pfade, Laufwerksbuchstaben, UNC, <c>..</c>, Punkt-Kaskaden,
    /// Verbotenzeichen, reservierte Windows-Namen, Segmente, die auf '.' oder Leerzeichen enden,
    /// Steuerzeichen und Überlängen.
    /// </summary>
    public static bool TryValidateRelative(string? candidateRelativePath, out string relativePath, out string? error)
    {
        relativePath = string.Empty;
        error = null;

        if (candidateRelativePath is null || candidateRelativePath.Trim().Length == 0)
        {
            error = "Dateiname fehlt.";
            return false;
        }

        var raw = candidateRelativePath.Trim().Trim('"', '\'');

        if (raw.Contains("://", StringComparison.Ordinal))
        {
            error = "URL-artiger Pfad blockiert.";
            return false;
        }
        if (raw.Length >= 2 && char.IsAsciiLetter(raw[0]) && raw[1] == ':')
        {
            error = "Laufwerkspfad blockiert.";
            return false;
        }
        if (raw.StartsWith('/') || raw.StartsWith('\\'))
        {
            error = "Absoluter Pfad blockiert.";
            return false;
        }
        if (raw.Contains(':', StringComparison.Ordinal))
        {
            error = "Doppelpunkt im Pfad blockiert (Windows/ADS).";
            return false;
        }

        var segments = raw.Split(new[] { '/', '\\' }, StringSplitOptions.None);
        var clean = new List<string>(segments.Length);

        for (var index = 0; index < segments.Length; index++)
        {
            var segment = segments[index].Trim();
            var isLast = index == segments.Length - 1;

            if (segment.Length == 0)
            {
                error = "Leeres Segment (doppelte Trenner oder hängender Slash).";
                return false;
            }
            if (segment == ".")
            {
                if (isLast)
                {
                    error = "Pfad endet auf '.' – das ist ein Verzeichnis, keine Datei.";
                    return false;
                }
                continue; // "./src/a.py" ist harmlos
            }
            if (segment == ".." || segment.Contains("..", StringComparison.Ordinal))
            {
                error = "Elternverzeichnis ('..') blockiert.";
                return false;
            }
            if (segment.Length > MaxSegmentLength)
            {
                error = $"Segment länger als {MaxSegmentLength} Zeichen.";
                return false;
            }

            foreach (var c in segment)
            {
                if (c < ' ' || ForbiddenChars.Contains(c))
                {
                    error = c < ' '
                        ? $"Steuerzeichen 0x{(int)c:X2} nicht erlaubt."
                        : $"Zeichen '{c}' ist nicht erlaubt.";
                    return false;
                }
            }

            if (segment.EndsWith('.') || segment.EndsWith(' '))
            {
                error = "Segment endet auf Punkt oder Leerzeichen (Windows würde es still entfernen).";
                return false;
            }
            if (IsReserved(segment))
            {
                error = $"Reservierter Windows-Name '{segment}'.";
                return false;
            }

            clean.Add(segment);
        }

        if (clean.Count == 0)
        {
            error = "Kein gültiger Dateiname übrig.";
            return false;
        }

        relativePath = string.Join('/', clean);
        if (relativePath.Length > MaxTotalLength)
        {
            error = $"Relativer Pfad länger als {MaxTotalLength} Zeichen.";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Versucht <paramref name="candidateRelativePath"/> innerhalb von <paramref name="root"/>
    /// aufzulösen. Zweite Verteidigungslinie nach der Zeichenprüfung: der normalisierte
    /// Vollpfad muss mit dem Root + Trenner beginnen.
    /// </summary>
    public static bool TryResolveInside(
        string root,
        string candidateRelativePath,
        out string relativePath,
        out string fullPath,
        out string? error)
    {
        fullPath = string.Empty;
        error = null;

        if (!TryValidateRelative(candidateRelativePath, out relativePath, out error))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(root))
        {
            error = "Kein Projektordner gesetzt.";
            return false;
        }

        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var parts = new[] { rootFull }.Concat(relativePath.Split('/')).ToArray();
        var candidate = Path.GetFullPath(Path.Combine(parts));

        if (candidate.Length > MaxTotalLength)
        {
            error = $"Gesamtpfad länger als {MaxTotalLength} Zeichen ({candidate.Length}).";
            return false;
        }
        if (!candidate.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            error = "Ziel liegt außerhalb des Projektordners.";
            return false;
        }

        fullPath = candidate;
        relativePath = Path.GetRelativePath(rootFull, candidate).Replace('\\', '/');
        return true;
    }

    /// <summary>
    /// Zeichen, die in einem echten Dateinamen praktisch nie vorkommen, in Quelltext und
    /// Kommandozeilen aber ständig – sie verraten einen Kandidaten als „kein Dateiname“.
    /// </summary>
    private static readonly HashSet<char> VerdaechtigeZeichen = new(new[]
    {
        '=', '(', ')', '[', ']', '{', '}', ',', ';', '&', '$', '!', '<', '>', '*', '?', '~', '^', '%'
    });

    /// <summary>
    /// Formprüfung für einen Namen aus <em>fremder</em> Quelle (DOM-Beschriftung, Blockkopf, nackte
    /// Zeile): Sieht das überhaupt wie ein Dateiname aus?
    /// Anlass (24.09.2026): Der Codeblock-Fanger nahm auch <c>Dispatcher.Invoke</c>,
    /// <c>BridgeHost.AufUi</c>, <c>node --check arena-bot.js</c> und <c>cmd/c ping -n 121 127.0.0.1</c>
    /// als Zieldatei – und legte damit leere Dateien im Projekt an. Verlangt werden: keine Leerzeichen,
    /// keine Steuerzeichen, keine Code-Zeichen und eine Endung aus 1–8 Buchstaben/Ziffern, die nicht
    /// wie ein Mitgliedszugriff aussieht (PascalCase wie <c>.Invoke</c>).
    /// Die Sicherheitsentscheidung trifft weiterhin <see cref="TryValidateRelative"/> – diese Prüfung
    /// ist nur die Formfrage davor.
    /// </summary>
    public static bool LooksLikeFileName(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        var s = candidate.Trim();
        if (s.Length > MaxTotalLength)
        {
            return false;
        }

        foreach (var c in s)
        {
            if (char.IsWhiteSpace(c) || c < ' ' || VerdaechtigeZeichen.Contains(c))
            {
                return false;
            }
        }

        var punkt = s.LastIndexOf('.');
        if (punkt <= 0 || punkt == s.Length - 1)
        {
            return false;
        }

        var endung = s[(punkt + 1)..];
        if (endung.Length is 0 or > 8)
        {
            return false;
        }
        foreach (var c in endung)
        {
            if (!char.IsAsciiLetterOrDigit(c))
            {
                return false;
            }
        }

        // Mitgliedszugriffe aus Quelltext enden auf ein Wort in PascalCase: .Invoke, .AufUi, .ReadAllText.
        // Dateitypen sind klein (.py, .csproj) oder durchgehend groß (.JPG) – aber nicht gemischt.
        return !IstPascalCaseWort(endung);
    }

    /// <summary>Ein Wort wie <c>Invoke</c>, <c>AufUi</c> oder <c>ReadAllText</c> – groß beginnend, sonst nur Buchstaben.</summary>
    private static bool IstPascalCaseWort(string wort)
    {
        if (!char.IsAsciiLetter(wort[0]) || !char.IsUpper(wort[0]))
        {
            return false;
        }
        for (var i = 1; i < wort.Length; i++)
        {
            if (!char.IsAsciiLetter(wort[i]))
            {
                return false;
            }
        }

        // Gemischte Schreibweise ist das Kennzeichen eines Namens aus Quelltext (.Invoke, .AufUi).
        // Durchgehend groß geschriebene Endungen sind Dateitypen und bleiben erlaubt (.JPG, .MD).
        return wort.Any(char.IsAsciiLetterLower);
    }

    /// <summary>Sauberert Rohtext zu einem Kandidaten für einen relativen Pfad (Anzeige + Import).
    /// Entfernt Einbettung (Markdown, Kommentarzeichen, Label-Präfix) – aber <em>keine</em>
    /// problematischen Anteile wie <c>..</c>: die sollen sichtbar abgelehnt und nicht still
    /// „repariert“ werden. Die Sicherheitsentscheidung trifft <see cref="TryValidateRelative"/>.</summary>
    public static string? CleanCandidate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var s = raw.Trim();

        // Kommentarzeichen am Anfang:  //  #  /*  <!--  --  ;  *
        for (var again = true; again;)
        {
            s = s.TrimStart();
            again = false;
            foreach (var marker in CommentOpeners)
            {
                if (s.StartsWith(marker, StringComparison.Ordinal) && s.Length > marker.Length)
                {
                    s = s[marker.Length..];
                    again = true;
                    break;
                }
            }
        }

        // Markdown-Umrandlung: **fett**, __fett__, `code`
        s = s.Replace("**", string.Empty, StringComparison.Ordinal)
             .Replace("__", string.Empty, StringComparison.Ordinal);
        s = s.Trim('`', '*', '\'', '"', ' ', '\t', '•', '>', '<', '|');

        // Label-Präfix "path: ", "datei: ", "Save to: " (nur bei kurzem Wort-Label)
        var colon = s.IndexOf(": ", StringComparison.Ordinal);
        if (colon is > 0 and <= 24)
        {
            var label = s[..colon].Trim();
            if (label.Length > 0 && label.All(c => char.IsLetterOrDigit(c) || c is ' ' or '_' or '-'))
            {
                s = s[(colon + 2)..].Trim();
            }
        }

        // Geschlossene Kommentar-Marker am Ende: "… */", "… -->"
        for (var again = true; again;)
        {
            s = s.TrimEnd(' ', ';', ',');
            again = false;
            if (s.EndsWith("*/", StringComparison.Ordinal))
            {
                s = s[..^2];
                again = true;
            }
            else if (s.EndsWith("-->", StringComparison.Ordinal))
            {
                s = s[..^3];
                again = true;
            }
        }

        s = s.Trim();
        if (s.StartsWith("./", StringComparison.Ordinal))
        {
            s = s[2..].TrimStart();
        }
        s = s.TrimEnd(' ', ',', ';', ')', ']');
        if (s.EndsWith('.') && !s.EndsWith("..", StringComparison.Ordinal))
        {
            s = s[..^1]; // Satzende in Prosa, kein Teil des Namens
        }
        s = s.Replace('\\', '/');

        if (s.Length == 0 || !s.Any(char.IsLetterOrDigit))
        {
            return null;
        }

        return s;
    }

    private static readonly string[] CommentOpeners = { "//", "/*", "<!--", "--", "#", ";" };

    private static bool IsReserved(string segment)
    {
        var name = segment;
        var dot = name.IndexOf('.');
        if (dot > 0)
        {
            name = name[..dot];
        }
        return ReservedNames.Contains(name);
    }
}
