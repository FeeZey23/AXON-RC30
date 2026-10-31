using ArenaDesktopCompanion.Core.Sync;
using ArenaDesktopCompanion.Core.Tests.Support;

namespace ArenaDesktopCompanion.Core.Tests;

public class IndexFaultToleranceTests
{
    [Fact]
    public void Schreibbarer_Projektordner_nur_lesbar_laesst_den_Import_gelingen()
    {
        using var tree = new TempTree();
        // Index-Pfad durch ein Verzeichnis blockieren: Save schlägt fehl, Apply nicht.
        Directory.CreateDirectory(tree.Full(SyncIndex.FileName));
        var policy = new SyncPolicy(tree.Root);

        var result = policy.Apply("a.py", "inhalt\n", "test");

        Assert.Equal(SyncOutcome.Written, result.Outcome);
        Assert.Equal("inhalt\n", tree.Read("a.py"));
        Assert.NotNull(policy.IndexWarning);
        Assert.Contains("Sync-Index nicht schreibbar", policy.IndexWarning, StringComparison.Ordinal);
    }

    [Fact]
    public void IndexSave_meldet_Erfolg_und_Fehler()
    {
        using var tree = new TempTree();
        var index = new SyncIndex();
        index.Set("a", "hash", 1, "t");

        Assert.True(index.Save(tree.Full("ok.json")));
        Assert.Null(index.LastError);

        Directory.CreateDirectory(tree.Full("blockiert.json"));
        Assert.False(index.Save(tree.Full("blockiert.json")));
        Assert.False(string.IsNullOrWhiteSpace(index.LastError));
    }
}
