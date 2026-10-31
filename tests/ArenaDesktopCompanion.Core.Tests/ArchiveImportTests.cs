using System.IO.Compression;
using ArenaDesktopCompanion.Core.Sync;
using ArenaDesktopCompanion.Core.Tests.Support;

namespace ArenaDesktopCompanion.Core.Tests;

public class ArchiveImporterTests
{
    private static string MakeZip(string directory, params (string Name, string Content)[] entries)
    {
        var zipPath = Path.Combine(directory, "arena-workspace.zip");
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            var entry = archive.CreateEntry(name);
            using var stream = entry.Open();
            using var writer = new StreamWriter(stream);
            writer.Write(content);
        }
        return zipPath;
    }

    [Fact]
    public void Zip_Inhalte_landen_mit_Unterordnern_im_Projekt()
    {
        using var staging = new TempTree();
        using var tree = new TempTree();
        var zip = MakeZip(staging.Root,
            ("main.py", "print('v2')\n"),
            ("src/util.py", "x = 2\n"));

        var report = ArchiveImporter.ImportZip(zip, new SyncPolicy(tree.Root));

        Assert.Equal(2, report.Written);
        Assert.Equal("print('v2')\n", tree.Read("main.py"));
        Assert.Equal("x = 2\n", tree.Read("src/util.py"));
    }

    [Fact]
    public void Top_Ordner_des_Zips_wird_entfernt()
    {
        using var staging = new TempTree();
        using var tree = new TempTree();
        var zip = MakeZip(staging.Root, ("session-42/main.py", "a\n"), ("session-42/src/u.py", "b\n"));

        ArchiveImporter.ImportZip(zip, new SyncPolicy(tree.Root));

        Assert.True(tree.Exists("main.py"));
        Assert.False(tree.Exists("session-42/main.py"));
    }

    [Fact]
    public void Boese_Eintrag_wird_blockiert_Gute_kommen_durch()
    {
        // Das ist der Unterschied zu ZipFile.ExtractToDirectory: dort wirft ein einziger
        // "../"-Eintrag den kompletten Import um – hier wird nur dieser Eintrag abgelehnt.
        using var staging = new TempTree();
        using var tree = new TempTree();
        var zip = MakeZip(staging.Root,
            ("ok.py", "fein\n"),
            ("../ daneben.py", "HACK\n"));

        var report = ArchiveImporter.ImportZip(zip, new SyncPolicy(tree.Root));

        Assert.Equal(1, report.Written);
        Assert.Equal(1, report.Blocked);
        Assert.True(tree.Exists("ok.py"));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(tree.Root)!, " daneben.py")));
        Assert.Contains(report.BlockNotes, n => n.Contains("blockiert", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Absolute_Laufwerkspfade_im_Zip_werden_blockiert()
    {
        using var staging = new TempTree();
        using var tree = new TempTree();
        var zip = MakeZip(staging.Root, ("C:/windows/system32/evil.dll", "x\n"));

        var report = ArchiveImporter.ImportZip(zip, new SyncPolicy(tree.Root));

        Assert.Equal(1, report.Blocked);
        Assert.Empty(tree.AllFiles().Where(f => !f.StartsWith(".arena", StringComparison.Ordinal)));
    }

    [Fact]
    public void Leeres_Archiv_ist_kein_Fehler()
    {
        using var staging = new TempTree();
        using var tree = new TempTree();
        var zip = MakeZip(staging.Root);

        var report = ArchiveImporter.ImportZip(zip, new SyncPolicy(tree.Root));

        Assert.Equal(0, report.Entries);
        Assert.Equal(0, report.Failed);
    }

    [Fact]
    public void Keine_Zip_Datei_wird_gemeldet_statt_gecrasht()
    {
        using var tree = new TempTree();
        tree.Write("not-a-zip.zip", "das hier ist kein zip-archiv");

        var report = ArchiveImporter.ImportZip(tree.Full("not-a-zip.zip"), new SyncPolicy(tree.Root));

        Assert.Equal(1, report.Failed);
        Assert.Contains("kein gültiges Zip", report.BlockNotes[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Ubergrosse_Datei_im_Archiv_wird_blockiert()
    {
        using var staging = new TempTree();
        using var tree = new TempTree();
        var zip = MakeZip(staging.Root, ("big.txt", new string('x', 5000)));

        var policy = new SyncPolicy(tree.Root, new SyncPolicyOptions { MaxFileBytes = 1000 });
        var report = ArchiveImporter.ImportZip(zip, policy);

        Assert.Equal(1, report.Blocked);
        Assert.False(tree.Exists("big.txt"));
    }

    [Fact]
    public void Zweiter_Import_desselben_Archivs_schreibt_nichts_mehr()
    {
        using var staging = new TempTree();
        using var tree = new TempTree();
        var zip = MakeZip(staging.Root, ("a.py", "1\n"), ("b.py", "2\n"));

        var policy = new SyncPolicy(tree.Root);
        var first = ArchiveImporter.ImportZip(zip, policy);
        var second = ArchiveImporter.ImportZip(zip, policy);

        Assert.Equal(2, first.Written);
        Assert.Equal(0, second.Written);
        Assert.Equal(0, second.Updated);
        Assert.Equal(2, second.Unchanged);
    }

    [Fact]
    public void Verzeichnis_Eintraege_und_Systemmuell_werden_uebersprungen()
    {
        using var staging = new TempTree();
        using var tree = new TempTree();
        var zipPath = Path.Combine(staging.Root, "mixed.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            archive.CreateEntry("src/");                       // Ordner-Eintrag
            var e = archive.CreateEntry("src/a.py");
            using (var w = new StreamWriter(e.Open())) w.Write("x\n");
            var d = archive.CreateEntry("__MACOSX/._a.py");
            using (var w = new StreamWriter(d.Open())) w.Write("müll\n");
            var g = archive.CreateEntry(".git/config");
            using (var w = new StreamWriter(g.Open())) w.Write("[core]\n");
        }

        var report = ArchiveImporter.ImportZip(zipPath, new SyncPolicy(tree.Root));

        Assert.Equal(1, report.Written);
        Assert.True(report.Skipped >= 2);
        Assert.Equal("x\n", tree.Read("src/a.py"));
        Assert.False(tree.Exists("__MACOSX/._a.py"));
        Assert.False(tree.Exists(".git/config"));
    }
}

public class InboxProcessorTests
{
    [Fact]
    public void Einfache_Datei_aus_der_Inbox_wird_ubernommen_und_wegeraeumt()
    {
        using var tree = new TempTree();
        var processor = new InboxProcessor(tree.Root, readyRetryCount: 2, readyDelayMs: 0);
        processor.EnsureLayout();
        File.WriteAllText(processor.InboxDirectory + "/hello.txt", "welt\n");

        var policy = new SyncPolicy(tree.Root);
        var run = processor.ProcessPending(policy);

        Assert.Equal("welt\n", tree.Read("hello.txt"));
        Assert.Equal(1, run.Written);
        Assert.Empty(Directory.GetFiles(processor.InboxDirectory));         // nichts bleibt liegen
        Assert.Single(Directory.GetFiles(Path.Combine(processor.InboxDirectory, "processed")));
    }

    [Fact]
    public void Zweiter_Durchlauf_ohne_Nachschub_ist_leer()
    {
        using var tree = new TempTree();
        var processor = new InboxProcessor(tree.Root, readyRetryCount: 2, readyDelayMs: 0);
        processor.EnsureLayout();

        var run = processor.ProcessPending(new SyncPolicy(tree.Root));

        Assert.True(run.IsEmpty);
    }

    [Fact]
    public void Zip_in_der_Inbox_wird_verteilt()
    {
        using var tree = new TempTree();
        var processor = new InboxProcessor(tree.Root, readyRetryCount: 2, readyDelayMs: 0);
        processor.EnsureLayout();

        var zipPath = Path.Combine(tree.Root, "pack.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var e = archive.CreateEntry("run-7/src/app.py");
            using var w = new StreamWriter(e.Open());
            w.Write("print(1)\n");
        }
        File.Move(zipPath, Path.Combine(processor.InboxDirectory, "workspace.zip"));

        var run = processor.ProcessPending(new SyncPolicy(tree.Root));

        Assert.Equal("print(1)\n", tree.Read("src/app.py"));
        Assert.Equal(1, run.Written);
    }

    [Fact]
    public void Punkt_Dateien_bleiben_in_der_Inbox_liegen()
    {
        using var tree = new TempTree();
        var processor = new InboxProcessor(tree.Root, readyRetryCount: 2, readyDelayMs: 0);
        processor.EnsureLayout();
        File.WriteAllText(Path.Combine(processor.InboxDirectory, ".part"), "hallo");

        var run = processor.ProcessPending(new SyncPolicy(tree.Root));

        Assert.True(run.IsEmpty);
        Assert.True(File.Exists(Path.Combine(processor.InboxDirectory, ".part")));
    }

    [Fact]
    public void Blockierter_Name_in_der_Inbox_wird_gemeldet_nicht_geschrieben()
    {
        using var tree = new TempTree();
        var processor = new InboxProcessor(tree.Root, readyRetryCount: 2, readyDelayMs: 0);
        processor.EnsureLayout();
        File.WriteAllText(Path.Combine(processor.InboxDirectory, "a..b.py"), "x");

        var run = processor.ProcessPending(new SyncPolicy(tree.Root));

        Assert.Contains(run.Items, i => i.Result.Outcome == SyncOutcome.Blocked);
        Assert.False(tree.Exists("a..b.py"));
    }
}

public class ReportWriterTests
{
    [Fact]
    public void Bericht_nennt_Ergebnis_und_Pfad()
    {
        using var tree = new TempTree();
        var processor = new InboxProcessor(tree.Root, readyRetryCount: 2, readyDelayMs: 0);
        processor.EnsureLayout();
        File.WriteAllText(Path.Combine(processor.InboxDirectory, "a.txt"), "inhalt\n");

        var run = processor.ProcessPending(new SyncPolicy(tree.Root));
        var path = ImportReportWriter.Write(tree.Root, run, "test");

        var text = File.ReadAllText(path);
        Assert.Contains("# Arena-Import", text, StringComparison.Ordinal);
        Assert.Contains("a.txt", text, StringComparison.Ordinal);
        Assert.Contains("Written", text, StringComparison.Ordinal);
        Assert.EndsWith(".md", path);
    }
}
