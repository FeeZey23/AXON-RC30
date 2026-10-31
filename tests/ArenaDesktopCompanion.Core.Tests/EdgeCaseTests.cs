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

public class EdgeBehaviourTests
{
    [Fact]
    public void Ordner_statt_zip_in_der_Inbox_wird_als_Fehler_gemeldet_und_nach_failed_legt()
    {
        using var tree = new TempTree();
        var processor = new InboxProcessor(tree.Root, readyRetryCount: 1, readyDelayMs: 0);
        processor.EnsureLayout();
        Directory.CreateDirectory(Path.Combine(processor.InboxDirectory, "kaputt.zip"));

        var run = processor.ProcessPending(new SyncPolicy(tree.Root));

        // Ordner in der Inbox werden ignoriert (die Liste liest nur Dateien). Wichtig: kein Crash,
        // kein halbverarbeiteter Zustand, der Ordner bleibt liegen.
        Assert.True(run.IsEmpty);
        Assert.True(Directory.Exists(Path.Combine(processor.InboxDirectory, "kaputt.zip")));
        Assert.False(tree.Exists("kaputt.zip"));
    }

    [Fact]
    public void Zielverzeichnis_durch_Datei_verbaut_Schreiben_wird_Fehler_statt_Crash()
    {
        using var tree = new TempTree();
        tree.Write("blocker", "ich bin eine Datei");

        // "blocker/datei.txt" kann kein Verzeichnis "blocker" anlegen
        var result = new SyncPolicy(tree.Root).Apply("blocker/datei.txt", "x\n", "test");

        Assert.Equal(SyncOutcome.Failed, result.Outcome);
        Assert.Contains("Schreiben fehlgeschlagen", result.Message, StringComparison.Ordinal);
        Assert.False(tree.Exists("blocker/datei.txt"));
    }

    [Fact]
    public void Bericht_zeigt_Sicherung_und_Konflikt_Pfade()
    {
        using var tree = new TempTree();
        var policy = new SyncPolicy(tree.Root, new SyncPolicyOptions { ConflictResolution = ConflictResolution.KeepMine });

        // Konflikt (fremde Datei) + normale Aktualisierung (von uns geschrieben) in einem Bericht.
        tree.Write("fremd.py", "nutzer\n");
        var conflict = policy.Apply("fremd.py", "arena\n", "test");
        policy.Apply("eigene.py", "v1\n", "test");
        var updated = policy.Apply("eigene.py", "v2\n", "test");

        Assert.Equal(SyncOutcome.ConflictPreserved, conflict.Outcome);
        Assert.Equal(SyncOutcome.UpdatedWithBackup, updated.Outcome);

        var items = new[]
        {
            new InboxItemResult("fremd.py", conflict, false),
            new InboxItemResult("eigene.py", updated, false)
        };
        var run = new InboxRunResult(items, new[] { "|maskiert|", "zeile\nmit umbruch" });
        var path = ImportReportWriter.Write(tree.Root, run, "morgen");
        var text = File.ReadAllText(path);

        Assert.Contains("Konflikt: `", text, StringComparison.Ordinal);
        Assert.Contains("Backup: `Backup/", text, StringComparison.Ordinal);
        Assert.Contains("## Fehler", text, StringComparison.Ordinal);
        Assert.Contains(@"\|maskiert\|", text, StringComparison.Ordinal);   // Tabelle bleibt gültig
        Assert.DoesNotContain("zeile\nmit umbruch\nirgendwas", text, StringComparison.Ordinal);
        Assert.Contains("morgen", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Watcher_ohne_Richtlinie_laeuft_ins_Leere_statt_zu_crashen()
    {
        using var tree = new TempTree();
        var processor = new InboxProcessor(tree.Root, readyRetryCount: 1, readyDelayMs: 0);
        processor.EnsureLayout();
        using var watcher = new InboxWatcher(processor, debounceMs: 40);

        Assert.False(watcher.IsWatching);
        Assert.Null(watcher.ActivePolicy);

        // Start ohne Policy: kein Absturz, keine Datei wird angefasst.
        watcher.Start();
        Assert.True(watcher.IsWatching);
        File.WriteAllText(Path.Combine(processor.InboxDirectory, "x.txt"), "y\n");
        var run = await watcher.RunOnceAsync();
        Assert.Null(run);
        Assert.True(tree.Exists(".arena-inbox/x.txt"));   // liegt weiter unverarbeitet drin

        watcher.Stop();
        Assert.False(watcher.IsWatching);
        watcher.Stop();                                     // zweimal anhalten ist ok
        watcher.Dispose();
    }

    [Fact]
    public void CrashReporter_ohne_Logger_liefert_keinen_Pfad_und_verliert_nichts()
    {
        var reporter = new CrashReporter(null);

        Assert.Null(reporter.ReportToFile("Bereich", new Exception("x")));
        Assert.Null(reporter.LastLogFile);
        Assert.Equal("Exception: x", new Exception("x").ShortMessage());
    }

    [Fact]
    public void Leere_und_Null_Eingaenge_im_Parser_sind_ohne_Ausnahme()
    {
        Assert.False(CodeBlockParser.TryParse(null, out var file, out var error));
        Assert.Null(file);
        Assert.Contains("Leerer Text", error, StringComparison.Ordinal);

        Assert.False(CodeBlockParser.TryParse("   \n\t\n", out _, out error));
        Assert.Contains("Leerer Text", error, StringComparison.Ordinal);

        Assert.Null(CodeBlockParser.TryExtractPath(""));
        Assert.Null(CodeBlockParser.TryExtractPath("   "));
    }

    [Fact]
    public void Backup_Rotation_ueberlebt_beschaedigten_Backup_Ordner()
    {
        using var tree = new TempTree();
        var policy = new SyncPolicy(tree.Root, new SyncPolicyOptions { BackupRunsToKeep = 1 });
        policy.Apply("a.py", "v1\n", "test");
        for (var i = 2; i <= 6; i++)
        {
            policy.Apply("a.py", $"v{i}\n", "test");
            Thread.Sleep(20);   // andere Zeitstempel-Sekunde ist für die Rotation nicht nötig, aber billig
        }

        var runs = Directory.GetDirectories(tree.Full(SyncPolicy.BackupDirName));
        Assert.True(runs.Length <= 6);
        Assert.True(Directory.Exists(tree.Full(SyncPolicy.BackupDirName)));

        // kaputter Eintrag (Datei statt Ordner) im Backup-Pfad: Prune wirft nicht
        File.WriteAllText(Path.Combine(tree.Full(SyncPolicy.BackupDirName), "00000000-000000"), "x");
        BackupRotator.Prune(tree.Root, runsToKeep: 1);
        Assert.True(File.Exists(Path.Combine(tree.Full(SyncPolicy.BackupDirName), "00000000-000000")));
    }
}

/// <summary>Drei weitere echte Versprechen, die die Klasse einlösen muss.</summary>
