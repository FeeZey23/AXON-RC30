using ArenaDesktopCompanion.Core.Conflicts;
using ArenaDesktopCompanion.Core.Diff;
using ArenaDesktopCompanion.Core.Sync;
using ArenaDesktopCompanion.Core.Tests.Support;

namespace ArenaDesktopCompanion.Core.Tests;

public class DecisionApplierTests
{
    [Fact]
    public void Vorlage_entsteht_nur_wenn_etwas_zu_entscheiden_ist()
    {
        using var tree = new TempTree();
        var policy = new SyncPolicy(tree.Root);
        policy.Apply("a.py", "x\n", "test");

        // Nur von uns geschriebene, unveränderte Dateien -> nichts zu entscheiden.
        Assert.Null(DecisionApplier.WriteTemplate(tree.Root, _ => { }));

        // Fremde Bestandsdatei (nie im Index) -> Konflikt -> Vorlage mit genau einem Eintrag.
        tree.Write("fremd.py", "nutzer\n");
        var conflict = policy.Apply("fremd.py", "arena\n", "test");
        Assert.Equal(SyncOutcome.NeedsDecision, conflict.Outcome);

        var path = DecisionApplier.WriteTemplate(tree.Root, _ => { });
        Assert.NotNull(path);
        var json = File.ReadAllText(path!);
        Assert.Contains("\"kind\": \"conflict\"", json, StringComparison.Ordinal);
        Assert.Contains("fremd.py", json, StringComparison.Ordinal);
        Assert.Contains("meine behalten", json, StringComparison.Ordinal);   // ausgefüllte Anleitung
        Assert.Single(DecisionApplier.ReadPending(tree.Root));
    }

    [Fact]
    public void Antwort_arena_uebernimmt_die_Datei_und_raeumt_auf()
    {
        using var tree = new TempTree();
        tree.Write("m.py", "meins\n");
        var policy = new SyncPolicy(tree.Root, new SyncPolicyOptions { ConflictResolution = ConflictResolution.Ask });
        policy.Apply("m.py", "arena\n", "test");

        var template = Path.Combine(tree.Root, SyncPolicy.ConflictDirName, "pending", "decisions.json");
        File.WriteAllText(template, File.ReadAllText(template).Replace("\"answer\": null", "\"answer\": \"arena\""));

        var applied = DecisionApplier.ApplyPending(tree.Root, policy, _ => { });

        Assert.Equal(1, applied);
        Assert.Equal("arena\n", tree.Read("m.py"));
        Assert.False(File.Exists(template));
        Assert.True(policy.Index.TryGet("m.py", out _));
        Assert.Equal("meins\n", Directory
            .GetFiles(tree.Full(SyncPolicy.BackupDirName), "m.py", SearchOption.AllDirectories)
            .Select(File.ReadAllText).Single());
    }

    [Fact]
    public void Antwort_mine_verwirft_die_Arena_Version_und_bewaahrt_mich()
    {
        using var tree = new TempTree();
        tree.Write("m.py", "meins\n");
        var policy = new SyncPolicy(tree.Root, new SyncPolicyOptions { ConflictResolution = ConflictResolution.Ask });
        var asked = policy.Apply("m.py", "arena\n", "test");

        var template = Path.Combine(tree.Root, SyncPolicy.ConflictDirName, "pending", "decisions.json");
        File.WriteAllText(template, File.ReadAllText(template).Replace("\"answer\": null", "\"answer\": \"mine\""));

        Assert.Equal(1, DecisionApplier.ApplyPending(tree.Root, policy, _ => { }));
        Assert.Equal("meins\n", tree.Read("m.py"));
        Assert.False(File.Exists(asked.PendingArenaCopyPath));
    }

    [Fact]
    public void Antwort_beide_haelt_beides()
    {
        using var tree = new TempTree();
        tree.Write("m.py", "meins\n");
        var policy = new SyncPolicy(tree.Root, new SyncPolicyOptions { ConflictResolution = ConflictResolution.Ask });
        policy.Apply("m.py", "arena\n", "test");

        var template = Path.Combine(tree.Root, SyncPolicy.ConflictDirName, "pending", "decisions.json");
        File.WriteAllText(template, File.ReadAllText(template).Replace("\"answer\": null", "\"answer\": \"both\""));

        DecisionApplier.ApplyPending(tree.Root, policy, _ => { });

        Assert.Equal("meins\n", tree.Read("m.py"));
        Assert.Equal("arena\n", tree.Read(Path.Combine(SyncPolicy.ConflictDirName, "m.arena.py")));
    }

    [Fact]
    public void Ungueltige_Antworten_bleiben_stehen_und_crashen_nicht()
    {
        using var tree = new TempTree();
        tree.Write("m.py", "meins\n");
        var policy = new SyncPolicy(tree.Root, new SyncPolicyOptions { ConflictResolution = ConflictResolution.Ask });
        policy.Apply("m.py", "arena\n", "test");

        var template = Path.Combine(tree.Root, SyncPolicy.ConflictDirName, "pending", "decisions.json");
        File.WriteAllText(template, File.ReadAllText(template).Replace("\"answer\": null", "\"answer\": \"Quatsch\""));

        Assert.Equal(0, DecisionApplier.ApplyPending(tree.Root, policy, _ => { }));
        Assert.True(File.Exists(template));
        Assert.Equal("meins\n", tree.Read("m.py"));
    }

    [Fact]
    public void SetAnswer_traegt_ein_unbekannte_Id_ist_ein_Fehler()
    {
        using var tree = new TempTree();
        tree.Write("m.py", "meins\n");
        var policy = new SyncPolicy(tree.Root);
        policy.Apply("m.py", "arena\n", "test");

        var item = DecisionApplier.ReadPending(tree.Root).Single();
        Assert.True(DecisionApplier.SetAnswer(tree.Root, item.Id, "ARENA"));
        Assert.Equal("arena", DecisionApplier.ReadPending(tree.Root).Single().Answer);
        Assert.False(DecisionApplier.SetAnswer(tree.Root, "unbekannt", "mine"));

        Assert.Equal(1, DecisionApplier.ApplyPending(tree.Root, policy, _ => { }));
        Assert.Equal("arena\n", tree.Read("m.py"));
    }

    [Fact]
    public void Fehlende_oder_kaputte_Entscheidungsdatei_ist_kein_Fehler()
    {
        using var tree = new TempTree();
        var policy = new SyncPolicy(tree.Root);

        Assert.Equal(0, DecisionApplier.ApplyPending(tree.Root, policy, _ => { }));
        Assert.Equal(0, DecisionApplier.ApplyPending("/gibt/es/nicht", policy, _ => { }));

        var template = Path.Combine(tree.Root, SyncPolicy.ConflictDirName, "pending");
        Directory.CreateDirectory(template);
        File.WriteAllText(Path.Combine(template, "decisions.json"), "{ kein json");
        Assert.Equal(0, DecisionApplier.ApplyPending(tree.Root, policy, _ => { }));
    }
}
