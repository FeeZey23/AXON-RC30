using ArenaDesktopCompanion.Core.Diagnostics;
using System.IO;
using System.Reflection;

namespace ArenaDesktopCompanion.Core.Tests;

public class WebView2RuntimeLocatorTests
{
    private static readonly string[] Roots = { "/pf", "/pf86" };

    [Fact]
    public void Kandidatenverzeichnisse_decken_beide_Pfade_Abgelegheiten_ab()
    {
        var dirs = WebView2RuntimeLocator.CandidateDirectories(Roots);

        Assert.Contains(Path.Combine("/pf", "Microsoft", "EdgeWebView", "Application"), dirs);
        Assert.Contains(Path.Combine("/pf86", "Microsoft", "Edge", "Application"), dirs);
        Assert.Equal(4, dirs.Count);
    }

    [Fact]
    public void Findet_die_Exe_im_Versionsordner()
    {
        var app = Path.Combine("/pf86", "Microsoft", "EdgeWebView", "Application");
        var versionDirectory = Path.Combine(app, "120.0.2210.77");
        var exe = Path.Combine(versionDirectory, "msedgewebview2.exe");

        var found = WebView2RuntimeLocator.FindRuntimeExe(
            Roots,
            directoryExists: d => d == app,
            versionDirectories: _ => new[] { versionDirectory },
            fileExists: f => f == exe);

        Assert.Equal(exe, found);
    }

    [Fact]
    public void Alte_naive_Pfadannahme_waere_falsch_Glob_findet_sie_aber()
    {
        var app = Path.Combine("/pf", "Microsoft", "Edge", "Application");

        // Nur die Exe direkt im Application-Ordner (unueblich, aber als Fallback gewollt).
        var found = WebView2RuntimeLocator.FindRuntimeExe(
            Roots,
            directoryExists: d => d == app,
            versionDirectories: _ => Array.Empty<string>(),
            fileExists: f => f == Path.Combine(app, "msedgewebview2.exe"));

        Assert.Equal(Path.Combine(app, "msedgewebview2.exe"), found);
    }

    [Fact]
    public void Kein_Treffer_ergibt_null_statt_Ausnahme()
    {
        var found = WebView2RuntimeLocator.FindRuntimeExe(
            Roots,
            directoryExists: _ => false,
            versionDirectories: _ => throw new UnauthorizedAccessException("darf nicht lesen"),
            fileExists: _ => false);

        Assert.Null(found);
    }

    [Fact]
    public void Unlesbare_Application_Ordner_uiberleben_den_Aufruf()
    {
        var app = Path.Combine("/pf", "Microsoft", "EdgeWebView", "Application");

        var found = WebView2RuntimeLocator.FindRuntimeExe(
            Roots,
            directoryExists: d => d == app,
            versionDirectories: _ => throw new IOException("Zugriff verweigert"),
            fileExists: _ => false);

        Assert.Null(found);
    }

    [Fact]
    public void DefaultRoots_entfernt_leere_doppelte_Umgebungsvariablen()
    {
        var roots = WebView2RuntimeLocator.DefaultRoots("/pf", "/pf").ToList();
        Assert.Single(roots);

        Assert.Empty(WebView2RuntimeLocator.DefaultRoots(null, "   "));
        Assert.Equal(2, WebView2RuntimeLocator.DefaultRoots("/pf", "/pf86").Count());
    }

    [Fact]
    public void Fehlmeldung_nennt_Downloadort_und_triftliche_Aussage()
    {
        var message = WebView2RuntimeLocator.MissingMessage();

        Assert.Contains(WebView2RuntimeLocator.DownloadUrl, message, StringComparison.Ordinal);
        Assert.Contains("trotzdem", message, StringComparison.Ordinal);
    }
}

/// <summary>Ein Fake mit denselben Eigenschaften, die die XamlParseException hat.</summary>
