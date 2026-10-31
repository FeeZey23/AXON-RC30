using System.IO;
using ArenaDesktopCompanion.Core.Diagnostics;
using ArenaDesktopCompanion.Core.Settings;

namespace ArenaDesktopCompanion;

/// <summary>
/// Die paar Dienste, die das Fenster und die Dialoge brauchen – hier erzeugt, damit
/// der Absturz-Handler schon läuft, bevor das erste Fenster existiert.
/// </summary>
public sealed class AppServices : IDisposable
{
    /// <summary>
    /// Wo das Protokoll liegt. Statisch, weil der Absturz-Handler diese Angabe schon braucht,
    /// wenn der Aufbau der Dienste selbst fehlgeschlagen ist (Fund N-20).
    /// </summary>
    public static string DefaultLogDirectory => Path.Combine(Rc30Paths.DataRoot,"logs");

    public AppServices()
    {
        LogDirectory = DefaultLogDirectory;
        Logger = new FileLogger(LogDirectory);
        Crash = new CrashReporter(Logger);
        SettingsStore = new AppSettingsStore();
        Settings = SettingsStore.Load();
        Instance = new AppInstanceGuard(Rc30Paths.InstanceName + (Environment.GetEnvironmentVariable("AXON_RC30_SMOKE") == "1" ? ".Smoke" : ""));
    }

    public string LogDirectory { get; }
    public FileLogger Logger { get; }
    public CrashReporter Crash { get; }
    public AppSettingsStore SettingsStore { get; }
    public AppSettings Settings { get; }
    public AppInstanceGuard Instance { get; }

    public void SaveSettings()
    {
        try
        {
            SettingsStore.Save(Settings);
        }
        catch (Exception ex)
        {
            Logger.Write("WARN", "Einstellungen nicht speicherbar: " + ex.Message);
        }
    }

    public void Dispose()
    {
        Instance.Dispose();
        Logger.Dispose();
    }
}
