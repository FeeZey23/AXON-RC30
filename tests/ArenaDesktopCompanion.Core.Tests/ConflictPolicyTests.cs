using ArenaDesktopCompanion.Core.Conflicts;
using ArenaDesktopCompanion.Core.Diff;
using ArenaDesktopCompanion.Core.Sync;
using ArenaDesktopCompanion.Core.Tests.Support;

namespace ArenaDesktopCompanion.Core.Tests;

public class ConflictPolicyTests
{
    [Theory]
    [InlineData(ConflictResolution.ArenaWins, ConflictDecisionKind.Written)]
    [InlineData(ConflictResolution.KeepMine, ConflictDecisionKind.KeptMine)]
    [InlineData(ConflictResolution.KeepBoth, ConflictDecisionKind.KeptBoth)]
    [InlineData(ConflictResolution.Ask, ConflictDecisionKind.NeedsUser)]
    public void Decide_traegt_nichts_ein_und_sagt_wie_es_laeuft(ConflictResolution resolution, ConflictDecisionKind expected)
    {
        using var tree = new TempTree();
        tree.Write("main.py", "meins\n");

        var decision = new ConflictPolicy(resolution).Decide(tree.Root, "main.py", "arena\n");

        Assert.Equal(expected, decision.Kind);
        Assert.Equal("meins\n", tree.Read("main.py"));   // Decide schreibt nichts an die Zieldatei
    }

    [Fact]
    public void Identische_Inhalte_sind_kein_Konflikt()
    {
        using var tree = new TempTree();
        tree.Write("main.py", "gleich\n");

        var decision = new ConflictPolicy(ConflictResolution.Ask).Decide(tree.Root, "main.py", "gleich\n");

        Assert.Equal(ConflictDecisionKind.NoConflict, decision.Kind);
        Assert.False(decision.NeedsUser);
    }

    [Fact]
    public void Ask_liefert_Pfade_fuer_Anfrage_und_Kopien()
    {
        using var tree = new TempTree();
        tree.Write("src/main.py", "meins\n");

        var decision = new ConflictPolicy(ConflictResolution.Ask).Decide(tree.Root, "src/main.py", "arena\n");

        Assert.True(decision.NeedsUser);
        Assert.Equal("src/main.py", decision.RelativePath);
        Assert.Contains("decisions.json", decision.RequestFile!.Replace('\\', '/'), StringComparison.Ordinal);
        Assert.EndsWith("mine", decision.MineCopyPath);
        Assert.EndsWith("arena", decision.ArenaCopyPath);
    }

    [Fact]
    public void Keep_both_waehlt_einen_freien_Namen()
    {
        using var tree = new TempTree();
        tree.Write("a/b/main.py", "meins\n");
        tree.Write("a/b/main.arena.py", "belegt\n");

        var decision = new ConflictPolicy(ConflictResolution.KeepBoth).Decide(tree.Root, "a/b/main.py", "arena\n");

        Assert.Equal("a/b/main.arena-2.py", decision.ArenaCopyRelative);
        Assert.Equal("belegt\n", tree.Read("a/b/main.arena.py"));   // nichts wurde überschrieben
    }

    [Fact]
    public void Arena_Wins_nennt_die_Sicherung_der_eigenen_Datei()
    {
        using var tree = new TempTree();
        tree.Write("main.py", "meins\n");

        var decision = new ConflictPolicy(ConflictResolution.ArenaWins).Decide(tree.Root, "main.py", "arena\n");

        Assert.StartsWith("Backup/", decision.MineBackupRelative!.Replace('\\', '/'), StringComparison.Ordinal);
        Assert.EndsWith("main.py", decision.MineBackupRelative);
    }
}

public class SyncPolicyConflictTests
{
    private static SyncPolicy Policy(string root, ConflictResolution resolution)
        => new(root, new SyncPolicyOptions { ConflictResolution = resolution });

    [Fact]
    public void Unbekannte_Datei_mit_Arena_wins_wird_ueberschrieben_mit_Sicherung()
    {
        using var tree = new TempTree();
        tree.Write("main.py", "nutzer\n");

        var result = Policy(tree.Root, ConflictResolution.ArenaWins).Apply("main.py", "arena\n", "test");

        Assert.Equal(SyncOutcome.UpdatedWithBackup, result.Outcome);
        Assert.Equal("arena\n", tree.Read("main.py"));
        Assert.Single(Directory.GetFiles(tree.Full(SyncPolicy.BackupDirName), "main.py", SearchOption.AllDirectories));
        Assert.Equal("nutzer\n", Directory.GetFiles(tree.Full(SyncPolicy.BackupDirName), "main.py", SearchOption.AllDirectories)
            .Select(File.ReadAllText).Single());
    }

    [Fact]
    public void Unbekannte_Datei_mit_Keep_mine_laesst_mich_in_Ruhe()
    {
        using var tree = new TempTree();
        tree.Write("main.py", "nutzer\n");

        var result = Policy(tree.Root, ConflictResolution.KeepMine).Apply("main.py", "arena\n", "test");

        Assert.Equal(SyncOutcome.ConflictPreserved, result.Outcome);
        Assert.Equal("nutzer\n", tree.Read("main.py"));
        Assert.NotNull(result.ConflictRelativePath);
    }

    [Fact]
    public void Keep_both_leggt_Arena_Version_aber_nicht_ueber_mich_drueber()
    {
        using var tree = new TempTree();
        tree.Write("a/b/main.py", "nutzer\n");

        var result = Policy(tree.Root, ConflictResolution.KeepBoth).Apply("a/b/main.py", "arena\n", "test");

        Assert.Equal(SyncOutcome.KeptBoth, result.Outcome);
        Assert.Equal("nutzer\n", tree.Read("a/b/main.py"));
        Assert.Equal("arena\n", tree.Read("a/b/main.arena.py"));
        Assert.Equal("a/b/main.arena.py", result.ArenaCopyRelativePath);
    }

    [Fact]
    public void Ask_erzeugt_eine_Entscheidungsanfrage_ohne_zu_schreiben()
    {
        using var tree = new TempTree();
        tree.Write("main.py", "nutzer\n");

        var result = Policy(tree.Root, ConflictResolution.Ask).Apply("main.py", "arena\n", "test");

        Assert.Equal(SyncOutcome.NeedsDecision, result.Outcome);
        Assert.Equal("nutzer\n", tree.Read("main.py"));
        var template = Path.Combine(tree.Root, SyncPolicy.ConflictDirName, "pending", "decisions.json");
        Assert.True(File.Exists(template));

        var json = File.ReadAllText(template);
        Assert.Contains("\"kind\": \"conflict\"", json, StringComparison.Ordinal);
        Assert.Contains("\"answer\": null", json, StringComparison.Ordinal);
        Assert.Contains("main.py", json, StringComparison.Ordinal);
        // Beide Versionen liegen neben der Anfrage, damit verglichen werden kann.
        Assert.Contains("arena\n", File.ReadAllText(result.PendingArenaCopyPath!), StringComparison.Ordinal);
        Assert.Contains("nutzer\n", File.ReadAllText(result.PendingMineCopyPath!), StringComparison.Ordinal);
    }

    [Fact]
    public void Nur_melden_schreibt_auch_bei_Arena_wins_nichts()
    {
        using var tree = new TempTree();
        tree.Write("main.py", "nutzer\n");
        var policy = new SyncPolicy(tree.Root, new SyncPolicyOptions
        {
            ConflictResolution = ConflictResolution.ArenaWins,
            ReportOnly = true
        });

        var result = policy.Apply("main.py", "arena\n", "test");

        Assert.Equal(SyncOutcome.Planned, result.Outcome);
        Assert.Equal("nutzer\n", tree.Read("main.py"));
    }

    [Fact]
    public void Von_uns_geschriebene_Datei_ignoriert_die_Konflikt_Rechnung()
    {
        using var tree = new TempTree();
        var policy = Policy(tree.Root, ConflictResolution.KeepMine);   // strengste Stufe
        policy.Apply("main.py", "v1\n", "test");

        var second = policy.Apply("main.py", "v2\n", "test");

        Assert.Equal(SyncOutcome.UpdatedWithBackup, second.Outcome);
        Assert.Equal("v2\n", tree.Read("main.py"));
    }
}
