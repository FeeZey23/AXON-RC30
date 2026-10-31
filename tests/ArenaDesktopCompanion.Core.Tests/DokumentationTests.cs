using System.Text.RegularExpressions;
using Xunit;

namespace ArenaDesktopCompanion.Core.Tests;

/// <summary>Maintained documentation contracts for the clean ISAS source repository.</summary>
public sealed class DokumentationTests
{
    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ArenaDesktopCompanion.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found");
    }
    private static string Read(string file) => File.ReadAllText(Path.Combine(RepositoryRoot(), file));
    private static IEnumerable<string> Docs() => Directory.EnumerateFiles(RepositoryRoot(), "*.md", SearchOption.AllDirectories)
        .Where(p => !p.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(s => s is "bin" or "obj" or ".git" or ".artifacts" or "releases"));

    [Fact]
    public void Einstieg_verlinkt_ausfuehrliche_Projektseite()
    {
        Assert.Contains("AXON RC30 (by ISAS)", Read("README.md"));
        Assert.Contains("](PROJECT.OVERVIEW.md)", Read("README.md"));
        Assert.Contains("ISAS - AXON-RC30", Read("PROJECT.OVERVIEW.md"));
    }
    [Fact]
    public void Version_im_Einstieg_entspricht_dem_Programmkern() =>
        Assert.Contains(ArenaDesktopCompanion.Core.Bridge.AppInfo.Version, Read("README.md"));

    [Fact]
    public void Alle_relativ_verlinkten_Dokumente_und_Bilder_existieren()
    {
        foreach (var doc in Docs())
        foreach (Match m in Regex.Matches(File.ReadAllText(doc), @"\]\((?<path>[^)\s]+)(?:\s+""[^""]*"")?\)"))
        {
            var path = m.Groups["path"].Value.Split('#')[0];
            if (path.Length == 0 || Uri.TryCreate(path, UriKind.Absolute, out _)) continue;
            var full = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(doc)!, Uri.UnescapeDataString(path)));
            Assert.True(full.StartsWith(RepositoryRoot() + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), full);
            Assert.True(File.Exists(full) || Directory.Exists(full), "Missing link: " + full);
        }
    }
    [Fact]
    public void Abbildungen_sind_lokal_und_als_Schema_oder_Bildschirmaufnahme_unterscheidbar()
    {
        foreach (var image in new[] {"architecture.svg", "session-isolation.svg", "connection-sequence.svg", "storage-map.svg", "ui-layout.svg", "application-status-redacted.png", "application-icon.png"})
            Assert.True(File.Exists(Path.Combine(RepositoryRoot(), "docs", "images", image)), image);
        Assert.Contains("kein generiertes Produktfoto", Read("PROJECT.OVERVIEW.md"));
        Assert.Contains("Dokumentationsgrafik", Read("docs/images/ui-layout.svg"));
    }
    [Fact]
    public void Windowsanleitung_verweist_nur_auf_vorhandene_Wartungsskripte()
    {
        foreach (var doc in Docs())
        foreach (Match m in Regex.Matches(File.ReadAllText(doc), @"scripts[\\/](?<name>[A-Za-z0-9.\-]+\.ps1)"))
            Assert.True(File.Exists(Path.Combine(RepositoryRoot(), "scripts", m.Groups["name"].Value)), m.Value);
    }
    [Fact]
    public void Solution_enthaelt_auch_die_Connectorintegration()
    {
        var sln = Read("ArenaDesktopCompanion.sln");
        Assert.Contains("AXON.RC30.Integration.csproj", sln);
        Assert.Contains("ArenaDesktopCompanion.Core.Tests.csproj", sln);
    }
    [Fact]
    public void Manuelles_Absenden_und_fehlende_OS_Isolation_sind_dokumentiert()
    {
        Assert.Contains("Der Nutzer sendet selbst", Read("README.md"));
        Assert.Contains("keine Betriebssystem-Sandbox", Read("README.md"));
        Assert.Contains("Benutzerrechte", Read("SECURITY.md"));
    }
    [Fact]
    public void Historische_Baseline_ist_keine_neue_Installerfreigabe()
    {
        Assert.Contains("historische Baselinewerte", Read("README.md"));
        Assert.Contains("Releasegates", Read("docs/VALIDATION.md"));
        Assert.Contains("Freigabegates", Read("installer/README.md"));
    }
    [Fact]
    public void Private_Laufzeitdaten_und_Secrets_werden_ignoriert()
    {
        var ignore = Read(".gitignore");
        foreach (var entry in new[] { ".axon/", "Profiles/", "Sessions/", "*.pfx", "settings.json", ".artifacts/", "releases/" })
            Assert.Contains(entry, ignore);
        Assert.Contains("nicht rückwirkend", Read("PROJECT.OVERVIEW.md"));
    }
    [Fact]
    public void Lizenz_und_Drittanbieterhinweise_bleiben_erhalten()
    {
        Assert.Contains("MIT", Read("LICENSE"));
        Assert.Contains("Arena.ai Desktop Companion contributors", Read("LICENSE"));
        Assert.Contains("WebView2", Read("THIRD-PARTY-NOTICES.md"));
    }
    [Fact]
    public void Installer_Freigabegate_ist_an_Windowsnachweis_gebunden()
    {
        var build = Read("scripts/Build-Installer.ps1");
        Assert.Contains("ReleaseGate", build);
        Assert.Contains("SourceDigest", build);
        Assert.Contains("WindowsInstallVerified=$false", build);
        Assert.Contains("No process was killed", Read("scripts/Install-Local.ps1"));
    }
    [Fact]
    public void Private_GitHubveroeffentlichung_ist_nicht_vorgetaeuscht()
    {
        Assert.Contains("privat", Read("README.md"));
        Assert.Contains("Eigentümer", Read("PROJECT.OVERVIEW.md"));
        Assert.Contains("workflow_dispatch", Read(".github/workflows/windows-validation.yml"));
        Assert.DoesNotContain("push:", Read(".github/workflows/windows-validation.yml"));
    }
}
