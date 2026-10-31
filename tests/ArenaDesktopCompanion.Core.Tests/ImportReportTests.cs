using ArenaDesktopCompanion.Core.Sync;

namespace ArenaDesktopCompanion.Core.Tests;

/// <summary>
/// Die Berichte: einer je Thema (Chat), nicht einer je Lauf. Vorher entstand bei jedem Watcher-Lauf
/// eine neue Datei – an einem Nachmittag über hundert, jede mit drei Zeilen. Diese Fälle halten die
/// neue Regel fest, samt dem, was schiefgehen kann (Namen aus einer Adresse, Anhängen statt
/// Überschreiben, kaputte Adresse).
/// </summary>
public class ImportReportTests
{
    private static string Reports(string root) => Path.Combine(root, InboxProcessor.InboxFolderName, "reports");

    [Fact]
    public void Zweiter_Lauf_landet_in_derselben_Datei_wie_der_erste()
    {
        using var tree = new Support.TempTree();
        var lauf1 = Lauf(("a.py", SyncOutcome.Written, "neu geschrieben"));
        var lauf2 = Lauf(("a.py", SyncOutcome.Unchanged, "unverändert"));

        var ersterPfad = ImportReportWriter.Write(tree.Root, lauf1, "Watcher", "chat-abc123");
        var zweiterPfad = ImportReportWriter.Write(tree.Root, lauf2, "Watcher", "chat-abc123");

        Assert.Equal(ersterPfad, zweiterPfad);
        Assert.Single(Directory.GetFiles(Reports(tree.Root), "*.md"));

        var text = File.ReadAllText(ersterPfad);
        Assert.Contains("unverändert", text);   // neuer Lauf steht drin
        Assert.Contains("neu geschrieben", text); // älterer Lauf bleibt erhalten
    }

    [Fact]
    public void Neuester_Lauf_steht_oben_und_der_Aufbau_bleibt_lesbar()
    {
        using var tree = new Support.TempTree();
        ImportReportWriter.Write(tree.Root, Lauf(("alt.py", SyncOutcome.Written, "alt")), "erster",
            "chat-abc123", new DateTime(2026, 9, 21, 10, 0, 0));
        var pfad = ImportReportWriter.Write(tree.Root, Lauf(("neu.py", SyncOutcome.Written, "neu")), "zweiter",
            "chat-abc123", new DateTime(2026, 9, 21, 11, 0, 0));

        var text = File.ReadAllText(pfad);
        Assert.Equal(1, text.Split("# Arena-Import – Bericht").Length - 1); // genau ein Kopf
        Assert.True(text.IndexOf("## 2026-09-21 11:00:00", StringComparison.Ordinal)
                  < text.IndexOf("## 2026-09-21 10:00:00", StringComparison.Ordinal));
        Assert.Contains("| `neu.py` |", text);
        Assert.Contains("| `alt.py` |", text);
    }

    [Fact]
    public void Verschiedene_Chats_bekommen_verschiedene_Dateien()
    {
        using var tree = new Support.TempTree();
        var a = ImportReportWriter.Write(tree.Root, Lauf(("a.py", SyncOutcome.Written, "x")), "Watcher", "chat-eins");
        var b = ImportReportWriter.Write(tree.Root, Lauf(("b.py", SyncOutcome.Written, "y")), "Watcher", "chat-zwei");

        Assert.NotEqual(a, b);
        Assert.Equal(2, Directory.GetFiles(Reports(tree.Root), "*.md").Length);
    }

    [Fact]
    public void Ohne_Chat_heisst_der_Bericht_inbox()
    {
        using var tree = new Support.TempTree();
        var pfad = ImportReportWriter.Write(tree.Root, Lauf(("a.py", SyncOutcome.Written, "x")), "Watcher");
        Assert.Equal("inbox.md", Path.GetFileName(pfad));
    }

    [Theory]
    [InlineData("", "inbox")]
    [InlineData("   ", "inbox")]
    [InlineData("chat-abc123", "chat-abc123")]
    [InlineData("../../etc/passwd", "etc-passwd")]          // kein Ausbruch aus dem Ordner
    [InlineData("a b:c\\d*e", "a-b-c-d-e")]                // Windows-Verbotenes wird zu "-"
    [InlineData("---", "inbox")]                            // nichts Brauchbares übrig
    public void Der_Themaname_ist_immer_ein_sicherer_Dateiname(string thema, string erwartet)
    {
        var name = ImportReportWriter.Sanitize(thema);
        Assert.Equal(erwartet, name);
        Assert.DoesNotContain("..", name);
        Assert.DoesNotContain(Path.DirectorySeparatorChar, name);
        Assert.All(Path.GetInvalidFileNameChars(), c => Assert.DoesNotContain(c, name));
    }

    [Theory]
    // Die Adresse aus dem Fenster des Nutzers, wörtlich.
    [InlineData("https://arena.ai/agent/0fdc154-7547-adf1-b7f3-b6edb7d58c9", "chat-0fdc154-7547-adf1-b7f3-b6edb7d58c9")]
    [InlineData("https://arena.ai/agent/abcdef", "chat-abcdef")]
    [InlineData("https://arena.ai/chat/abc123?x=1", "chat-abc123")]
    [InlineData("https://arena.ai/thread/abc123/sub", "chat-abc123")]
    // Kein Chat in der Adresse: dann der Sammelbericht.
    [InlineData("https://arena.ai/", "inbox")]
    [InlineData("https://arena.ai/download-workspace", "inbox")]
    [InlineData("https://arena.ai/agent/ab", "inbox")]     // zu kurz, kein Chat
    [InlineData("https://arena.ai/agent/a b c d", "inbox")]
    [InlineData("", "inbox")]
    [InlineData("keine-adresse", "inbox")]
    public void Das_Thema_kommt_aus_der_Chat_Adresse(string url, string erwartet)
        => Assert.Equal(erwartet, ImportReportWriter.TopicFromUrl(url));

    [Fact]
    public void Das_Thema_aus_der_Adresse_ist_ein_sicherer_Dateiname()
    {
        var thema = ImportReportWriter.TopicFromUrl("https://arena.ai/agent/0fdc154-7547-adf1?b=1#x");
        Assert.Equal("chat-0fdc154-7547-adf1", thema);
        Assert.All(Path.GetInvalidFileNameChars(), c => Assert.DoesNotContain(c, ImportReportWriter.Sanitize(thema)));
    }

    [Fact]
    public void Sehr_langer_Themaname_wird_gekuerzt()
    {
        var name = ImportReportWriter.Sanitize(new string('a', 200));
        Assert.True(name.Length <= 60, $"Länge {name.Length}");
    }

    [Fact]
    public void Die_Zieldatei_wird_atomar_ersetzt_und_hinterlaesst_keinen_Rest()
    {
        using var tree = new Support.TempTree();
        var pfad = ImportReportWriter.Write(tree.Root, Lauf(("a.py", SyncOutcome.Written, "x")), "Watcher", "chat-a");
        ImportReportWriter.Write(tree.Root, Lauf(("b.py", SyncOutcome.Written, "y")), "Watcher", "chat-a");

        Assert.True(File.Exists(pfad));
        Assert.Empty(Directory.GetFiles(Reports(tree.Root), "*.tmp"));
    }

    [Fact]
    public void Leerer_Lauf_steht_als_Zeile_im_Bericht_statt_als_leere_Tabelle()
    {
        using var tree = new Support.TempTree();
        var pfad = ImportReportWriter.Write(tree.Root, Lauf(), "Inbox prüfen", "chat-a");
        Assert.Contains("Keine Dateien in diesem Lauf.", File.ReadAllText(pfad));
    }

    [Fact]
    public void Pfad_und_Dateiname_passen_zusammen()
    {
        using var tree = new Support.TempTree();
        var pfad = ImportReportWriter.Write(tree.Root, Lauf(("a.py", SyncOutcome.Written, "x")), "Watcher", "chat-a");
        Assert.Equal(ImportReportWriter.PathFor(tree.Root, "chat-a"), pfad);
        Assert.Equal("chat-a.md", ImportReportWriter.FileName("chat-a"));
    }

    private static InboxRunResult Lauf(params (string Pfad, SyncOutcome Ergebnis, string Meldung)[] eintraege)
    {
        var items = eintraege
            .Select(e => new InboxItemResult(e.Pfad + ".zip",
                new SyncResult(e.Ergebnis, e.Pfad, e.Meldung, 12), true))
            .ToList();
        return new InboxRunResult(items, Array.Empty<string>());
    }
}
