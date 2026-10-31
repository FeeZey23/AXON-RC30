namespace ArenaDesktopCompanion.Core.Diagnostics;

/// <summary>
/// Zwei Instanzen derselben App würden sich denselben WebView2-Profildatensatz streitig machen
/// (zweite startet dann nicht sauber) und beide könnten in denselben Projektordner schreiben.
/// Deshalb: eine Instanz, die zweite meldet und geht.
/// </summary>
public sealed class AppInstanceGuard : IDisposable
{
    private readonly Mutex _mutex;
    private bool _owned;

    public AppInstanceGuard(string appName = "ArenaDesktopCompanion")
    {
        // lokal | Präfix = pro Benutzer, ohne Namensraum-Konflikte zwischen Benutzern einer Maschine
        _mutex = new Mutex(initiallyOwned: true, name: $"Local\\{appName}.SingleInstance", createdNew: out var createdNew);
        _owned = createdNew;
    }

    public bool IsFirstInstance => _owned;

    public void Dispose()
    {
        if (!_owned)
        {
            return;
        }
        try
        {
            _mutex.ReleaseMutex();
            _owned = false;
        }
        catch (ApplicationException)
        {
            // Mutex gehört nicht uns (z. B. Absturz zuvor) – Verlassen ist trotzdem ok.
        }
        finally
        {
            _mutex.Dispose();
        }
    }
}
