namespace ArenaDesktopCompanion.Core.Diagnostics;

/// <summary>
/// Findet die installierte Edge/WebView2-Runtime. Der Grund, warum es diesen Helfer überhaupt gibt:
/// die Runtime liegt NIE direkt unter <c>…\Application\msedgewebview2.exe</c>, sondern immer in einem
/// Versionsordner (<c>…\Application\120.0.2210.77\msedgewebview2.exe</c>) – ein naiver Pfadtest
/// meldet sie deshalb als fehlend, obwohl sie da ist.
/// </summary>
public static class WebView2RuntimeLocator
{
    public const string ExeName = "msedgewebview2.exe";
    public const string DownloadUrl = "https://developer.microsoft.com/microsoft-edge/webview2/";

    /// <summary>
    /// Die üblichen Wurzelordner (Program Files, Program Files (x86)) – als Parameter, damit die
    /// Methode auf jedem System testbar ist.
    /// </summary>
    public static IEnumerable<string> DefaultRoots(string? programFiles, string? programFilesX86)
    {
        var seen = new HashSet<string>(Path.DirectorySeparatorChar == '/' ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
        foreach (var root in new[] { programFilesX86, programFiles })
        {
            if (!string.IsNullOrWhiteSpace(root) && seen.Add(root))
            {
                yield return root;
            }
        }
    }

    /// <summary>Pfade, die geprüft werden (für Meldungen und Tests, ohne Dateisystem-Zugriff).</summary>
    public static IReadOnlyList<string> CandidateDirectories(IEnumerable<string> roots)
    {
        var list = new List<string>();
        foreach (var root in roots)
        {
            list.Add(Path.Combine(root, "Microsoft", "EdgeWebView", "Application"));
            list.Add(Path.Combine(root, "Microsoft", "Edge", "Application"));
        }
        return list;
    }

    /// <summary>
    /// Sucht die Runtime-Exe unter den Kandidatenverzeichnissen (ein Versionsordner tiefer).
    /// </summary>
    /// <param name="roots">Wurzelordner wie <c>C:\Program Files (x86)</c>.</param>
    /// <param name="directoryExists">Injectable, damit testbar ohne echtes Dateisystem.</param>
    /// <param name="versionDirectories">liefert die Ordner innerhalb eines Application-Verzeichnisses.</param>
    public static string? FindRuntimeExe(
        IEnumerable<string> roots,
        Func<string, bool>? directoryExists = null,
        Func<string, IEnumerable<string>>? versionDirectories = null,
        Func<string, bool>? fileExists = null)
    {
        directoryExists ??= Directory.Exists;
        fileExists ??= File.Exists;
        versionDirectories ??= static applicationDirectory => Directory.GetDirectories(applicationDirectory);

        foreach (var applicationDirectory in CandidateDirectories(roots))
        {
            if (!directoryExists(applicationDirectory))
            {
                continue;
            }

            // Ein unlesbarer Ordner (Netzlaufwerk, Rechte, OneDrive-on-Demand) darf den Suchlauf
            // nicht beenden – er ist hier nur einer von vier Kandidaten.
            IEnumerable<string> versions;
            try
            {
                versions = versionDirectories(applicationDirectory) ?? Array.Empty<string>();
            }
            catch (Exception)
            {
                continue;
            }

            foreach (var version in versions)
            {
                var candidate = Path.Combine(version, ExeName);
                if (fileExists(candidate))
                {
                    return candidate;
                }
            }

            // Einzelfall: manche Installationen legen die Exe direkt in Application\.
            try
            {
                var direct = Path.Combine(applicationDirectory, ExeName);
                if (fileExists(direct))
                {
                    return direct;
                }
            }
            catch (Exception)
            {
                // ignorieren und nächsten Kandidaten versuchen
            }
        }

        return null;
    }

    public static string MissingMessage()
        => $"Die Microsoft Edge WebView2 Runtime wurde nicht gefunden.{Environment.NewLine}{Environment.NewLine}" +
           $"Bitte installieren: {DownloadUrl}{Environment.NewLine}" +
           "Der Browser-Teil der App bleibt dann leer – Projektordner, Import, Konflikte und Protokoll funktionieren trotzdem.";
}
