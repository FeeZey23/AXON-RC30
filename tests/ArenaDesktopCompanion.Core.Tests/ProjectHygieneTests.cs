using ArenaDesktopCompanion.Core.Conflicts;
using ArenaDesktopCompanion.Core.Diagnostics;
using ArenaDesktopCompanion.Core.IO;
using ArenaDesktopCompanion.Core.Import;
using ArenaDesktopCompanion.Core.Settings;
using ArenaDesktopCompanion.Core.Sync;
using ArenaDesktopCompanion.Core.Tests.Support;
using System.IO.Compression;
using System.Reflection;

namespace ArenaDesktopCompanion.Core.Tests;

public class ProjectHygieneTests
{
    [Fact]
    public void Leerer_Ordner_ohne_git_und_gitignore_zeigt_beides_an()
    {
        using var tree = new TempTree();
        var readiness = ProjectHygiene.Inspect(tree.Root);

        Assert.False(readiness.HasGitRepository);
        Assert.False(readiness.HasGitignore);
        Assert.True(readiness.NeedsGitInit);
        Assert.True(readiness.NeedsGitignore);
        Assert.False(readiness.IsClean);
        Assert.Null(readiness.Error);
    }

    [Fact]
    public void Vorhandene_git_und_gitignore_sind_ruhe()
    {
        using var tree = new TempTree();
        Directory.CreateDirectory(tree.Full(".git"));
        tree.Write(".gitignore", "bin/\n");

        var readiness = ProjectHygiene.Inspect(tree.Root);

        Assert.True(readiness.IsClean);
        Assert.False(ProjectHygiene.ShouldShowHint(readiness, dismissedByUser: false));
    }

    [Fact]
    public void Git_als_Datei_zaehlt_worktree_und_submodule()
    {
        using var tree = new TempTree();
        tree.Write(".git", "gitdir: /wo/auch/immer\n");

        Assert.True(ProjectHygiene.Inspect(tree.Root).HasGitRepository);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("   ", true)]
    public void Ohne_Ordner_kein_Hinweis_aber_Meldung(string? root, bool expectsError)
    {
        var readiness = ProjectHygiene.Inspect(root);

        Assert.Equal(expectsError, readiness.Error is not null);
        Assert.False(ProjectHygiene.ShouldShowHint(readiness, dismissedByUser: false));
    }

    [Fact]
    public void Nicht_existenter_Ordner_wird_nicht_angefasst()
    {
        var path = Path.Combine(Path.GetTempPath(), "gibt-es-nicht-" + Guid.NewGuid().ToString("N"));
        var readiness = ProjectHygiene.Inspect(path);

        Assert.Contains("existiert nicht", readiness.Error, StringComparison.Ordinal);
        Assert.Null(ProjectHygiene.EnsureRecommendedGitignore(path));
        Assert.False(Directory.Exists(path));
    }

    [Fact]
    public void Gitignore_Wird_Nur_Angelegt_Wenn_Fehlend()
    {
        using var tree = new TempTree();
        var first = ProjectHygiene.EnsureRecommendedGitignore(tree.Root);

        Assert.NotNull(first);
        var content = File.ReadAllText(first!);
        foreach (var entry in new[] { ".arena-inbox/", "Backup/", ".arena-conflicts/", ".arena-sync-index.json" })
        {
            Assert.Contains(entry, content, StringComparison.Ordinal);
        }

        // Vorhandene Datei anfassen wäre ein Übergriff.
        Assert.Null(ProjectHygiene.EnsureRecommendedGitignore(tree.Root));
        Assert.Equal(content, File.ReadAllText(first!));
    }

    [Fact]
    public void Fehlende_Eintragungen_werden_ergaenzt_bestehende_bleiben()
    {
        using var tree = new TempTree();
        tree.Write(".gitignore", "# meine Regeln\n*.log\n.arena-inbox/\n");

        Assert.True(ProjectHygiene.AppendMissingIgnoreEntries(tree.Root));

        var content = File.ReadAllText(tree.Full(".gitignore"));
        Assert.Contains("# meine Regeln", content, StringComparison.Ordinal);
        Assert.Contains("*.log", content, StringComparison.Ordinal);
        Assert.Contains("Backup/", content, StringComparison.Ordinal);
        Assert.Contains(".arena-conflicts/", content, StringComparison.Ordinal);
        Assert.Equal(1, content.Split("\n").Count(l => l.Trim() == ".arena-inbox/"));  // nicht doppelt

        // Alles vorhanden -> nichts tun.
        Assert.False(ProjectHygiene.AppendMissingIgnoreEntries(tree.Root));
    }

    [Fact]
    public void Ausblenden_ist_endgueltig_unabhaengig_vom_Ordner()
    {
        using var tree = new TempTree();
        var readiness = ProjectHygiene.Inspect(tree.Root);

        Assert.True(ProjectHygiene.ShouldShowHint(readiness, dismissedByUser: false));
        Assert.False(ProjectHygiene.ShouldShowHint(readiness, dismissedByUser: true));
    }

    [Fact]
    public void Git_init_Kandidaten_decken_alte_git_Versionen_ab()
    {
        var candidates = ProjectHygiene.GitInitCommandCandidates();

        Assert.Equal(2, candidates.Count);
        Assert.Equal(new[] { "init", "-b", "main" }, candidates[0]);
        Assert.Equal(new[] { "init" }, candidates[1]);
    }
}
