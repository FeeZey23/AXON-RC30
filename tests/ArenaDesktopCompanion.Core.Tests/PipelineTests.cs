using ArenaDesktopCompanion.Core.Import;
using ArenaDesktopCompanion.Core.Settings;
using ArenaDesktopCompanion.Core.Sync;
using ArenaDesktopCompanion.Core.Tests.Support;

namespace ArenaDesktopCompanion.Core.Tests;

/// <summary>
/// Die Kette, die im echten Leben abläuft – als ein Test, damit keine Lücke zwischen
/// Bausteinen bleibt (Nachricht → Parser → Regelwerk → Platte).
/// </summary>
public class PipelineTests
{
    private const string Message = """
        {"action":"SYNC_FILE","fileName":"","content":"```python\r\n# path: src/app.py\r\nprint('hall\u00f6')\r\n```"}
        """;

    [Fact]
    public void BotNachricht_wird_parsed_und_geschrieben_Kopf_und_Fence_frei()
    {
        using var tree = new TempTree();
        var policy = new SyncPolicy(tree.Root);

        Assert.True(BotMessageParser.TryParse(Message, out var message, out var error), error);
        Assert.True(CodeBlockParser.TryParse(message!.Content, out var block, out var parseError), parseError);

        var result = policy.Apply(block!.RelativePath, block.Content, "dom-codeblock");

        Assert.Equal(SyncOutcome.Written, result.Outcome);
        var written = tree.Read("src/app.py");
        Assert.Equal("print('hallö')\n", written);
        Assert.DoesNotContain("path:", written, StringComparison.Ordinal);
        Assert.DoesNotContain("```", written, StringComparison.Ordinal);
    }

    [Fact]
    public void Zweiter_Durchlauf_des_gleichen_Blocks_schreibt_nicht_erneut()
    {
        using var tree = new TempTree();
        var policy = new SyncPolicy(tree.Root);
        CodeBlockParser.TryParse("```py\n# path: a.py\nv1\n```", out var first);
        policy.Apply(first!.RelativePath, first.Content, "bot");

        // Gleicher Inhalt erneut (die Seite rendert nach): kein zweiter Schreibvorgang.
        var again = policy.Apply(first.RelativePath, first.Content, "bot");

        Assert.Equal(SyncOutcome.Unchanged, again.Outcome);
    }

    [Fact]
    public void Geaenderter_Block_aktualisiert_und_sichert()
    {
        using var tree = new TempTree();
        var policy = new SyncPolicy(tree.Root);
        CodeBlockParser.TryParse("# path: a.py\nv1\n", out var v1);
        policy.Apply(v1!.RelativePath, v1.Content, "bot");

        CodeBlockParser.TryParse("# path: a.py\nv2\n", out var v2);
        var updated = policy.Apply(v2!.RelativePath, v2.Content, "bot");

        Assert.Equal(SyncOutcome.UpdatedWithBackup, updated.Outcome);
        Assert.Equal("v2\n", tree.Read("a.py"));
        var backup = Directory.GetFiles(tree.Full(SyncPolicy.BackupDirName), "a.py", SearchOption.AllDirectories);
        Assert.Single(backup);
        Assert.Equal("v1\n", File.ReadAllText(backup[0]));
    }

    [Fact]
    public void Aus_BotNachricht_und_Archiv_entsteht_derselbe_Zustand()
    {
        using var tree = new TempTree();
        var policy = new SyncPolicy(tree.Root);
        CodeBlockParser.TryParse("# path: docs/readme.md\nhallo\n", out var block);
        policy.Apply(block!.RelativePath, block.Content, "bot");

        using var staging = new TempTree();
        var zip = Path.Combine(staging.Root, "w.zip");
        using (var archive = System.IO.Compression.ZipFile.Open(zip, System.IO.Compression.ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("session/docs/readme.md");
            using var w = new StreamWriter(entry.Open());
            w.Write("hallo\n");
        }

        var report = ArchiveImporter.ImportZip(zip, policy);

        // Identischer Inhalt aus zweiter Quelle -> kein Überschreiben, kein Backup-Müll.
        Assert.Equal(0, report.Written + report.Updated);
        Assert.Equal(1, report.Unchanged);
    }

    [Fact]
    public void Blockierter_Name_aus_der_Nachricht_verlaesst_die_Platte_nicht()
    {
        using var tree = new TempTree();
        var policy = new SyncPolicy(tree.Root);

        Assert.False(CodeBlockParser.TryParse("# path: ../../escape.py\nx\n", out _, out var error));
        Assert.Contains("blockiert", error);
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(tree.Root)!, "escape.py")));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(tree.Root)!)!, "escape.py")));
    }
}

public class InboxWatcherTests
{
    [Fact]
    public async Task Watcher_verarbeitet_eine_Datei_die_waehrend_des_Laufs_ankommt()
    {
        using var tree = new TempTree();
        var processor = new InboxProcessor(tree.Root, readyRetryCount: 5, readyDelayMs: 20);
        using var watcher = new InboxWatcher(processor, debounceMs: 60) { ActivePolicy = new SyncPolicy(tree.Root) };

        var done = new TaskCompletionSource<InboxRunResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        watcher.ImportFinished += run => done.TrySetResult(run);

        watcher.Start();
        File.WriteAllText(Path.Combine(processor.InboxDirectory, "spaet.txt"), "inkommt\n");

        var finished = await Task.WhenAny(done.Task, Task.Delay(TimeSpan.FromSeconds(20)));
        Assert.Same(done.Task, finished);

        var run = await done.Task;
        Assert.Equal(1, run.Written);
        Assert.Equal("inkommt\n", tree.Read("spaet.txt"));
    }

    [Fact]
    public async Task Gleichzeitige_Laeufe_verarbeiten_jede_Datei_genau_einmal()
    {
        using var tree = new TempTree();
        var processor = new InboxProcessor(tree.Root, readyRetryCount: 3, readyDelayMs: 0);
        processor.EnsureLayout();
        for (var i = 0; i < 12; i++)
        {
            File.WriteAllText(Path.Combine(processor.InboxDirectory, $"d{i}.txt"), "x" + i);
        }

        var policy = new SyncPolicy(tree.Root);

        // Sechs gleichzeitige Anstöße (Watcher + Knopfdruck): jeder Datei-Inhalt darf genau
        // einmal ankommen – weder doppelt noch verloren.
        var tasks = Enumerable.Range(0, 6)
            .Select(_ => Task.Run(() => processor.ProcessPending(policy).Items.Count))
            .ToArray();
        var counts = await Task.WhenAll(tasks);

        Assert.Equal(12, counts.Sum());
        var processed = Path.Combine(processor.InboxDirectory, InboxProcessor.ProcessedFolderName);
        Assert.Equal(12, Directory.GetFiles(processed).Length);
        Assert.Empty(Directory.GetFiles(processor.InboxDirectory));
        Assert.Equal(12, policy.Index.Count);
    }
}

/// <summary>
/// Die Schicht, die das Fenster tatsächlich benutzt. Wird sie hier nicht getestet,
/// ist sie auf dem PC der einzige ungetestete Teil der Kette.
/// </summary>
public class ProjectSyncServiceTests
{
    private static AppSettings Settings(Action<AppSettings>? tweak = null)
    {
        var s = new AppSettings { AutoImport = false, ReportOnly = false, KeepBackups = true, BackupRunsToKeep = 20 };
        tweak?.Invoke(s);
        return s;
    }

    [Fact]
    public async Task Bot_Ablauf_ueber_den_Service_legt_Datei_an_und_liest_sie_in_der_Liste()
    {
        using var tree = new TempTree();
        var log = new List<string>();
        using var service = new ProjectSyncService(tree.Root, Settings(), m => log.Add(m));

        var line = await service.ImportFromBotAsync("", "# path: src/app.py\nprint(1)\n");

        Assert.Contains("src/app.py neu geschrieben", line);
        Assert.Equal("print(1)\n", tree.Read("src/app.py"));

        // Zweiter Anstoß mit gleichem Inhalt: nichts Neues.
        var again = await service.ImportFromBotAsync("", "# path: src/app.py\nprint(1)\n");
        Assert.Contains("unverändert", again);
    }

    [Fact]
    public async Task Beschriftung_am_Block_gewinnt_gegen_den_Textpfad()
    {
        using var tree = new TempTree();
        using var service = new ProjectSyncService(tree.Root, Settings(), _ => { });

        await service.ImportFromBotAsync("**web/index.html**", "<!-- path: falsch/anderer.html -->\n<h1>hi</h1>\n");

        Assert.True(tree.Exists("web/index.html"));
        Assert.False(tree.Exists("falsch/anderer.html"));
        Assert.Equal("<h1>hi</h1>\n", tree.Read("web/index.html"));   // Kopfzeile trotzdem entfernt
    }

    [Fact]
    public async Task Unerkennbarer_Block_wird_gemeldet_statt_gefahren()
    {
        using var tree = new TempTree();
        using var service = new ProjectSyncService(tree.Root, Settings(), _ => { });

        var line = await service.ImportFromBotAsync("", "nur text ohne dateinamen\n");

        Assert.Contains("ignoriert", line);
        Assert.Empty(tree.AllFiles().Where(f => !f.StartsWith(".arena", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Gefaehrlicher_Name_aus_dem_Block_wird_blockiert()
    {
        using var tree = new TempTree();
        using var service = new ProjectSyncService(tree.Root, Settings(), _ => { });

        var line = await service.ImportFromBotAsync("../../../etc/passwd", "x\n");

        Assert.Contains("blockiert", line);
    }

    [Fact]
    public async Task Scan_Nach_Download_und_Zweiter_Call_sind_idempotent()
    {
        using var tree = new TempTree();
        var service = new ProjectSyncService(tree.Root, Settings(), _ => { });
        service.EnsureInboxLayoutForTests();
        File.WriteAllText(Path.Combine(service.InboxDirectory, "a.txt"), "1\n");

        var first = await service.ScanNowAsync("test");
        var second = await service.ScanNowAsync("test");

        Assert.Equal(1, first!.Written);
        Assert.NotNull(second);
        Assert.Equal(0, second!.Written);
        Assert.Equal("1\n", tree.Read("a.txt"));
        service.Dispose();
    }

    [Fact]
    public async Task NurMelden_Modus_plant_ohne_Schreiben()
    {
        using var tree = new TempTree();
        var settings = Settings(s => s.ReportOnly = true);
        using var service = new ProjectSyncService(tree.Root, settings, _ => { });

        var line = await service.ImportFromBotAsync("", "# path: neue-datei.py\nprint(1)\n");

        Assert.Contains("Nur melden", line);
        Assert.False(tree.Exists("neue-datei.py"));
    }

    [Fact]
    public void ApplySettings_erneuert_die_Richtlinie_eines_laufenden_Watchers()
    {
        using var tree = new TempTree();
        var settings = Settings(s => s.AutoImport = true);
        using var service = new ProjectSyncService(tree.Root, settings, _ => { });
        service.StartWatching();
        Assert.NotNull(service.WatcherActivePolicy);

        settings.ReportOnly = true;
        service.ApplySettings();

        Assert.True(service.Policy.ReportOnly);
        Assert.True(service.WatcherActivePolicy!.ReportOnly);
        service.StopWatching();
        Assert.Null(service.WatcherActivePolicy);
    }

    [Fact]
    public async Task Konfliktfall_ueber_den_Service_ueberschreibt_nicht()
    {
        using var tree = new TempTree();
        using var service = new ProjectSyncService(tree.Root, Settings(), _ => { });
        await service.ImportFromBotAsync("", "# path: main.py\narena-v1\n");
        tree.Write("main.py", "nutzer-v1\n");

        var line = await service.ImportFromBotAsync("", "# path: main.py\narena-v2\n");

        Assert.Contains("Konflikt", line);
        Assert.Equal("nutzer-v1\n", tree.Read("main.py"));
    }

    // ============ 24.09.2026, live: das Berichts-Karussell ============
    // Im Fenster stand im Sekundentakt „Bericht: chat-….md" mit „2385 Dateien verarbeitet, 0 neu".
    // Ursache: Ein Lauf, der nur nachsieht und nichts ändert, schrieb trotzdem einen Bericht.

    [Fact]
    public async Task Lauf_ohne_Aenderung_schreibt_keinen_Bericht()
    {
        using var tree = new TempTree();
        using var service = new ProjectSyncService(tree.Root, Settings(), _ => { });
        service.EnsureInboxLayoutForTests();

        var archiv = Path.Combine(tree.Root, ".arena-inbox", "paket.zip");
        PaketSchreiben(archiv);

        var erster = await service.ScanNowAsync("Test");
        Assert.NotNull(erster);
        Assert.True(erster!.HatArbeit);
        var bericht = Path.Combine(tree.Root, ".arena-inbox", "reports");
        Assert.True(Directory.Exists(bericht), "Beim ersten Lauf muss ein Bericht entstehen.");
        var nachErstem = Directory.GetFiles(bericht).Select(File.GetLastWriteTimeUtc).Max();

        // Dasselbe Paket ein zweites Mal in die Inbox legen: alles unverändert – kein neuer Bericht.
        PaketSchreiben(archiv);
        await Task.Delay(30);
        var zweiter = await service.ScanNowAsync("Test 2");

        Assert.NotNull(zweiter);
        Assert.False(zweiter!.HatArbeit);
        Assert.Equal(nachErstem, Directory.GetFiles(bericht).Select(File.GetLastWriteTimeUtc).Max());
        Assert.All(zweiter.Items, i => Assert.Equal(SyncOutcome.Unchanged, i.Result.Outcome));
    }

    [Fact]
    public void Zusammenfassung_unterscheidet_geprueft_und_geaendert()
    {
        var unveraendert = new InboxRunResult(
            [new InboxItemResult("a.txt", new SyncResult(SyncOutcome.Unchanged, "a.txt", "unverändert"), false)],
            []);
        Assert.False(unveraendert.HatArbeit);
        Assert.Contains("geprüft", unveraendert.Summarize());
        Assert.Contains("0 neu", unveraendert.Summarize());

        var geschrieben = new InboxRunResult(
            [new InboxItemResult("a.txt", new SyncResult(SyncOutcome.Written, "a.txt", "neu geschrieben", 7), false)],
            []);
        Assert.True(geschrieben.HatArbeit);

        var mitFehler = new InboxRunResult([], ["kaputt"]);
        Assert.True(mitFehler.HatArbeit);
    }

    /// <summary>Ein kleines Paket mit zwei Dateien – Inhalt immer gleich.</summary>
    private static void PaketSchreiben(string pfad)
    {
        using var zip = new System.IO.Compression.ZipArchive(
            File.Open(pfad, FileMode.Create), System.IO.Compression.ZipArchiveMode.Create);
        foreach (var name in new[] { "a.txt", "b.txt" })
        {
            var eintrag = zip.CreateEntry(name);
            using var strom = new StreamWriter(eintrag.Open());
            strom.Write("inhalt " + name);
        }
    }
}
