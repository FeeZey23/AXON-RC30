using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace ArenaDesktopCompanion.Core.Diagnostics;

/// <summary>
/// Aus einer Exception-Kette das machen, was in einem Dialog steht: die unterste Ursache.
/// Der Standardfall ist hier nicht theoretisch – eine fehlgeschlagene XAML-/Control-Erzeugung
/// wirft <c>TargetInvocationException</c> bzw. <c>XamlParseException</c>, und deren eigene
/// Meldung („Exception has been thrown by the target of an invocation.“) ist für jeden
/// Nutzer wertlos. Die Ursache steckt eine oder zwei Ebenen tiefer.
/// </summary>
public static class ExceptionFormatting
{
    public const int MaxLineLength = 320;

    /// <summary>Die tiefste Exception der Kette (die mit der eigentlichen Aussage).</summary>
    public static Exception RootCause(this Exception error)
    {
        var current = error;
        var guard = 0;
        while (current.InnerException is not null && guard++ < 16)
        {
            current = current.InnerException;
        }
        return current;
    }

    /// <summary>
    /// Einzeilige Kurzfassung für Dialoge: „Typ der Ursache: Meldung“.
    /// Wirft nie – eine kaputte Exception-Kette darf einen Fehlerdialog nicht selbst zum Fehler machen.
    /// </summary>
    public static string ShortMessage(this Exception error)
    {
        try
        {
            var root = error.RootCause();
            var text = FirstLine(root.Message);
            var name = SimpleTypeName(root.GetType().FullName);
            var line = string.IsNullOrWhiteSpace(text) ? name : $"{name}: {text}";
            var location = XamlLocation(error);
            if (location is not null)
            {
                line += $" (in {Path.GetFileName(SourceFile(error))} {location})";
            }

            var wrapped = ReferenceEquals(root, error) ? string.Empty : FirstLine(error.Message);
            var outer = ReferenceEquals(root, error) ? string.Empty : FirstLine(error.Message);
            if (!string.IsNullOrWhiteSpace(outer)
                && !outer.Contains(text, StringComparison.OrdinalIgnoreCase)
                && !outer.StartsWith("Exception has been thrown", StringComparison.OrdinalIgnoreCase))
            {
                line += $" (äußere: {outer})";
            }
            return line.Length <= MaxLineLength ? line : line[..MaxLineLength] + "…";
        }
        catch (Exception)
        {
            return "Unbekannter Fehler (Details in der Logdatei).";
        }
    }

    /// <summary>
    /// Fundstelle aus einer XAML-Ausnahme: Zeile/Position. bewusst per Reflexion – der Core darf
    /// keine WindowsBase-/PresentationFramework-Referenz bekommen (Architekturtest 4.7!).
    /// Ohne diese Angabe ist ein XAML-Fehler praktisch unfindbar. die BAML meldet immer "line 1".
    /// </summary>
    public static string? XamlLocation(Exception error)
    {
        var current = error;
        var guard = 0;
        while (current is not null && guard++ < 16)
        {
            var type = current.GetType();
            if (type.Name.Contains("XamlParseException", StringComparison.Ordinal)
                || type.Name.Contains("ParseException", StringComparison.Ordinal))
            {
                var line = ReadIntProperty(current, "LineNumber");
                var position = ReadIntProperty(current, "LinePosition");
                if (line is > 0)
                {
                    return position is > 0 ? $"Zeile {line}, Position {position}" : $"Zeile {line}";
                }
            }
            current = SafeInner(current);
        }
        return null;
    }

    private static Exception? SafeInner(Exception error)
    {
        try { return error.InnerException; }
        catch (Exception) { return null; }
    }

    private static int? ReadIntProperty(object target, string name)
    {
        try
        {
            var property = target.GetType().GetProperty(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            var value = property?.GetValue(target);
            return value is int i ? i : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Wie viele Fundstellen der Absturzdialog zeigt (mehr passt nicht in ein Dialogfeld).</summary>
    public const int MaxFrames = 3;

    /// <summary>Kennung unseres Codes in Stack-Zeilen – Core und UI tragen beide diesen Namensraum.</summary>
    private const string OurNamespace = "ArenaDesktopCompanion";

    /// <summary>Obergrenze für die Muster (wie in <c>CodeBlockParser</c>; Regex-Timeout statt GeneratedRegex, siehe N-3).</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Eine Stack-Zeile: „bei/at Methode(Argumente) in Datei:Zeile N“. Datei und Zeile fehlen in
    /// Release-Builds ohne PDB – deshalb sind sie optional, der Methodenname nicht.
    /// </summary>
    private static readonly Regex FrameLine = new(
        @"^\s*(?:bei|at)\s+(?<method>.+?)(?:\((?<args>[^)]*)\))?(?:\s+in\s+(?<file>.+?):(?:line|Zeile)\s+(?<line>\d+))?\s*$",
        RegexOptions.CultureInvariant, Timeout);

    private static readonly Regex LambdaInside = new(
        @"<(?<name>[A-Za-z_][A-Za-z0-9_]*)>", RegexOptions.CultureInvariant, Timeout);

    /// <summary>
    /// Die ersten Stack-Zeilen aus unserem Code als „Datei.cs:118 · Methode“. Genau das fehlte bisher:
    /// der Dialog nannte nur „NullReferenceException“, und die Reparatur war Raten (Fund N-20).
    /// Gesucht wird in der ganzen Kette, weil die eigentliche Stelle oft in der inneren Ausnahme steht.
    /// </summary>
    public static IReadOnlyList<string> TopFrames(Exception error, int max = MaxFrames)
    {
        var frames = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            var current = error;
            var guard = 0;
            while (current is not null && guard++ < 16 && frames.Count < max)
            {
                foreach (var raw in (current.StackTrace ?? string.Empty).Split('\n'))
                {
                    var match = FrameLine.Match(raw.TrimEnd());
                    if (!match.Success)
                    {
                        continue;
                    }

                    var method = match.Groups["method"].Value;
                    if (method.IndexOf(OurNamespace, StringComparison.Ordinal) < 0)
                    {
                        continue;   // nur unser Code – fremde Frames helfen beim Reparieren nicht
                    }

                    var name = ShortMethodName(method);
                    var file = match.Groups["file"].Success ? Path.GetFileName(match.Groups["file"].Value) : null;
                    var entry = file is null
                        ? name
                        : $"{file}:{match.Groups["line"].Value} · {name}";
                    if (seen.Add(entry) && frames.Count < max)
                    {
                        frames.Add(entry);
                    }
                }

                current = SafeInner(current);
            }
        }
        catch (Exception)
        {
            // Auch eine kaputte Stack-Eigenschaft darf den Fehlerdialog nicht selbst zum Fehler machen.
        }

        return frames;
    }

    /// <summary>„ArenaDesktopCompanion.MainWindow.&lt;MainWindow_Loaded&gt;b__12_0(String)“ → „MainWindow_Loaded“.</summary>
    private static string ShortMethodName(string method)
    {
        var name = method.Trim();
        var parenthesis = name.IndexOf('(');
        if (parenthesis > 0)
        {
            name = name[..parenthesis];
        }

        var inner = LambdaInside.Match(name);
        if (inner.Success)
        {
            return inner.Groups["name"].Value;   // Lambdas lesbar machen
        }

        var lastDot = name.LastIndexOf('.');
        return lastDot >= 0 && lastDot < name.Length - 1 ? name[(lastDot + 1)..] : name;
    }

    /// <summary>
    /// Der Text des Absturzdialogs: Ursache, Fundort in unserem Code, Logpfad. Bewusst im Core,
    /// damit er testbar ist (ohne Windows) und der Dialog im Fenster und im Protokoll dasselbe sagt.
    /// </summary>
    public static string CrashDialogText(Exception error, string? logFile, int maxFrames = MaxFrames)
    {
        var sb = new StringBuilder();
        sb.Append("Die App hat einen unerwarteten Fehler erlebt und wird beendet.").Append('\n').Append('\n');
        sb.Append(error.ShortMessage());

        var frames = TopFrames(error, maxFrames);
        if (frames.Count > 0)
        {
            sb.Append("\n\nFundstelle (unser Code):");
            foreach (var frame in frames)
            {
                sb.Append("\n  · ").Append(frame);
            }
        }

        sb.Append(logFile is null
            ? "\n\nEin Log konnte nicht geschrieben werden."
            : $"\n\nEinzelheiten:\n{logFile}");
        return sb.ToString();
    }

    /// <summary>Mehrzeiliger, vollständiger Bericht für die Logdatei.</summary>
    public static string FullReport(Exception error, string area)
    {
        var sb = new StringBuilder();
        sb.Append("UNERWARTETER FEHLER in ").Append(area).Append('\n');
        sb.Append("Zeit: ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append('\n');
        var depth = 0;
        var current = error;
        while (current is not null && depth++ < 12)
        {
            sb.Append(new string(' ', depth)).Append(current.GetType().FullName).Append(": ")
              .Append(current.Message).Append('\n');
            if (!string.IsNullOrEmpty(current.StackTrace))
            {
                sb.Append(current.StackTrace).Append('\n');
            }
            if (current is System.Reflection.TargetInvocationException tie && tie.InnerException is null)
            {
                sb.Append("  (keine innere Exception – die Reflexionsaufruf-Ziele melden nichts genauer)\n");
            }
            current = current.InnerException;
        }
        return sb.ToString();
    }

    /// <summary>Dateiname aus einer StackFrame-Zeile, falls vorhanden („… in C:\…\MainWindow.xaml:line 1“).</summary>
    private static string SourceFile(Exception error)
    {
        var stack = error.RootCause().StackTrace ?? error.StackTrace ?? string.Empty;
        foreach (var raw in stack.Split('\n'))
        {
            var line = raw.Trim();
            var index = line.IndexOf(".xaml", StringComparison.OrdinalIgnoreCase);
            if (index > 0)
            {
                var start = line.LastIndexOfAny(new[] { ' ', '\\', '/' }, index);
                return line[(start + 1)..(index + 5)];
            }
        }
        return "XAML";
    }

    private static string FirstLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }
        var line = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')[0].Trim();
        // XamlParseException hängt Zeilennummer an – die ist genau helpful, also behalten.
        return line.Length <= MaxLineLength ? line : line[..MaxLineLength] + "…";
    }

    private static string SimpleTypeName(string? full)
    {
        if (string.IsNullOrEmpty(full))
        {
            return "Exception";
        }
        var idx = full.LastIndexOf('.');
        return idx > 0 && idx < full.Length - 1 ? full[(idx + 1)..] : full;
    }
}
