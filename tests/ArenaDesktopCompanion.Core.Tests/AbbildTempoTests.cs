using ArenaDesktopCompanion.Core.Bridge;
using ArenaDesktopCompanion.Core.Tests.Support;

namespace ArenaDesktopCompanion.Core.Tests;

/// <summary>
/// Befund vom 24.09.2026, live: Im Projektordner lag ein entpackter PostgreSQL-Baum
/// (~21 900 Dateien, ~1,4 GB). Das Ordner-Abbild rechnete bei jedem Takt (alle 20 s) für jede Datei
/// einen SHA-256 – der Takt dauerte Minuten, die Brücke schwieg, und von außen sah es aus, als wäre
/// der Companion beendet („Brücke seit 09:55:14 tot"). Diese Prüfungen halten beide Umbauten fest:
/// den Hash-Cache (nur Geändertes wird gerechnet) und die Zeitgrenze im Takt.
/// </summary>
public class AbbildTempoTests
{
    [Fact]
    public void Bekannte_Datei_wird_beim_zweiten_Lauf_nicht_erneut_gerechnet()
    {
        using var tree = new TempTree();
        tree.Write("a.txt", "inhalt A");

        var cache = new HashCache();
        var erster = WorkspaceManifestBuilder.Build(tree.Root, cache: cache);
        var zweiter = WorkspaceManifestBuilder.Build(tree.Root, cache: cache);

        Assert.Single(erster.Files);
        Assert.Equal(erster.Files[0].Hash, zweiter.Files[0].Hash);
        Assert.Equal(1, cache.NeuBerechnet);
        Assert.Equal(1, cache.Treffer);
        Assert.Equal(1, erster.Counts["hashNeu"]);
        Assert.Equal(1, zweiter.Counts["hashTreffer"]);
    }

    [Fact]
    public void Geaenderte_Datei_wird_neu_gerechnet()
    {
        using var tree = new TempTree();
        tree.Write("a.txt", "inhalt A");
        var cache = new HashCache();
        var erster = WorkspaceManifestBuilder.Build(tree.Root, cache: cache);

        // Gleiche Länge, anderer Inhalt und andere Änderungszeit: muss auffallen.
        Thread.Sleep(20);
        tree.Write("a.txt", "inhalt B");
        var zweiter = WorkspaceManifestBuilder.Build(tree.Root, cache: cache);

        Assert.NotEqual(erster.Files[0].Hash, zweiter.Files[0].Hash);
        Assert.Equal(2, cache.NeuBerechnet);
    }

    /// <summary>
    /// Befund vom 26.09.2026, live: Der Projektordner eines Chats enthielt <b>347 715</b> Dateien
    /// (Werkzeug-Ablagen, Bauausgaben, Crawl-Reste). Das Abbild wurde größer als die 4 MB, die der
    /// Briefkasten aufnimmt – die Brücke blieb dauerhaft auf „gestört: Datei zu groß für den
    /// Briefkasten", und der Chat bekam gar nichts mehr. Diese Prüfungen halten beide Gegenmittel
    /// fest: die harte Größenobergrenze und die erweiterte Sprungliste.
    /// </summary>
    [Fact]
    public void Ein_Monsterordner_sprengt_den_Briefkasten_nicht()
    {
        using var tree = new TempTree();
        for (var i = 0; i < 500; i++)
        {
            tree.Write($"ordner{i % 20}/datei{i:0000}.txt", $"inhalt {i}");
        }

        // Winziges Budget: die Grenze muss halten, egal wie viele Dateien da sind.
        var abbild = WorkspaceManifestBuilder.Build(tree.Root, maxBytes: 5_000);
        var json = WorkspaceManifestBuilder.ToJson(abbild);

        Assert.Equal(1, abbild.Counts["truncated"]);
        Assert.InRange(abbild.Files.Count, 1, 60);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(json) < 30_000,
            $"Abbild zu groß: {System.Text.Encoding.UTF8.GetByteCount(json)} Bytes.");
        Assert.Contains("gekürzt", WorkspaceManifestBuilder.ToMarkdown(abbild), StringComparison.Ordinal);

        // Und ohne Truncation bleibt alles wie vorher.
        var voll = WorkspaceManifestBuilder.Build(tree.Root);
        Assert.Equal(0, voll.Counts["truncated"]);
        Assert.Equal(500, voll.Files.Count);
    }

    [Fact]
    public void Werkzeug_und_Bauordner_landen_nicht_im_Abbild()
    {
        using var tree = new TempTree();
        tree.Write("src/app.py", "x");
        tree.Write("dist/bundle.js", "x");
        tree.Write(".venv/lib/site.py", "x");
        tree.Write(".dotnet/sdk/x.txt", "x");
        tree.Write(".nuget/pakete/x.txt", "x");
        tree.Write(".templateengine/t.txt", "x");
        tree.Write("node_modules/paket/index.js", "x");

        var abbild = WorkspaceManifestBuilder.Build(tree.Root);

        Assert.Single(abbild.Files);
        Assert.Equal("src/app.py", abbild.Files[0].Path);
    }

    [Fact]
    public void Ohne_Cache_bleibt_alles_wie_vorher()
    {
        // Der Cache ist eine Zugabe: wer ihn nicht übergibt, bekommt dieselben Hashes wie eh und je.
        using var tree = new TempTree();
        tree.Write("a.txt", "inhalt A");

        var ohneCache = WorkspaceManifestBuilder.Build(tree.Root);
        var mitCache = WorkspaceManifestBuilder.Build(tree.Root, cache: new HashCache());

        Assert.Equal(ohneCache.Files[0].Hash, mitCache.Files[0].Hash);
        Assert.Equal(0, ohneCache.Counts["hashTreffer"]);
    }

    [Fact]
    public void Ein_grosser_Baum_kostet_beim_zweiten_Mal_keine_Rechenzeit()
    {
        // Der eigentliche Beweis: 2 000 Dateien, zweiter Durchlauf ohne einen einzigen Hash.
        using var tree = new TempTree();
        for (var i = 0; i < 2000; i++)
        {
            tree.Write($"unter/datei-{i:D4}.txt", $"inhalt {i}");
        }

        var cache = new HashCache();
        var erster = WorkspaceManifestBuilder.Build(tree.Root, cache: cache);
        var zweiter = WorkspaceManifestBuilder.Build(tree.Root, cache: cache);

        Assert.Equal(2000, erster.Files.Count);
        Assert.Equal(2000, cache.NeuBerechnet);
        Assert.Equal(2000, cache.Treffer);
        Assert.Equal(0, zweiter.Counts["hashNeu"]);
        Assert.Equal(2000, zweiter.Counts["hashTreffer"]);
    }

    [Fact]
    public async Task Zeitgrenze_gibt_auf_und_laesst_die_Aufgabe_weiterlaufen()
    {
        // Ein hängendes Abbild darf den Takt nicht anhalten: nach der Grenze kommt null zurück,
        // die Aufgabe selbst läuft weiter und wird nie zu einer unbeobachteten Ausnahme.
        var tor = new TaskCompletionSource<string>();

        var ergebnis = await BridgeService.MitZeitgrenzeAsync(tor.Task, TimeSpan.FromMilliseconds(80));

        Assert.Null(ergebnis);
        Assert.False(tor.Task.IsCompleted);

        tor.SetResult("spät fertig");            // läuft weiter – und stört niemanden mehr
        Assert.Equal("spät fertig", await tor.Task);
    }

    [Fact]
    public async Task Zeitgrenze_gibt_das_Ergebnis_zurueck_wenn_es_rechtzeitig_kommt()
    {
        var schnell = Task.FromResult("fertig");

        var ergebnis = await BridgeService.MitZeitgrenzeAsync(schnell, TimeSpan.FromSeconds(5));

        Assert.Equal("fertig", ergebnis);
    }

    [Fact]
    public async Task Zeitgrenze_wirft_bei_fehlerhafter_Aufgabe_nicht()
    {
        // Der Takt darf an einem fehlerhaften Abbild nicht scheitern – er bekommt null und macht weiter.
        var kaputt = Task.FromException<string>(new IOException("Ordner gesperrt"));

        var ergebnis = await BridgeService.MitZeitgrenzeAsync(kaputt, TimeSpan.FromSeconds(1));

        Assert.Null(ergebnis);
    }
}
