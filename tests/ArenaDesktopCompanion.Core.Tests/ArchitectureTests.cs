using ArenaDesktopCompanion.Core.Diagnostics;
using ArenaDesktopCompanion.Core.Import;
using ArenaDesktopCompanion.Core.Settings;
using ArenaDesktopCompanion.Core.Sync;
using ArenaDesktopCompanion.Core.Tests.Support;
using System.Reflection;

namespace ArenaDesktopCompanion.Core.Tests;

public class ArchitectureTests
{
    [Theory]
    [InlineData("PresentationFramework")]
    [InlineData("WindowsBase")]
    [InlineData("System.Windows.Forms")]
    [InlineData("System.Drawing")]
    [InlineData("Microsoft.Web.WebView2.Core")]
    [InlineData("Microsoft.Web.WebView2.Wpf")]
    public void Core_referenziert_kein_Windows_Umfeld(string forbidden)
    {
        var refs = typeof(SyncPolicy).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToList();
        Assert.DoesNotContain(forbidden, refs);
    }

    /// <summary>
    /// Wunsch vom 26.09.2026: „Außerdem will ich keine popupfenster, gib alle rechte frei aus ende."
    /// In den Fenstern darf deshalb kein <c>MessageBox</c> mehr stehen – Rückmeldungen laufen über
    /// Statuszeile und Protokoll. Eine Ausnahme bleibt bewusst: die Absturzmeldung beim Start
    /// (<see cref="App"/>) – ohne sie sähe man bei einem Startfehler gar nichts.
    /// </summary>
    [Fact]
    public void In_den_Fenstern_steht_kein_Popup_mehr()
    {
        var root = RepositoryRoot();
        foreach (var rel in new[]
                 {
                     Path.Combine("src", "ArenaDesktopCompanion", "MainWindow.xaml.cs"),
                     Path.Combine("src", "ArenaDesktopCompanion", "MainWindow.xaml"),
                     Path.Combine("src", "ArenaDesktopCompanion", "ConflictsWindow.xaml.cs"),
                     Path.Combine("src", "ArenaDesktopCompanion", "ConflictsWindow.xaml"),
                 })
        {
            var text = File.ReadAllText(Path.Combine(root, rel));
            Assert.DoesNotContain("MessageBox", text, StringComparison.Ordinal);
        }

        var app = File.ReadAllText(Path.Combine(root, "src", "ArenaDesktopCompanion", "App.xaml.cs"));
        Assert.Contains("MessageBox.Show(", app, StringComparison.Ordinal);
    }

    /// <summary>
    /// Seit rc24 gibt es keine Freigabefrage mehr („… gib alle rechte frei aus ende"). Was bleibt:
    /// der <b>Not-Aus</b> hält alles an, und jeder Aufruf ohne Freigabefenster steht im Protokoll.
    /// Diese Prüfung hält beides fest, damit die Rückfrage nicht durch die Hintertür zurückkommt.
    /// </summary>
    [Fact]
    public void Rc30_removes_stop_but_keeps_cancellation()
    {
        var host=File.ReadAllText(Path.Combine(RepositoryRoot(),"src","ArenaDesktopCompanion","Bridge","BridgeHost.cs"));
        Assert.DoesNotContain("_services.Settings.NotAus",host);
        Assert.Contains("CancellationToken",host);
        Assert.Contains("läuft ohne Rückfrage",host);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ArenaDesktopCompanion.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repo-Wurzel nicht gefunden.");
    }

    [Fact]
    public void Core_ist_net8_ohne_Windows_Plattformband()
    {
        var target = typeof(SyncPolicy).Assembly
            .GetCustomAttribute<System.Runtime.Versioning.TargetFrameworkAttribute>()?.FrameworkName ?? "";
        Assert.DoesNotContain("-windows", target, StringComparison.OrdinalIgnoreCase);
    }
}
