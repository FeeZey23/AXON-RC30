using ArenaDesktopCompanion.Core.Sync;
using ArenaDesktopCompanion.Core.Tests.Support;

namespace ArenaDesktopCompanion.Core.Tests;

/// <summary>Der Sync-Index: das Gedächtnis hinter der Frage „wer hat das zuletzt geschrieben?“.</summary>
public class SyncIndexTests
{
    [Fact]
    public void Index_ueberlebt_einen_Neustart_als_Roundtrip()
    {
        using var tree = new TempTree();
        var path = tree.Full(SyncIndex.FileName);

        var index = new SyncIndex();
        index.Set("src/a.py", "abc123", 42, "test");
        Assert.True(index.Save(path));

        var reloaded = SyncIndex.Load(path);

        Assert.True(reloaded.TryGet("src/a.py", out var entry));
        Assert.Equal("abc123", entry!.Sha256);
        Assert.Equal(42, entry.Size);
        Assert.Equal("test", entry.Source);
        Assert.False(reloaded.WasCorrupt);
    }

    [Fact]
    public void Defekter_Index_ist_kein_Hindernis()
    {
        using var tree = new TempTree();
        var path = tree.Full(SyncIndex.FileName);
        File.WriteAllText(path, "{ das ist kein json ");

        var reloaded = SyncIndex.Load(path);

        Assert.True(reloaded.WasCorrupt);
        Assert.Equal(0, reloaded.Count);
    }

    [Fact]
    public void Fehlender_Index_startt_leer_ohne_Fehler()
    {
        using var tree = new TempTree();
        var reloaded = SyncIndex.Load(tree.Full("gibt-es-nicht.json"));

        Assert.Equal(0, reloaded.Count);
        Assert.False(reloaded.WasCorrupt);
    }

    [Fact]
    public void Save_ist_atomar_und_hinterlaesst_kein_temp()
    {
        using var tree = new TempTree();
        var path = tree.Full(SyncIndex.FileName);
        var index = new SyncIndex();
        index.Set("a", "h", 1, "t");

        index.Save(path);

        Assert.False(File.Exists(path + ".tmp"));
        Assert.Contains("\"a\"", File.ReadAllText(path), StringComparison.Ordinal);
    }
}
