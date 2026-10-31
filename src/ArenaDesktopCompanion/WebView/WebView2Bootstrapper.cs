using System.IO;
using ArenaDesktopCompanion.Core.Diagnostics;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ArenaDesktopCompanion.WebView;

public sealed record WebView2BootstrapResult(bool Success, string? ErrorMessage, string UserDataFolder);

/// <summary>
/// Initialisiert WebView2 so, dass das Browser-Profil (und damit der Login) außerhalb des
/// Build-Ordners liegt. Wichtig: <c>Source</c> darf im XAML nicht gesetzt sein – das Control
/// würde dann implizit mit der Default-Umgebung initialisiert und die Wahl des Profildordners
/// wäre wertlos.
/// </summary>
public static class WebView2Bootstrapper
{
    public const string StartUrl = "https://arena.ai/";

    public static string DefaultUserDataFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AXON", "RC30", "Profiles");

    /// <summary>
    /// Prueft vorab, ob die Runtime ueberhaupt da ist – und zwar ueber die Versionssuche
    /// (der naive Test auf …\\Application\\msedgewebview2.exe findet sie nie, die Exe liegt
    /// einen Ordner tiefer). Nur damit der Browser-Ausfall erklaert werden kann, statt dass
    /// das Control eine unlesbare Reflection-Exception wirft.
    /// </summary>
    public static string? ProbeRuntime()
        => WebView2RuntimeLocator.FindRuntimeExe(WebView2RuntimeLocator.DefaultRoots(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetEnvironmentVariable("ProgramFiles(x86)")));

    /// <summary>Erzeugt das Control abgestuetzt. Scheitert der Type-Loader, kommt der Grund zurueck,
    /// statt dass die XAML-Nachrichtenkette das Fenster beendet.</summary>
    public static (WebView2? Browser, string? Error) CreateBrowser(FileLogger? logger = null)
    {
        try
        {
            return (new WebView2(), null);
        }
        catch (Exception ex)
        {
            var reason = ex.ShortMessage();
            logger?.Write("ERROR", $"WebView2-Control konnte nicht erzeugt werden: {reason}");
            return (null, reason);
        }
    }

    private static readonly Dictionary<string, Task<CoreWebView2Environment>> Environments = new(StringComparer.OrdinalIgnoreCase);
    public static Task<CoreWebView2Environment> UmgebungAsync(FileLogger? logger = null, string? userDataFolder = null)
    {
        var folder = Path.GetFullPath(userDataFolder ?? DefaultUserDataFolder);
        lock (Environments)
        {
            if (Environments.TryGetValue(folder, out var existing)) return existing;
            Directory.CreateDirectory(folder);
            var created = CoreWebView2Environment.CreateAsync(null, folder, null);
            Environments.Add(folder, created);
            return created;
        }
    }

    public static async Task<WebView2BootstrapResult> InitializeAsync(
        WebView2 control,
        FileLogger? logger = null,
        string? userDataFolder = null,
        string startUrl = StartUrl,
        Func<CoreWebView2,Task>? beforeNavigate = null)
    {
        var folder = userDataFolder ?? DefaultUserDataFolder;

        try
        {
            var environment = await UmgebungAsync(logger, folder);

            await control.EnsureCoreWebView2Async(environment);

            var core = control.CoreWebView2;
            if (core is null)
            {
                return new WebView2BootstrapResult(false, "CoreWebView2 wurde nicht erstellt.", folder);
            }

            var settings = core.Settings;
            settings.IsStatusBarEnabled = false;
            settings.AreDefaultContextMenusEnabled = true;
            settings.IsZoomControlEnabled = true;
            // DevTools bleiben an: damit lässt sich die DOM-Struktur von arena.ai prüfen,
            // ohne die der Codeblock-Fanger nicht sinnvoll einstellbar ist.
            settings.AreDevToolsEnabled = true;

            if(beforeNavigate is not null)await beforeNavigate(core);

            if (!string.IsNullOrWhiteSpace(startUrl))
            {
                core.Navigate(startUrl);
            }

            logger?.Write("INFO", $"WebView2 bereit (Profil: {folder})");
            return new WebView2BootstrapResult(true, null, folder);
        }
        catch (WebView2RuntimeNotFoundException ex)
        {
            logger?.Write("ERROR", $"WebView2-Runtime fehlt: {ex.Message}");
            return new WebView2BootstrapResult(false, WebView2RuntimeLocator.MissingMessage(), folder);
        }
        catch (Exception ex)
        {
            logger?.Write("ERROR", $"WebView2-Initialisierung: {ex}");
            return new WebView2BootstrapResult(false, ex.Message, folder);
        }
    }
}
