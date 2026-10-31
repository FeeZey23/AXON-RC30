using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ArenaDesktopCompanion.Infrastructure;

/// <summary>
/// Färbt die Windows-Titelleiste dunkel. Ohne das sitzt über der dunklen Oberfläche
/// ein helles Fensterband – die eine Stelle, die sich nicht per XAML einfärben lässt.
/// Der Aufruf ist bewusst fehlertolerant: auf älteren Windows-Builds (DWM-Attribut
/// unbekannt) bleibt die Leiste hell, alles andere funktioniert weiter.
/// </summary>
public static class DarkTitleBar
{
    /// <summary>Attributname ab Windows 10 1809.</summary>
    private const int DwmwaUseImmersiveDarkMode = 20;

    /// <summary>Vorgängernummer (Windows 10 1803/1809-Vorstände, Insider-Builds).</summary>
    private const int DwmwaUseImmersiveDarkModeLegacy = 19;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Aus <c>OnSourceInitialized</c> aufrufen – dann steht das Handle bereits fest.</summary>
    public static void Apply(Window window)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            var enabled = 1;
            if (DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int)) != 0)
            {
                // Nur wenn das neue Attribut nicht greift, die Nummer davor versuchen.
                DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkModeLegacy, ref enabled, sizeof(int));
            }
        }
        catch (Exception)
        {
            // Kein Grund, deswegen zu scheitern – die Titelleiste ist Kosmetik.
        }
    }
}
