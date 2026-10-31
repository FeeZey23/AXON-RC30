using ArenaDesktopCompanion.Core.Sync;
using ArenaDesktopCompanion.Core.Tests.Support;

namespace ArenaDesktopCompanion.Core.Tests;

/// <summary>
/// zip-Ablage erkennen: Top-Ordner nur entfernen, wenn er eindeutig ist und nicht zum Projekt gehoert.
/// </summary>
public class ArchiveLayoutTests
{
    [Theory]
    [InlineData(new[] { "s/a.py", "s/b.py" }, "s")]
    [InlineData(new[] { "a.py", "b.py" }, null)]
    [InlineData(new[] { "s/a.py", "t/b.py" }, null)]
    [InlineData(new[] { "s/a.py" }, "s")]
    [InlineData(new[] { "s/a.py", "s/b/c.py" }, "s")]
    [InlineData(new string[0], null)]
    public void Gemeinsamer_Top_Ordner_wird_erkannt(string[] entries, string? expected)
        => Assert.Equal(expected, ArchiveLayout.DetectCommonRoot(entries));

    [Fact]
    public void Ordner_existiert_er_schon_im_Projekt_dann_bleibt_alles_wo_es_ist()
    {
        using var tree = new TempTree();
        Directory.CreateDirectory(tree.Full("docs"));

        Assert.Null(ArchiveLayout.DetectCommonRoot(new[] { "docs/readme.md", "docs/tut.md" }, tree.Root));
        Assert.Equal("docs", ArchiveLayout.DetectCommonRoot(new[] { "docs/readme.md", "docs/tut.md" }, null));
    }

    [Fact]
    public void Entfernen_des_Top_Ordners()
    {
        Assert.Equal("a.py", ArchiveLayout.RemoveRoot("s/a.py", "s"));
        Assert.Equal("s/a.py", ArchiveLayout.RemoveRoot("s/a.py", null));
        Assert.Equal("andere/x.py", ArchiveLayout.RemoveRoot("andere/x.py", "s"));
    }

    [Fact]
    public void Absolute_Namen_werden_nie_vorbehandelt()
    {
        // Sonst wurde aus "C:/windows/x.dll" durch Abschneiden ein harmloser Pfad.
        Assert.Null(ArchiveLayout.DetectCommonRoot(new[] { "C:/windows/x.dll" }));
        Assert.True(ArchiveLayout.LooksAbsolute("C:/windows/x.dll"));
        Assert.True(ArchiveLayout.LooksAbsolute("/etc/passwd"));
        Assert.False(ArchiveLayout.LooksAbsolute("src/a.py"));
    }
}
