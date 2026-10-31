using System.IO;
using ArenaDesktopCompanion.Core.Diagnostics;
using ArenaDesktopCompanion.Core.Sync;
using Microsoft.Web.WebView2.Core;

namespace ArenaDesktopCompanion.WebView;

/// <summary>
/// Leitet Downloads von arena.ai in die Inbox um, statt sie ungeordnet in den
/// Downloads-Ordner – oder ungeprüft in den Projektordner – zu legen.
/// Kollisionsnamen, Fehler und Fortschritt werden protokolliert.
/// </summary>
public sealed class DownloadRouter
{
    private readonly FileLogger? _logger;
    private int _lastPercent = -1;

    public DownloadRouter(FileLogger? logger = null) => _logger = logger;

    public event Action<string>? Status;

    public string InboxDirectory { get; private set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;

    public void Bind(string projectRoot)
        => InboxDirectory = Path.Combine(Path.GetFullPath(projectRoot), InboxProcessor.InboxFolderName);

    /// <summary>
    /// Die Bindung lösen (Reiter ohne Projektordner). Danach greift der Router nicht mehr ein –
    /// <c>OnDownloadStarting</c> lässt Downloads dann wieder an ihren Standardort laufen.
    /// </summary>
    public void Entbinden() => InboxDirectory = string.Empty;

    /// <summary>Aufruf aus <c>CoreWebView2.DownloadStarting</c> (kommt auf dem UI-Thread).</summary>
    public void OnDownloadStarting(CoreWebView2DownloadStartingEventArgs e)
    {
        if (!IsEnabled || string.IsNullOrEmpty(InboxDirectory))
        {
            return; // deaktiviert -> WebView2 macht sein normales Verhalten
        }

        try
        {
            Directory.CreateDirectory(InboxDirectory);

            var operation = e.DownloadOperation;
            var suggested = ResolveSuggestedName(e);
            var target = MakeUnique(Path.Combine(InboxDirectory, suggested));

            e.ResultFilePath = target;
            // e.Handled bleibt false: WebView2 schreibt, wir haben nur das Ziel gesetzt.

            var fileName = Path.GetFileName(target);
            Status?.Invoke($"Download → Inbox: {fileName}");
            _logger?.Write("INFO", $"Download umgeleitet nach {target}");

            // CoreWebView2DownloadOperation hat KEIN Completed-Event – fertig ist ein Download
            // erst, wenn StateChanged auf Completed (oder Interrupted) springt.
            operation.StateChanged += (_, _) =>
            {
                switch (operation.State)
                {
                    case CoreWebView2DownloadState.Completed:
                        Status?.Invoke($"✔ Download fertig: {fileName}");
                        _logger?.Write("INFO", $"Download fertig: {target}");
                        break;
                    case CoreWebView2DownloadState.Interrupted:
                        Status?.Invoke($"⚠ Download abgebrochen: {fileName} ({operation.InterruptReason})");
                        _logger?.Write("WARN", $"Download unterbrochen ({operation.InterruptReason}): {target}");
                        break;
                }
            };

            operation.BytesReceivedChanged += (_, _) => ReportProgress(operation, fileName);
        }
        catch (Exception ex)
        {
            _logger?.Write("ERROR", $"Download-Umleitung: {ex.Message}");
            Status?.Invoke($"Download konnte nicht umgeleitet werden: {ex.Message}");
        }
    }

    private void ReportProgress(CoreWebView2DownloadOperation operation, string fileName)
    {
        var total = operation.TotalBytesToReceive;
        if (total is null or 0)
        {
            return;
        }

        var percent = (int)Math.Min(100d, (double)operation.BytesReceived * 100 / total.Value);
        if (percent == _lastPercent || percent is < 25)
        {
            return;
        }

        _lastPercent = percent;
        Status?.Invoke($"Download {fileName}: {percent} %");
    }

    private static string ResolveSuggestedName(CoreWebView2DownloadStartingEventArgs e)
    {
        var name = TryName(e.ResultFilePath) ?? TryName(e.DownloadOperation?.Uri) ?? TryName(e.DownloadOperation?.ResultFilePath);
        return name ?? $"arena-download-{DateTime.Now:yyyyMMdd-HHmmss}.bin";
    }

    private static string? TryName(string? pathOrUri)
    {
        if (string.IsNullOrWhiteSpace(pathOrUri))
        {
            return null;
        }

        var candidate = pathOrUri;
        if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.AbsolutePath))
        {
            candidate = uri.AbsolutePath;
        }

        var name = Path.GetFileName(Uri.UnescapeDataString(candidate).TrimEnd('/'));
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        // Nur der Name wandert weiter; die Pfadregeln der Inbox greifen beim Import.
        return PathSafety.TryValidateRelative(name, out var clean, out _) ? clean : MakeSafeFallback(name);
    }

    private static string MakeSafeFallback(string name)
    {
        var invalid = new[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*' };
        var sb = new System.Text.StringBuilder(name.Length);
        foreach (var c in name)
        {
            sb.Append(c < ' ' || invalid.Contains(c) ? '_' : c);
        }
        var cleaned = sb.ToString().Trim('.', ' ');
        return cleaned.Length == 0 ? "arena-download.bin" : cleaned;
    }

    private static string MakeUnique(string target)
    {
        if (!File.Exists(target))
        {
            return target;
        }

        var dir = Path.GetDirectoryName(target)!;
        var stem = Path.GetFileNameWithoutExtension(target);
        var ext = Path.GetExtension(target);
        for (var i = 1; i < 1000; i++)
        {
            var candidate = Path.Combine(dir, $"{stem} ({i}){ext}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }
        return Path.Combine(dir, $"{stem}-{Guid.NewGuid():N}{ext}");
    }
}
