using System.Text;

namespace ArenaDesktopCompanion.Core.Diagnostics;

/// <summary>
/// Letzte Hilfe, bevor der Prozess endet: vollständiger Text in die Logdatei, eine lesbare
/// Meldung für den Nutzer. Ohne das verschwindet eine WPF-App kommentarlos – genau das war P1-8.
/// </summary>
public sealed class CrashReporter
{
    private readonly FileLogger? _logger;
    private int _reported;

    public CrashReporter(FileLogger? logger) => _logger = logger;

    /// <summary>Pfad der Datei, in die der Absturz geschrieben wurde (für den Dialog).</summary>
    public string? LastLogFile { get; private set; }

    /// <summary>Ausführlich in die Logdatei. Gibt den Pfad zurück; wirft nie.</summary>
    public string? ReportToFile(string area, Exception error)
    {
        try
        {
            Interlocked.Exchange(ref _reported, 1);
            var sb = new StringBuilder();
            sb.Append("App: ").Append(GetVersion()).Append('\n');
            sb.Append("OS : ").Append(Environment.OSVersion.VersionString)
              .Append(" (64-bit: ").Append(Environment.Is64BitOperatingSystem ? "ja" : "nein").Append(")\n");
            sb.Append("Aktueller Ordner: ").Append(SafeLocation()).Append('\n');
            sb.Append(ExceptionFormatting.FullReport(error, area));
            _logger?.WriteRaw(sb.ToString());
            LastLogFile = _logger?.FilePath;
            return LastLogFile;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string SafeLocation()
    {
        try
        {
            return System.IO.Directory.GetCurrentDirectory();
        }
        catch (Exception)
        {
            return "unbekannt";
        }
    }

    private static string GetVersion()
    {
        try
        {
            var assembly = System.Reflection.Assembly.GetEntryAssembly();
            return assembly?.GetName().Version?.ToString() ?? "unbekannt";
        }
        catch (Exception)
        {
            return "unbekannt";
        }
    }
}
