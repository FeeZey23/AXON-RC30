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

public class GuardAndReportTests
{
    [Fact]
    public void Zwei_Instanzen_in_demselben_Prozess_erkennt_die_zweite()
    {
        using var erste = new AppInstanceGuard("ArenaCompanionTest" + Guid.NewGuid().ToString("N"));
        Assert.True(erste.IsFirstInstance);

        using var zweite = new AppInstanceGuard("ArenaCompanionTest" + Guid.NewGuid().ToString("N"));
        Assert.True(zweite.IsFirstInstance);          // anderer Name -> andereMutex

        using var ersteNochmal = new AppInstanceGuard("dieselbe");
        using var zweiteGleich = new AppInstanceGuard("dieselbe");
        Assert.True(ersteNochmal.IsFirstInstance);
        Assert.False(zweiteGleich.IsFirstInstance);
    }

    [Fact]
    public void Absturzbericht_landet_vollstaendig_in_der_Logdatei()
    {
        using var tree = new TempTree();
        using var logger = new FileLogger(tree.Root);
        var reporter = new CrashReporter(logger);

        var exception = new InvalidOperationException("vordergrund", new IOException("innen drin"));
        var logFile = reporter.ReportToFile("UI-Thread", exception);

        Assert.NotNull(logFile);
        var text = File.ReadAllText(logFile!);
        Assert.Contains("UNERWARTETER FEHLER in UI-Thread", text, StringComparison.Ordinal);
        Assert.Contains("System.InvalidOperationException", text, StringComparison.Ordinal);
        Assert.Contains("innen drin", text, StringComparison.Ordinal);
        Assert.Contains("System.IO.IOException", text, StringComparison.Ordinal);
        Assert.Contains("OS :", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Kurzmeldung_zeigt_die_Ursache_statt_der_Hueller_Exception()
    {
        var wrapper = new System.Reflection.TargetInvocationException(
            "Exception has been thrown by the target of an invocation.", new InvalidOperationException("Runtime fehlt"));

        var message = wrapper.ShortMessage();

        Assert.Contains("InvalidOperationException: Runtime fehlt", message, StringComparison.Ordinal);
        // Der wertlose Huellersatz taucht nicht zusaetzlich auf.
        Assert.DoesNotContain("target of an invocation", message, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteRaw_haengt_an_und_write_nach_Dispose_passiert_nichts()
    {
        using var tree = new TempTree();
        var logger = new FileLogger(tree.Root, "rohtext.log");
        logger.WriteRaw("ZEILE-EINS\nZEILE-ZWEI");
        logger.Write("INFO", "normal");
        logger.Dispose();
        logger.Write("INFO", "nach disposed");
        logger.WriteRaw("nach disposed");

        var text = File.ReadAllText(logger.FilePath);
        Assert.Contains("ZEILE-EINS", text, StringComparison.Ordinal);
        Assert.Contains("[INFO] normal", text, StringComparison.Ordinal);
        Assert.DoesNotContain("nach disposed", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Rotation_behaelt_nur_keepfiles_Dateien()
    {
        using var tree = new TempTree();
        using var logger = new FileLogger(tree.Root, "roll.log", maxBytes: 64 * 1024, keepFiles: 2);
        for (var i = 0; i < 6000; i++)
        {
            logger.Write("INFO", new string('y', 60) + i);
        }

        Assert.True(File.Exists(Path.Combine(tree.Root, "roll.log")));
        Assert.True(File.Exists(Path.Combine(tree.Root, "roll.log.1")));
        Assert.True(File.Exists(Path.Combine(tree.Root, "roll.log.2")));
        Assert.False(File.Exists(Path.Combine(tree.Root, "roll.log.3")));
    }

    [Fact]
    public void LogRing_Kantenfaelle_Kleinkapazitaet_und_Text()
    {
        var ring = new LogRing(capacity: 1);          // wird auf 10 angehoben
        Assert.Equal(10, ring.Capacity);
        for (var i = 0; i < 25; i++)
        {
            ring.Add("INFO", "z" + i);
        }

        Assert.Equal(10, ring.Count);
        var text = ring.ToText();
        Assert.Equal(10, text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Contains("z24", text, StringComparison.Ordinal);
        Assert.DoesNotContain("z14", text, StringComparison.Ordinal);
        var display = new LogEntry(DateTimeOffset.UtcNow, "INFO", "x").ToDisplayLine();
        Assert.StartsWith("[", display, StringComparison.Ordinal);
        Assert.EndsWith("x", display, StringComparison.Ordinal);
    }

    [Fact]
    public void SyncResult_und_ArchiveImportReport_melden_richtig()
    {
        Assert.True(new SyncResult(SyncOutcome.Written, "a", "x").WroteSomething);
        Assert.True(new SyncResult(SyncOutcome.KeptBoth, "a", "x").WroteSomething);
        Assert.False(new SyncResult(SyncOutcome.Unchanged, "a", "x").WroteSomething);
        Assert.False(new SyncResult(SyncOutcome.Blocked, "a", "x").WroteSomething);

        var report = new ArchiveImportReport("z.zip", 6, 2, 1, 3, 0, 1, 1, 0, Array.Empty<string>());
        Assert.True(report.HadChanges);
        Assert.Contains("6 Einträge", report.Summarize(), StringComparison.Ordinal);
        Assert.Contains("3 unverändert", report.Summarize(), StringComparison.Ordinal);
        Assert.Contains("1 blockiert", report.Summarize(), StringComparison.Ordinal);

        var leer = new ArchiveImportReport("z.zip", 0, 0, 0, 0, 0, 0, 0, 0, Array.Empty<string>());
        Assert.False(leer.HadChanges);
    }

    [Fact]
    public void Bericht_sichert_Meldungen_und_Fehler_Abschnitt()
    {
        using var tree = new TempTree();
        var processor = new InboxProcessor(tree.Root, readyRetryCount: 1, readyDelayMs: 0);
        processor.EnsureLayout();
        File.WriteAllText(Path.Combine(processor.InboxDirectory, "a.txt"), "x\n");
        File.WriteAllText(Path.Combine(processor.InboxDirectory, "b..c.txt"), "y\n");   // blockiert

        var run = processor.ProcessPending(new SyncPolicy(tree.Root));
        var path = ImportReportWriter.Write(tree.Root, run, "test");
        var text = File.ReadAllText(path);

        Assert.Contains("Ausgelöst durch: test", text, StringComparison.Ordinal);
        Assert.Contains("| `a.txt` | Written |", text, StringComparison.Ordinal);
        Assert.Contains("blockiert", text, StringComparison.Ordinal);
        Assert.Contains("| Datei | Ergebnis | Notiz |", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Lauf_berichtet_ueber_Summarize_und_Zaehler()
    {
        var leer = new InboxRunResult(Array.Empty<InboxItemResult>(), Array.Empty<string>());
        Assert.True(leer.IsEmpty);
        Assert.Equal("Inbox leer – nichts zu tun.", leer.Summarize());

        var items = new[]
        {
            new InboxItemResult("a", new SyncResult(SyncOutcome.Written, "a", "neu"), true),
            new InboxItemResult("b", new SyncResult(SyncOutcome.UpdatedWithBackup, "b", "ü"), true),
            new InboxItemResult("c", new SyncResult(SyncOutcome.Unchanged, "c", "=c"), true),
            new InboxItemResult("d", new SyncResult(SyncOutcome.ConflictPreserved, "d", "k"), false),
            new InboxItemResult("e", new SyncResult(SyncOutcome.Blocked, "e", "b"), false)
        };
        var run = new InboxRunResult(items, new[] { "Fehler 1" });

        Assert.Equal(1, run.Written);
        Assert.Equal(1, run.Updated);
        Assert.Equal(1, run.Unchanged);
        Assert.Equal(1, run.Conflicts);
        Assert.Equal(1, run.Blocked);
        Assert.Contains("1 Fehler", run.Summarize(), StringComparison.Ordinal);
        Assert.False(run.IsEmpty);
    }

    [Fact]
    public void Zip_Bombe_wird_durch_Verhaeltnis_Grenze_aufgehalten()
    {
        using var staging = new TempTree();
        using var tree = new TempTree();
        var zip = Path.Combine(staging.Root, "bombe.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("bombe.txt");
            using var stream = entry.Open();
            var block = System.Text.Encoding.UTF8.GetBytes(new string('a', 200_000));
            for (var i = 0; i < 12; i++)
            {
                stream.Write(block, 0, block.Length);      // 2,4 MB, extrem komprimierbar
            }
        }

        var report = ArchiveImporter.ImportZip(zip, new SyncPolicy(tree.Root));

        Assert.Equal(1, report.Blocked);
        Assert.False(tree.Exists("bombe.txt"));
        Assert.Contains(report.BlockNotes, n => n.Contains("Verhältnis", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Zip_Ohne_Groessen_Angabe_liest_trotzdem()
    {
        // Archives ohne befüllte Längenfelder (Stream-basiert) dürfen nicht zu Teil-Importen führen.
        using var staging = new TempTree();
        using var tree = new TempTree();
        var zip = Path.Combine(staging.Root, "strom.zip");
        using (var file = File.Create(zip))
        using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
        {
            var e = archive.CreateEntry("drin.txt");
            using var w = new StreamWriter(e.Open());
            w.Write("inhalt\n");
        }

        var report = ArchiveImporter.ImportZip(zip, new SyncPolicy(tree.Root));

        Assert.Equal(1, report.Written);
        Assert.Equal("inhalt\n", tree.Read("drin.txt"));
    }

    [Fact]
    public void Ordner_an_der_Zielstelle_wird_nicht_ueberschrieben()
    {
        using var tree = new TempTree();
        Directory.CreateDirectory(tree.Full("ordner.py"));

        var decision = new ConflictPolicy(ConflictResolution.ArenaWins).Decide(tree.Root, "ordner.py", "x\n");
        Assert.Equal(ConflictDecisionKind.KeptMine, decision.Kind);

        var result = new SyncPolicy(tree.Root).Apply("ordner.py", "x\n", "test");
        Assert.Equal(SyncOutcome.Blocked, result.Outcome);
        Assert.Contains("Ordner gleichen Namens", result.Message, StringComparison.Ordinal);
        Assert.True(Directory.Exists(tree.Full("ordner.py")));   // der Ordner ist noch da
    }

    [Fact]
    public void ArenaCopyName_ohne_Endung_und_mit_Punkt_im_Namen()
    {
        Assert.Equal("Makefile.arena", ConflictPolicy.ArenaCopyName("Makefile"));
        Assert.Equal("a.b.c.arena.txt", ConflictPolicy.ArenaCopyName("a.b.c.txt"));
        Assert.Equal("src/a.arena.md", ConflictPolicy.ArenaCopyName("src/a.md"));
    }

    [Fact]
    public void Standard_Frage_Modus_legt_knappe_Id_an_und_keep_mine_nicht()
    {
        using var tree = new TempTree();
        tree.Write("k.py", "meins\n");

        // Standard ist "Nachfragen": es entsteht eine Anfrage mit kurzer Id und beiden Kopien.
        var asked = new SyncPolicy(tree.Root).Apply("k.py", "arena\n", "test");
        Assert.Equal(SyncOutcome.NeedsDecision, asked.Outcome);
        var first = Assert.Single(DecisionApplier.ReadPending(tree.Root));
        Assert.Equal(12, first.Id.Length);
        Assert.Null(first.Answer);
        Assert.StartsWith(".arena-conflicts/pending/files/", first.Mine!.Replace('\\', '/'), StringComparison.Ordinal);
        Assert.StartsWith(".arena-conflicts/pending/files/", first.Arena!.Replace('\\', '/'), StringComparison.Ordinal);

        // KeepMine dagegen: keine Anfrage, nur die Ablage im Konfliktordner.
        using var other = new TempTree();
        other.Write("k.py", "meins\n");
        var quiet = new SyncPolicy(other.Root, new SyncPolicyOptions { ConflictResolution = ConflictResolution.KeepMine });
        Assert.Equal(SyncOutcome.ConflictPreserved, quiet.Apply("k.py", "arena\n", "test").Outcome);
        Assert.Empty(DecisionApplier.ReadPending(other.Root));
    }

    [Fact]
    public void Unerwuenschter_Archivname_in_der_Vorlage_bleibt_stehen()
    {
        using var tree = new TempTree();
        tree.Write("u.py", "meins\n");
        var policy = new SyncPolicy(tree.Root, new SyncPolicyOptions { ConflictResolution = ConflictResolution.Ask });
        policy.Apply("../../../u.py", "x\n", "test");        // Vorher blockiert, keine Anfrage

        Assert.Empty(DecisionApplier.ReadPending(tree.Root));
        Assert.Null(DecisionApplier.WriteTemplate(tree.Root, _ => { }));
    }

    [Fact]
    public void ProjektSyncService_legt_Inbox_An_und_zaehlt_wartende_Dateien()
    {
        using var tree = new TempTree();
        var log = new List<string>();
        using var service = new ProjectSyncService(tree.Root, new AppSettings { AutoImport = false }, m => log.Add(m));

        Assert.Equal(0, service.CountPendingInboxItems());
        Assert.True(Directory.Exists(service.InboxDirectory));
        Assert.EndsWith(Path.Combine(".arena-inbox", "").TrimEnd(Path.DirectorySeparatorChar), service.InboxDirectory.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);

        File.WriteAllText(Path.Combine(service.InboxDirectory, "w.txt"), "x");
        Assert.Equal(1, service.CountPendingInboxItems());

        // Ordner weg -> 0 wartende Dateien, keine Ausnahme
        Directory.Delete(service.InboxDirectory, recursive: true);
        Assert.Equal(0, service.CountPendingInboxItems());
    }

    [Fact]
    public void AppSettingsStore_legt_Ordner_an_und_kann_leeren_Ordner_verarbeiten()
    {
        using var tree = new TempTree();
        var unter = Path.Combine(tree.Root, "tief", "drin");
        var store = new AppSettingsStore(unter);

        store.Save(new AppSettings { ProjectFolder = tree.Root });

        Assert.True(Directory.Exists(unter));
        Assert.True(File.Exists(store.FilePath));
        Assert.Equal(tree.Root, new AppSettingsStore(unter).Load().ProjectFolder);
    }

    [Fact]
    public void FileHash_und_Konvertierung_helfer()
    {
        var bytes = FileHash.ToBytes("ä\r\n");
        Assert.Equal(4, bytes.Length);                       // ä=2 Byte, CRLF=2
        Assert.Equal(64, FileHash.OfBytes(bytes).Length);    // SHA-256 hex
        Assert.Equal(FileHash.OfText("x"), FileHash.OfBytes(System.Text.Encoding.UTF8.GetBytes("x")));
        Assert.Null(FileHash.OfFile(Path.Combine(Path.GetTempPath(), "gibt-es-nicht-" + Guid.NewGuid().ToString("N"))));

        using var tree = new TempTree();
        tree.Write("d.txt", "inhalt");
        Assert.Equal(64, FileHash.OfFile(tree.Full("d.txt"))!.Length);
    }

    [Fact]
    public void CodeBlockParser_TryExtractPath_alle_Vier_Schreibweisen()
    {
        Assert.Equal("a.py", CodeBlockParser.TryExtractPath("<!-- file: a.py -->"));
        Assert.Equal("a.py", CodeBlockParser.TryExtractPath("# Dateiname: a.py"));
        Assert.Equal("a.py", CodeBlockParser.TryExtractPath("path: a.py"));
        Assert.Equal("src/../a.py", CodeBlockParser.TryExtractPath("path: src/../a.py"));   // Cleaning ohne Bewertung
        Assert.Null(CodeBlockParser.TryExtractPath("   "));
        Assert.Null(CodeBlockParser.TryExtractPath(null));
        Assert.Null(CodeBlockParser.TryExtractPath("x = 1"));
    }
}

/// <summary>Verhalten an den Kanten: kaputte Quellen, volle Pfade, Reporter-Details.</summary>

public class LogTests
{
    [Fact]
    public void Ring_puffert_hoechstens_Capacity_Zeilen()
    {
        var ring = new LogRing(capacity: 50);
        for (var i = 0; i < 500; i++)
        {
            ring.Add("INFO", "zeile " + i);
        }

        Assert.Equal(50, ring.Count);
        var snapshot = ring.Snapshot();
        Assert.Equal("zeile 499", snapshot[^1].Message);
        Assert.Equal("zeile 450", snapshot[0].Message);
    }

    [Fact]
    public void Last_liefert_den_letzten_Eintrag_leer_am_Anfang_null()
    {
        var ring = new LogRing();
        Assert.Null(ring.Last());
        ring.Add("INFO", "hi");
        Assert.Equal("hi", ring.Last()!.Value.Message);
    }

    [Fact]
    public void Event_fuer_jeden_Eintrag()
    {
        var ring = new LogRing();
        var seen = 0;
        ring.EntryAdded += _ => seen++;
        ring.Add("INFO", "a");
        ring.Add("WARN", "b");
        Assert.Equal(2, seen);
    }

    [Fact]
    public void DateiLogger_rotiert_und_schreibt_weiter()
    {
        using var tree = new TempTree();
        var logger = new FileLogger(tree.Root, "app.log", maxBytes: 64 * 1024, keepFiles: 2);

        for (var i = 0; i < 4000; i++)
        {
            logger.Write("INFO", new string('x', 40) + i);
        }

        Assert.True(File.Exists(logger.FilePath));
        Assert.True(File.Exists(logger.FilePath + ".1"), "Rotationsdatei .1 muss existieren");
        logger.Dispose();
    }

    [Fact]
    public void DateiLogger_schluckt_Fehler_nach_Dispose()
    {
        using var tree = new TempTree();
        var logger = new FileLogger(tree.Root);
        logger.Dispose();
        var ex = Record.Exception(() => logger.Write("INFO", "nach dem Schließen"));
        Assert.Null(ex);
    }
}
