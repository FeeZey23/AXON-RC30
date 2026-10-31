using ArenaDesktopCompanion.Core.Sync;
using ArenaDesktopCompanion.Core.Tests.Support;

namespace ArenaDesktopCompanion.Core.Tests;

public class PathSafetyTests
{
    [Theory]
    // Was durchgehen muss
    [InlineData("app.py", "app.py")]
    [InlineData("src/app.py", "src/app.py")]
    [InlineData("src\\deep\\util.py", "src/deep/util.py")]
    [InlineData("./lead.py", "lead.py")]
    [InlineData("strasse.py", "strasse.py")]
    [InlineData("mein file.py", "mein file.py")]
    [InlineData("README.MD", "README.MD")]
    [InlineData("a/b/c/d.txt", "a/b/c/d.txt")]
    public void Gueltige_Pfade_werden_normalisiert(string input, string expected)
    {
        Assert.True(PathSafety.TryValidateRelative(input, out var relative, out var error), error);
        Assert.Equal(expected, relative);
    }

    [Theory]
    // Alles, was aus dem Projektordner herausführen oder auf Windows kippen würde
    [InlineData("../outside.py")]
    [InlineData("src/../../outside.py")]
    [InlineData("C:\\proj\\app.py")]
    [InlineData("c:/proj/app.py")]
    [InlineData("/etc/passwd")]
    [InlineData("\\\\server\\share\\x.py")]
    [InlineData("src/a<b>.py")]
    [InlineData("src/a>b.py")]
    [InlineData("a:b.py")]
    [InlineData("file?.py")]
    [InlineData("file*.py")]
    [InlineData("file\"1.py")]
    [InlineData("nul")]
    [InlineData("con.py")]
    [InlineData("LPT1.txt")]
    [InlineData("src/")]
    [InlineData("src//a.py")]
    [InlineData("src/.")]
    [InlineData("....//x")]
    [InlineData("a/..")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("src/trailing.")]
    public void Gefaehrliche_Pfade_werden_abgelehnt(string input)
    {
        Assert.False(PathSafety.TryValidateRelative(input, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error), "Ablehnung muss einen Grund nennen.");
    }

    [Fact]
    public void Zu_langes_Segment_wird_abgelehnt()
    {
        var input = new string('a', 200) + ".py";
        Assert.False(PathSafety.TryValidateRelative(input, out _, out var error));
        Assert.Contains("länger", error);
    }

    [Fact]
    public void Zu_langer_Gesamtpfad_wird_abgelehnt()
    {
        var input = string.Join('/', Enumerable.Repeat(new string('b', 100), 4)) + ".py";
        Assert.False(PathSafety.TryValidateRelative(input, out _, out var error));
        Assert.Contains("länger", error);
    }

    [Fact]
    public void Steuerzeichen_werden_abgelehnt()
    {
        Assert.False(PathSafety.TryValidateRelative("src/a\tb.py", out _, out var error));
        Assert.Contains("0x09", error);
    }

    [Fact]
    public void Aufloesung_bleibt_im_Projektordner()
    {
        using var tree = new TempTree();
        Assert.True(PathSafety.TryResolveInside(tree.Root, "src/app.py", out var relative, out var full, out _));
        Assert.StartsWith(Path.GetFullPath(tree.Root), Path.GetFullPath(full), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("src/app.py", relative);
    }

    [Fact]
    public void Ausbruch_ueber_root_wird_abgelehnt_und_nichts_geschrieben()
    {
        using var tree = new TempTree();
        var outside = Path.Combine(Path.GetDirectoryName(tree.Root)!, "opfer.txt");
        try
        {
            Assert.False(PathSafety.TryResolveInside(tree.Root, "../opfer.txt", out _, out _, out var error));
            Assert.False(File.Exists(outside));
            Assert.Contains("..", error);
        }
        finally
        {
            if (File.Exists(outside)) File.Delete(outside);
        }
    }

    [Theory]
    [InlineData("**app.py**", "app.py")]
    [InlineData("`src/a.py`", "src/a.py")]
    [InlineData("path: src/a.py", "src/a.py")]
    [InlineData("// path: app.py", "app.py")]
    [InlineData("/* file: index.html */", "index.html")]
    [InlineData("# dateiname: haupt.py", "haupt.py")]
    [InlineData("  app.py  ", "app.py")]
    [InlineData("'app.py'", "app.py")]
    public void Sauberer_Kandidat_aus_Rohtext(string input, string expected)
        => Assert.Equal(expected, PathSafety.CleanCandidate(input));

    [Fact]
    public void Sauberer_Kandidat_erhaelt_traversal_absicht()
    {
        // Cleaning darf einen Ausbruchsversuch nicht unschädlich "reparieren" –
        // sonst verschwindet die Information, dass hier etwas blockiert wurde.
        Assert.Equal("../outside.py", PathSafety.CleanCandidate("# path: ../outside.py"));
    }

    [Fact]
    public void Sauberer_Kandidat_fuer_leeren_Eingabe_liefert_null()
    {
        Assert.Null(PathSafety.CleanCandidate(null));
        Assert.Null(PathSafety.CleanCandidate("   "));
        Assert.Null(PathSafety.CleanCandidate("::"));
    }

    [Theory]
    // Was wie ein Dateiname aussieht …
    [InlineData("app.py", true)]
    [InlineData("src/app.csproj", true)]
    [InlineData("landschaft_alpen.png", true)]
    [InlineData("photo.JPG", true)]
    [InlineData("README.MD", true)]
    [InlineData("mein-file_v2.txt", true)]
    // … und was nicht (Feldrunde 24.09.2026: daraus wurden leere Dateien)
    [InlineData("Dispatcher.Invoke", false)]
    [InlineData("BridgeHost.AufUi", false)]
    [InlineData("System.IO.File.ReadAllText", false)]
    [InlineData("node --check arena-bot.js", false)]
    [InlineData("cmd/c ping -n 121 127.0.0.1", false)]
    [InlineData("x = 1", false)]
    [InlineData("git --version", false)]
    [InlineData("ohne-endung", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void Formpruefung_erkennt_Dateinamen(string text, bool erwartet)
        => Assert.Equal(erwartet, PathSafety.LooksLikeFileName(text));
}
