using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using ArenaDesktopCompanion.Core.Diagnostics;

namespace ArenaDesktopCompanion;

/// <summary>
/// Basisklasse hier explizit ausgeschrieben: würde jemand später <c>UseWindowsForms</c>
/// wieder einschalten, entsteht sonst erneut CS0104 („Application ist mehrdeutig“).
/// </summary>
public partial class App : System.Windows.Application
{
    public static AppServices Services { get; private set; } = null!;

    /// <summary>Notnagel, falls schon der Aufbau von <see cref="Services"/> scheitert: ohne ihn
    /// gäbe es dann weder Protokoll noch Dialog, und die App verschwände kommentarlos (N-20).</summary>
    private static CrashReporter? _notfallCrash;

    private bool _crashDialogShown;

    private static CrashReporter Crash =>
        Services is not null ? Services.Crash : _notfallCrash ??= new CrashReporter(new FileLogger(AppServices.DefaultLogDirectory));

    protected override void OnStartup(StartupEventArgs e)
    {
        // Zuerst die Handler, dann die Dienste: ein Fehler beim Aufbau darf nicht mehr
        // unsichtbar bleiben. Die Handler selbst kommen ohne Services aus (siehe Crash).
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        try
        {
            Services = new AppServices();
        }
        catch (Exception ex)
        {
            ShowCrashDialog(ex, Crash.ReportToFile("Start (Dienste)", ex));
            Shutdown(1);
            return;
        }

        // Einzelinstanz: zwei Fenster, ein Profildatensatz und zwei Schreiber in dasselbe Projekt
        // sind keine gute Idee (P2-7).
        if (!Services.Instance.IsFirstInstance)
        {
            // Ohne Fenster (rc24): Der zweite Start beendet sich still und schreibt eine Zeile ins
            // Protokoll – das laufende Fenster steht ohnehin in der Taskleiste. Eine Frage gibt es nur
            // noch, wenn das Programm abstürzt (Absturzmeldung unten).
            Services.Logger.Write("INFO", "Zweiter Start – es läuft schon eine Instanz. Diese beendet sich.");
            Services.Dispose();
            Shutdown();
            return;
        }

        Services.Logger.Write("INFO", "App startet.");
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            // Services kann fehlen, wenn der Aufbau abgebrochen ist (siehe OnStartup).
            Services?.Logger.Write("INFO", $"App beendet (ExitCode {e.ApplicationExitCode}).");
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
        finally
        {
            Services?.Dispose();
            base.OnExit(e);
        }
    }

    internal static void ReportSmokeFailure(Exception ex)
    {
        if(Environment.GetEnvironmentVariable("AXON_RC30_SMOKE")!="1")return;
        var path=Environment.GetEnvironmentVariable("AXON_RC30_SMOKE_OUTPUT");
        if(path is null)return;
        try{System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);System.IO.File.WriteAllText(path,System.Text.Json.JsonSerializer.Serialize(new{Passed=false,Checks=Array.Empty<string>(),Error=ex.ToString()}));}catch{ /* preserve original failure */ }
    }
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var logFile = Crash.ReportToFile("UI-Thread", e.Exception);
        if(Environment.GetEnvironmentVariable("AXON_RC30_SMOKE")=="1"){ReportSmokeFailure(e.Exception);e.Handled=true;Shutdown(2);return;}
        ShowCrashDialog(e.Exception, logFile);
        // Nicht verschlucken: ein Fehler im UI-Loop soll das Fenster beenden, nicht korrupt weiterlaufen lassen.
        e.Handled = false;
    }

    private void OnDomainUnhandledException(object? sender, UnhandledExceptionEventArgs args)
    {
        if (args.ExceptionObject is Exception ex)
        {
            Crash.ReportToFile($"Hintergrund (beendet={args.IsTerminating})", ex);ReportSmokeFailure(ex);
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Crash.ReportToFile("Task ohne await", e.Exception);
        e.SetObserved();   // Der Finalizer-Absturz wäre schlimmer als der verlorene Fehler.
    }

    private void ShowCrashDialog(Exception error, string? logFile)
    {
        if(Environment.GetEnvironmentVariable("AXON_RC30_SMOKE")=="1"){ReportSmokeFailure(error);return;}
        if (_crashDialogShown)
        {
            return;
        }
        _crashDialogShown = true;

        // Wichtig: die URSACHE und die FUNDSTELLE zeigen. Eine TargetInvocation-/XamlParseException
        // meldet sonst nur "Exception has been thrown by the target of an invocation." – und ohne
        // Datei:Zeile ist jede Reparatur Raten (Fund N-20, Text kommt aus dem Core und ist getestet).
        MessageBox.Show(
            ExceptionFormatting.CrashDialogText(error, logFile),
            "AXON RC30 – Fehler",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}
