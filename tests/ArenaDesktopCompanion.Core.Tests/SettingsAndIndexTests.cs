using ArenaDesktopCompanion.Core.Conflicts;
using ArenaDesktopCompanion.Core.Diagnostics;
using ArenaDesktopCompanion.Core.Diff;
using ArenaDesktopCompanion.Core.Import;
using ArenaDesktopCompanion.Core.Settings;
using ArenaDesktopCompanion.Core.Sync;
using ArenaDesktopCompanion.Core.Tests.Support;
using System.Reflection;

namespace ArenaDesktopCompanion.Core.Tests;

public class ForgetAndSettingsTests
{
    [Fact]
    public void Forget_entfernt_den_Index_Eintrag_sonst_kommt_ein_Scheinkonflikt()
    {
        using var tree = new TempTree();
        var policy = new SyncPolicy(tree.Root);
        policy.Apply("a.py", "v1\n", "test");

        File.Delete(tree.Full("a.py"));
        policy.Forget("a.py");

        // Ohne Forget würde der nächste Import „Konflikt“ melden, obwohl die Datei weg ist.
        var again = policy.Apply("a.py", "v1\n", "test");
        Assert.Equal(SyncOutcome.Written, again.Outcome);
        Assert.False(policy.Index.Entries.ContainsKey("fehlt.py"));

        policy.Forget("fehlt.py");   // unbekannter Pfad: kein Fehler, kein Schreibvorgang
        Assert.False(File.Exists(tree.Full(SyncIndex.FileName + ".tmp")));
    }

    [Fact]
    public void Konfliktmodus_ueberlebt_die_Einstellungsdatei()
    {
        using var tree = new TempTree();
        var store = new ArenaDesktopCompanion.Core.Settings.AppSettingsStore(tree.Root);
        store.Save(new ArenaDesktopCompanion.Core.Settings.AppSettings
        {
            ConflictResolution = ArenaDesktopCompanion.Core.Conflicts.ConflictResolution.KeepBoth,
            BackupRunsToKeep = 7
        });

        var loaded = new ArenaDesktopCompanion.Core.Settings.AppSettingsStore(tree.Root).Load();

        Assert.Equal(ArenaDesktopCompanion.Core.Conflicts.ConflictResolution.KeepBoth, loaded.ConflictResolution);
        Assert.Equal(7, loaded.BackupRunsToKeep);
    }

    [Fact]
    public void Ungueltiger_Konfliktmodus_faellt_auf_die_Empfehlung_zurueck()
    {
        var settings = new ArenaDesktopCompanion.Core.Settings.AppSettings { ConflictResolutionMode = 99 };
        Assert.Equal(ArenaDesktopCompanion.Core.Conflicts.ConflictResolution.Ask, settings.ConflictResolution);

        settings.ConflictResolutionMode = -3;
        Assert.Equal(ArenaDesktopCompanion.Core.Conflicts.ConflictResolution.Ask, settings.ConflictResolution);
    }

    [Fact]
    public void Service_uebergibt_den_Konfliktmodus_an_die_Richtlinie()
    {
        using var tree = new TempTree();
        var settings = new ArenaDesktopCompanion.Core.Settings.AppSettings
        {
            AutoImport = false,
            ConflictResolution = ArenaDesktopCompanion.Core.Conflicts.ConflictResolution.KeepMine
        };
        using var service = new ProjectSyncService(tree.Root, settings, _ => { });

        tree.Write("x.py", "meins\n");
        var result = service.Policy.Apply("x.py", "arena\n", "test");

        Assert.Equal(SyncOutcome.ConflictPreserved, result.Outcome);
        Assert.Equal("meins\n", tree.Read("x.py"));
    }
}

public class AppSettingsStoreTests
{
    [Fact]
    public void Ohne_Datei_gelten_die_Defaults()
    {
        using var tree = new TempTree();
        var store = new AppSettingsStore(tree.Root);

        var settings = store.Load();

        Assert.True(settings.AutoImport);
        Assert.False(settings.CodeBlockBotEnabled);   // Experimentelles ist Standard aus (ADR 001)
        Assert.False(settings.ReportOnly);
        Assert.Null(settings.ProjectFolder);
        Assert.False(store.LastLoadWasCorrupt);
    }

    [Fact]
    public void Einstellungen_ueberleben_einen_Neustart()
    {
        using var tree = new TempTree();
        var store = new AppSettingsStore(tree.Root);
        store.Save(new AppSettings { ProjectFolder = tree.Root, ReportOnly = true, CodeBlockBotEnabled = true });

        var reloaded = new AppSettingsStore(tree.Root).Load();

        Assert.Equal(tree.Root, reloaded.ProjectFolder);
        Assert.True(reloaded.ReportOnly);
        Assert.True(reloaded.CodeBlockBotEnabled);
    }

    [Fact]
    public void Kaputte_Settings_datei_bricht_nichts_und_bleibt_erhalten()
    {
        using var tree = new TempTree();
        var store = new AppSettingsStore(tree.Root);
        File.WriteAllText(store.FilePath, "{ hallo ");

        var settings = store.Load();

        Assert.True(store.LastLoadWasCorrupt);
        Assert.True(settings.AutoImport);
        Assert.Contains(Directory.GetFiles(tree.Root), f => Path.GetFileName(f).StartsWith("settings.json.corrupt-"));
    }

    [Fact]
    public void Speichern_in_nicht_schreibbarem_Pfad_meldet_den_Fehler()
    {
        var store = new AppSettingsStore(Path.Combine(Path.GetTempPath(), "arena-companion-tests", "gehtnicht", "\u0000"));
        var ex = Record.Exception(() => store.Save(new AppSettings()));
        Assert.NotNull(ex);
    }
}
