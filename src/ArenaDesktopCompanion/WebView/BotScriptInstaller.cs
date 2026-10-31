using System.IO;
using System.Reflection;
using ArenaDesktopCompanion.Core.Diagnostics;
using Microsoft.Web.WebView2.Core;

namespace ArenaDesktopCompanion.WebView;

/// <summary>
/// Bringt <c>Assets/arena-bot.js</c> in die Seite – über das Dokument-Erzeugt-Event, damit es
/// vor den Seitenskripten und in allen Frames läuft und Reloads übersteht. Ein bloßes
/// <c>ExecuteScriptAsync</c> nach <c>NavigationCompleted</c> verliert alles, was vor dem
/// Ereignis gerendert wurde, und stirbt lautlos, wenn <c>document.body</c> noch null ist.
/// </summary>
public sealed class BotScriptInstaller
{
    private const string ResourceName = "ArenaDesktopCompanion.Assets.arena-bot.js";

    private string? _scriptSource;
    private string? _registrationId;
    private bool _registered;

    public BotScriptInstaller(FileLogger? logger = null) => _logger = logger;

    private readonly FileLogger? _logger;

    public bool IsRegistered => _registered;

    /// <summary>Text aus der Embedded-Resource (bewusst keine C#-String-Konstante im Code).</summary>
    public static string LoadScript()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Eingebettete Ressource '{ResourceName}' fehlt.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public async Task<bool> RegisterAsync(CoreWebView2 core, CancellationToken cancellationToken = default)
    {
        if (_registered)
        {
            return true;
        }

        _scriptSource ??= LoadScript();

        try
        {
            var result = await core.AddScriptToExecuteOnDocumentCreatedAsync(_scriptSource);
            if (string.IsNullOrEmpty(result))
            {
                _logger?.Write("WARN", "Bot-Skript konnte nicht registriert werden (leere Id).");
                return false;
            }
            _registrationId = result;
            _registered = true;
            _logger?.Write("INFO", $"Bot-Skript registriert (Id {_registrationId}).");
            return true;
        }
        catch (Exception ex)
        {
            _logger?.Write("ERROR", $"Bot-Skript-Registrierung: {ex.Message}");
            return false;
        }
    }

    /// <summary>Abschalten = Registrierung entfernen UND schon installierte Instanz im Dokument stilllegen.</summary>
    public async Task UnregisterAsync(CoreWebView2 core, CancellationToken cancellationToken = default)
    {
        if (!_registered)
        {
            return;
        }

        try
        {
            if (!string.IsNullOrEmpty(_registrationId))
            {
                // Die API ist hier synchron (RemoveScriptToExecuteOnDocumentCreated) – kein Async-Suffix.
                core.RemoveScriptToExecuteOnDocumentCreated(_registrationId);
            }
            // Der Bot in der geladenen Seite meldet sich über dieses Flag ab.
            await core.ExecuteScriptAsync("window.__arenaCompanionBot = 'disabled';");
        }
        catch (Exception ex)
        {
            _logger?.Write("WARN", $"Bot-Deinstallation: {ex.Message}");
        }
        finally
        {
            _registered = false;
            _registrationId = null;
        }
    }

    /// <summary>Lebenszeichen aus der laufenden Seite – für die Statusanzeige im Fenster.</summary>
    public async Task<bool> ProbeAsync(CoreWebView2 core)
    {
        try
        {
            var result = await core.ExecuteScriptAsync("window.__arenaCompanionBot === true");
            return result.Trim() is "true";
        }
        catch (Exception ex)
        {
            _logger?.Write("WARN", $"Bot-Lebenszeichen: {ex.Message}");
            return false;
        }
    }

    /// <summary>Nachträglicher Anstoß für die bereits offene Seite (ohne Reload).</summary>
    public async Task<bool> ExecuteNowAsync(CoreWebView2 core)
    {
        try
        {
            _scriptSource ??= LoadScript();

            // ExecuteScriptWithResultAsync statt ExecuteScriptAsync: nur so sieht man eine
            // JS-Ausnahme (z. B. blockiertes Skript) statt nur "undefined" zu erahnen.
            var result = await core.ExecuteScriptWithResultAsync(_scriptSource);
            if (result is null)
            {
                _logger?.Write("WARN", "Sofort-Ausführung ohne Ergebnis (Runtime zu alt?).");
                return true;
            }
            if (!result.Succeeded)
            {
                var text = result.Exception?.Message ?? "unbekannter Skriptfehler";
                _logger?.Write("WARN", $"Sofort-Ausführung meldete: {text}");
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            _logger?.Write("ERROR", $"Sofort-Ausführung: {ex.Message}");
            return false;
        }
    }
}
