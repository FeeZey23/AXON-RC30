using ArenaDesktopCompanion.Core.Sync;
using ArenaDesktopCompanion.Core.Tests.Support;

namespace ArenaDesktopCompanion.Core.Tests;

/// <summary>Backup-Aufbewahrung: letzte N Stände, Aufräumen darf nichts kaputt machen.</summary>
public class BackupRotationTests
{
    [Fact]
    public void Nur_die_letzten_zwei_Stande_bleiben_uebrig()
    {
        using var tree = new TempTree();
        var backupRoot = tree.Full(SyncPolicy.BackupDirName);
        Directory.CreateDirectory(backupRoot);
        foreach (var stamp in new[] { "20200101-000000", "20200102-000000", "20200103-000000" })
        {
            var dir = Path.Combine(backupRoot, stamp);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "x.txt"), stamp);
        }

        BackupRotator.Prune(tree.Root, runsToKeep: 2);

        var left = Directory.GetDirectories(backupRoot).Select(Path.GetFileName).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "20200102-000000", "20200103-000000" }, left);
    }

    [Fact]
    public void Sicherung_einer_nicht_vorhandenen_Datei_ist_stillschweigend_nichts()
    {
        using var tree = new TempTree();
        Assert.Null(BackupRotator.Create(tree.Root, "fehlt.py", 5));
    }

    [Fact]
    public void Sicherung_kopiert_die_Vorversion_in_den_Zeitstempelordner()
    {
        using var tree = new TempTree();
        tree.Write("src/a.py", "vorher");

        var relative = BackupRotator.Create(tree.Root, "src/a.py", 20);

        Assert.NotNull(relative);
        Assert.Equal("vorher", File.ReadAllText(tree.Full(relative!)));
        Assert.Contains(SyncPolicy.BackupDirName, relative!, StringComparison.Ordinal);
    }

    [Fact]
    public void Prune_mit_null_behaelt_nichts_und_wirft_nicht()
    {
        using var tree = new TempTree();
        var backupRoot = tree.Full(SyncPolicy.BackupDirName);
        Directory.CreateDirectory(Path.Combine(backupRoot, "20200101-000000"));

        BackupRotator.Prune(tree.Root, runsToKeep: 0);

        Assert.Empty(Directory.GetDirectories(backupRoot));
    }

    /// <summary>
    /// Fund aus dem Master-Check (24.09.2026): Die Brücke legt ihre Sicherungen als
    /// <c>bruecke-&lt;zeit&gt;</c> an. Über den Namen sortiert stand das „b" vor jeder Ziffer – die
    /// Brücken-Sicherungen galten damit immer als die neuesten, und die Rotation hätte die falschen
    /// Stände gelöscht. Jetzt zählt der Zeitstempel, egal ob ein Vorsatz davor steht.
    /// </summary>
    [Fact]
    public void Bruecken_Sicherungen_zaehlen_nach_ihrer_Zeit_und_nicht_nach_dem_Namen()
    {
        using var tree = new TempTree();
        var backupRoot = tree.Full(SyncPolicy.BackupDirName);
        Directory.CreateDirectory(backupRoot);
        foreach (var name in new[]
                 {
                     "bruecke-20200101-120000", "20200102-120000", "bruecke-20200103-120000", "20200104-120000",
                 })
        {
            Directory.CreateDirectory(Path.Combine(backupRoot, name));
        }

        BackupRotator.Prune(tree.Root, runsToKeep: 3);

        var left = Directory.GetDirectories(backupRoot).Select(Path.GetFileName)
            .OrderBy(x => x, StringComparer.Ordinal).ToArray();
        // Ohne den Griff wäre der älteste Stand (bruecke-2020…) als der neueste durchgegangen und
        // stattdessen 20200102-120000 gelöscht worden.
        Assert.Equal(new[] { "20200102-120000", "20200104-120000", "bruecke-20200103-120000" }, left);
    }

    /// <summary>
    /// Dieselbe Grenze wie im Sync: Nach dem Aufräumen bleiben genau so viele Stände wie eingestellt –
    /// auch wenn Zähler- und Brücken-Sicherungen gemischt im Ordner liegen.
    /// </summary>
    [Fact]
    public void Gemischte_Staende_bleiben_genau_so_viele_wie_eingestellt()
    {
        using var tree = new TempTree();
        var backupRoot = tree.Full(SyncPolicy.BackupDirName);
        Directory.CreateDirectory(backupRoot);
        for (var tag = 1; tag <= 25; tag++)
        {
            var name = (tag % 2 == 0 ? "bruecke-" : string.Empty) + $"202001{tag:00}-120000";
            Directory.CreateDirectory(Path.Combine(backupRoot, name));
        }

        BackupRotator.Prune(tree.Root, runsToKeep: 20);

        Assert.Equal(20, Directory.GetDirectories(backupRoot).Length);
        Assert.True(Directory.Exists(Path.Combine(backupRoot, "20200125-120000")));
        Assert.False(Directory.Exists(Path.Combine(backupRoot, "bruecke-20200101-120000")));
    }
}
