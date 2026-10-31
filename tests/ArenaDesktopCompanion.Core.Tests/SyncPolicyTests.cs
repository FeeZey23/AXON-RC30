using ArenaDesktopCompanion.Core.Sync;
using ArenaDesktopCompanion.Core.Tests.Support;

namespace ArenaDesktopCompanion.Core.Tests;

/// <summary>
/// Das Regelwerk, das entscheidet was mit einer hereinkommenden Datei passiert (ADR 002).
/// </summary>
public class SyncPolicyTests
{
    [Fact]
    public void Neue_Datei_wird_geschrieben_und_im_Index_vermerkt()
    {
        using var tree = new TempTree();
        var policy = new SyncPolicy(tree.Root);

        var result = policy.Apply("a.txt", "inhalt 1", "test");

        Assert.Equal(SyncOutcome.Written, result.Outcome);
        Assert.Equal("inhalt 1", tree.Read("a.txt"));
        Assert.True(policy.Index.TryGet("a.txt", out var entry));
        Assert.Equal("test", entry!.Source);
        Assert.True(File.Exists(tree.Full(SyncIndex.FileName)));
    }

    [Fact]
    public void Identischer_Inhalt_loest_keinen_Schreibvorgang_aus()
    {
        using var tree = new TempTree();
        var policy = new SyncPolicy(tree.Root);
        policy.Apply("a.txt", "gleich", "test");
        var before = File.GetLastWriteTimeUtc(tree.Full("a.txt"));

        Thread.Sleep(30);
        var second = policy.Apply("a.txt", "gleich", "test");

        Assert.Equal(SyncOutcome.Unchanged, second.Outcome);
        Assert.Equal(before, File.GetLastWriteTimeUtc(tree.Full("a.txt")));
    }

    [Fact]
    public void Dreimal_dieselbe_Datei_ergibt_einen_Eintrag_idempotent()
    {
        using var tree = new TempTree();
        var policy = new SyncPolicy(tree.Root);
        for (var i = 0; i < 3; i++)
        {
            policy.Apply("a.txt", "gleich", "test");
        }

        Assert.Equal(1, policy.Index.Count);
    }

    [Fact]
    public void Von_uns_geschriebene_Datei_wird_aktualisiert_mit_Sicherung()
    {
        using var tree = new TempTree();
        var policy = new SyncPolicy(tree.Root);
        policy.Apply("src/a.py", "v1", "test");

        var result = policy.Apply("src/a.py", "v2", "test");

        Assert.Equal(SyncOutcome.UpdatedWithBackup, result.Outcome);
        Assert.Equal("v2", tree.Read("src/a.py"));
        var backups = Directory.GetFiles(tree.Full(SyncPolicy.BackupDirName), "a.py", SearchOption.AllDirectories);
        Assert.Single(backups);
        Assert.Equal("v1", File.ReadAllText(backups[0]));
    }

    [Fact]
    public void NurMelden_schreibt_nichts_aber_plane()
    {
        using var tree = new TempTree();
        var policy = new SyncPolicy(tree.Root, new SyncPolicyOptions { ReportOnly = true });

        var plan = policy.Apply("x.txt", "inhalt", "test");

        Assert.Equal(SyncOutcome.Planned, plan.Outcome);
        Assert.False(tree.Exists("x.txt"));
        Assert.False(File.Exists(tree.Full(SyncIndex.FileName)));
    }

    [Fact]
    public void Sicherung_aus_schreibt_ohne_Backup_Ordner()
    {
        using var tree = new TempTree();
        var policy = new SyncPolicy(tree.Root, new SyncPolicyOptions { KeepBackups = false });
        policy.Apply("a.py", "v1", "test");

        var result = policy.Apply("a.py", "v2", "test");

        Assert.Equal(SyncOutcome.UpdatedWithBackup, result.Outcome);
        Assert.Null(result.BackupRelativePath);
        Assert.False(Directory.Exists(tree.Full(SyncPolicy.BackupDirName)));
    }

    [Theory]
    [InlineData("../entkommen.py")]
    [InlineData("C:\\windows\\x.py")]
    [InlineData("src/a<b>.py")]
    [InlineData("nul")]
    public void Blockierte_Pfade_erreichen_die_Platte_nicht(string relative)
    {
        using var tree = new TempTree();
        var policy = new SyncPolicy(tree.Root);

        var result = policy.Apply(relative, "payload", "test");

        Assert.Equal(SyncOutcome.Blocked, result.Outcome);
        Assert.Contains("blockiert", result.Message, StringComparison.Ordinal);
        Assert.Empty(tree.AllFiles().Where(f => !f.StartsWith(".arena", StringComparison.Ordinal)));
    }

    [Fact]
    public void Bau_und_Abhangaengigkeiten_werden_uebersprungen()
    {
        using var tree = new TempTree();
        var policy = new SyncPolicy(tree.Root);

        Assert.Equal(SyncOutcome.Skipped, policy.Apply("node_modules/pkg/index.js", "x", "test").Outcome);
        Assert.Equal(SyncOutcome.Skipped, policy.Apply(".git/config", "x", "test").Outcome);
        Assert.False(tree.Exists("node_modules"));
    }

    [Fact]
    public void Riesen_Inhalt_wird_blockiert()
    {
        using var tree = new TempTree();
        var policy = new SyncPolicy(tree.Root, new SyncPolicyOptions { MaxFileBytes = 16 });

        var result = policy.Apply("big.txt", new string('x', 200), "test");

        Assert.Equal(SyncOutcome.Blocked, result.Outcome);
        Assert.Contains("Limit", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Byte_Ueberload_erhaelt_Zeilenenden_und_Kodierung_exakt()
    {
        using var tree = new TempTree();
        var policy = new SyncPolicy(tree.Root);
        var bytes = new byte[] { (byte)'a', (byte)'\r', (byte)'\n', (byte)'b', 0xC3, 0xA4 };

        policy.Apply("crlf.txt", bytes, "test");

        Assert.Equal(bytes, tree.ReadAll("crlf.txt"));
    }

    [Fact]
    public void Unterordner_werden_beim_Schreiben_angelegt()
    {
        using var tree = new TempTree();
        var policy = new SyncPolicy(tree.Root);

        policy.Apply("a/b/c/deep.txt", "x", "test");

        Assert.True(tree.Exists("a/b/c/deep.txt"));
    }

    [Fact]
    public void Nach_einem_Fehler_liegt_keine_temp_Datei_im_Projekt()
    {
        using var tree = new TempTree();
        var policy = new SyncPolicy(tree.Root);
        tree.Write("locked.txt", "alt");
        policy.Apply("locked.txt", "neu-1", "test");
        policy.Apply("locked.txt", "neu-2", "test");

        Assert.Empty(Directory.GetFiles(tree.Root, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public void Lokal_veraenderte_Datei_wird_nie_ueberschrieben()
    {
        using var tree = new TempTree();
        var policy = new SyncPolicy(tree.Root, new SyncPolicyOptions
        {
            ConflictResolution = ArenaDesktopCompanion.Core.Conflicts.ConflictResolution.KeepMine
        });
        policy.Apply("main.py", "arena-v1", "test");

        tree.Write("main.py", "nutzer-arbeit");      // Nutzer editiert nach unserem Schreibvorgang

        var result = policy.Apply("main.py", "arena-v2", "test");

        Assert.Equal(SyncOutcome.ConflictPreserved, result.Outcome);
        Assert.Equal("nutzer-arbeit", tree.Read("main.py"));        // Original bleibt heil
        Assert.NotNull(result.ConflictRelativePath);
        Assert.Equal("arena-v2", File.ReadAllText(tree.Full(result.ConflictRelativePath!)));
        Assert.False(File.Exists(tree.Full(SyncPolicy.BackupDirName)));  // nichts ueberschrieben => kein Backup
    }

    [Fact]
    public void Unbekannte_bestehende_Datei_wird_nicht_ohne_Sicherung_geaendert()
    {
        using var tree = new TempTree();
        tree.Write("alt.py", "das-war-schon-da");
        var policy = new SyncPolicy(tree.Root, new SyncPolicyOptions
        {
            ConflictResolution = ArenaDesktopCompanion.Core.Conflicts.ConflictResolution.KeepMine
        });

        var result = policy.Apply("alt.py", "neu", "test");

        Assert.Equal(SyncOutcome.ConflictPreserved, result.Outcome);
        Assert.Equal("das-war-schon-da", tree.Read("alt.py"));
    }

    [Fact]
    public void Zwei_Richtlinien_auf_demselben_Ordner_streiten_nicht_miteinander()
    {
        using var tree = new TempTree();
        var streng = new SyncPolicy(tree.Root, new SyncPolicyOptions
        {
            ConflictResolution = ArenaDesktopCompanion.Core.Conflicts.ConflictResolution.KeepMine
        });
        var grosszuegig = new SyncPolicy(tree.Root, new SyncPolicyOptions
        {
            ConflictResolution = ArenaDesktopCompanion.Core.Conflicts.ConflictResolution.ArenaWins
        });

        streng.Apply("x.py", "v1", "test");
        tree.Write("x.py", "nutzer");
        var erster = grosszuegig.Apply("x.py", "v2", "test");

        Assert.Equal(SyncOutcome.UpdatedWithBackup, erster.Outcome);
        Assert.Equal("v2", tree.Read("x.py"));
        Assert.NotNull(erster.BackupRelativePath);
        Assert.Equal("nutzer", File.ReadAllText(tree.Full(erster.BackupRelativePath!)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n")]
    [InlineData("   \r\n\t\n")]
    public void Leerer_Inhalt_leert_keine_gefuellte_Datei(string leer)
    {
        // Am 24.09.2026 hat der Codeblock-Fanger docs\BRUECKE.md von 19.672 auf 0 Bytes gebracht.
        // Ein leerer Block darf eine gefüllte Datei nie mehr überschreiben.
        using var tree = new TempTree();
        var policy = new SyncPolicy(tree.Root);
        policy.Apply("docs/BRUECKE.md", "wichtiger Inhalt", "test");

        var result = policy.Apply("docs/BRUECKE.md", leer, "dom-codeblock");

        Assert.Equal(SyncOutcome.Blocked, result.Outcome);
        Assert.Contains("leerer Inhalt", result.Message);
        Assert.Equal("wichtiger Inhalt", tree.Read("docs/BRUECKE.md"));
    }

    [Fact]
    public void Leerer_Inhalt_auf_leere_Datei_bleibt_erlaubt()
    {
        // Die Sperre greift nur gegen Datenverlust – eine leere Datei bleibt eine leere Datei.
        using var tree = new TempTree();
        var policy = new SyncPolicy(tree.Root);
        tree.Write("leer.txt", string.Empty);

        var result = policy.Apply("leer.txt", string.Empty, "test");

        Assert.Equal(SyncOutcome.Unchanged, result.Outcome);
    }
}
