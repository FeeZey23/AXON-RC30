using System.Net;
using System.Text.Json;
using ArenaDesktopCompanion.Core.Agents;
using ArenaDesktopCompanion.Core.Bridge;

namespace ArenaDesktopCompanion.Core.Tests;

/// <summary>
/// Die Brücke (Phase 8): das kompakte Abbild des Arbeitsordners und die Aufträge, die darüber
/// laufen dürfen. Die Zusagen, die hier festgehalten werden: ein Abbild beschreibt den Ordner
/// vollständig, ohne Inhalte zu übertragen und ohne Build-Müll; ein Auftrag kann nichts, was nicht
/// ausdrücklich erlaubt ist; kein Kennwort und kein Pfad entkommen.
/// </summary>
public class BridgeTests
{
    private const string Token = "geheim-1234";

    [Fact]
    public async Task Background_write_does_not_block_ping_and_does_not_replay()
    {
        using var tree=new Support.TempTree();var mailbox=new SammelBriefkasten();using var http=new HttpClient(mailbox);using var relay=new RelayClient(http){Bin="adc-aaaaaaaaaaaaaaaaaaaaaaaa"};
        using var release=new ManualResetEventSlim();var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var writes=0;
        var service=new BridgeService(relay,new CommandRunner(),_=>null,(_,_)=>{started.TrySetResult();if(!release.Wait(TimeSpan.FromSeconds(10)))throw new TimeoutException();Interlocked.Increment(ref writes);return "written";},()=>Token,()=>WorkspaceManifestBuilder.Build(tree.Root)){ProjectRoot=tree.Root,SchreibendErlaubt=true,BackgroundCommands=true};
        mailbox.Abgelegt[BridgeJob.DateiName("slow-write")]=BridgeProtocol.ToJson(Job("write",path:"a.txt",content:"x",id:"slow-write"));
        try
        {
            await service.TaktAsync().WaitAsync(TimeSpan.FromSeconds(3));await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            mailbox.Abgelegt[BridgeJob.DateiName("ping-live")]=BridgeProtocol.ToJson(Job("ping",id:"ping-live"));
            await service.TaktAsync().WaitAsync(TimeSpan.FromSeconds(3));Assert.True(mailbox.Abgelegt.ContainsKey(BridgeJob.ErgebnisName("ping-live")));Assert.Equal(0,writes);
        }
        finally{release.Set();await service.WaitForPublicationAsync();}
        await service.TaktAsync();await service.WaitForPublicationAsync();Assert.Equal(1,writes);
    }

    // ============================ Abbild ============================

    [Fact]
    public void Das_Abbild_zaehlt_Dateien_und_Groessen_richtig()
    {
        using var tree = new Support.TempTree();
        tree.Write("src/app.py", "print(1)");
        tree.Write("docs/README.md", "hallo");
        tree.Write("a.txt", "x");

        var abbild = WorkspaceManifestBuilder.Build(tree.Root, "test-pc", "0.2.0-rc6");

        Assert.Equal(3, abbild.Counts["files"]);
        Assert.Equal("print(1)".Length + "hallo".Length + 1, abbild.Counts["bytes"]);
        Assert.Equal("test-pc", abbild.Host);
        Assert.Equal(WorkspaceManifest.Version, abbild.SchemaVersion);
        Assert.Contains(abbild.Files, f => f.Path == "src/app.py");
    }

    [Fact]
    public void Das_Abbild_laesst_Build_und_Systemordner_weg()
    {
        using var tree = new Support.TempTree();
        tree.Write("src/app.py", "x");
        tree.Write("bin/Debug/app.dll", "riesig");
        tree.Write("obj/projekt.assets.json", "{}");
        tree.Write(".git/config", "[core]");
        tree.Write(".arena-inbox/downloads/x.zip", "zip");
        tree.Write("node_modules/paket/index.js", "js");

        var abbild = WorkspaceManifestBuilder.Build(tree.Root);

        Assert.Single(abbild.Files);
        Assert.Equal("src/app.py", abbild.Files[0].Path);
    }

    [Fact]
    public void Gleicher_Inhalt_ergibt_gleichen_Hash_und_Aenderung_anderen()
    {
        using var tree = new Support.TempTree();
        tree.Write("a.txt", "inhalt");
        var ersteDatei = WorkspaceManifestBuilder.Build(tree.Root).Files.Single();

        tree.Write("a.txt", "inhalt");   // gleicher Inhalt
        var nochmal = WorkspaceManifestBuilder.Build(tree.Root).Files.Single();
        Assert.Equal(ersteDatei.Hash, nochmal.Hash);

        tree.Write("a.txt", "anderer inhalt");
        var geaendert = WorkspaceManifestBuilder.Build(tree.Root).Files.Single();
        Assert.NotEqual(ersteDatei.Hash, geaendert.Hash);
    }

    [Fact]
    public void Der_Vergleich_erkennt_neu_geaendert_und_entfernt()
    {
        using var tree = new Support.TempTree();
        tree.Write("bleibt.txt", "1");
        tree.Write("aendert.txt", "alt");
        tree.Write("verschwindet.txt", "weg");
        var vorher = WorkspaceManifestBuilder.Build(tree.Root);

        tree.Write("aendert.txt", "neu");
        File.Delete(Path.Combine(tree.Root, "verschwindet.txt"));
        tree.Write("kommt.txt", "hier");
        var nachher = WorkspaceManifestBuilder.Build(tree.Root);

        var (dazu, anders, weg) = WorkspaceManifestBuilder.Diff(vorher, nachher);
        Assert.Equal(["kommt.txt"], dazu);
        Assert.Equal(["aendert.txt"], anders);
        Assert.Equal(["verschwindet.txt"], weg);
    }

    [Fact]
    public void Die_lesbare_Fassung_zeigt_Kennzahlen_und_keinen_Dateibaum()
    {
        using var tree = new Support.TempTree();
        for (var i = 0; i < 50; i++)
        {
            tree.Write($"src/modul{i:00}.py", new string('x', 100 + i));
        }

        var abbild = WorkspaceManifestBuilder.Build(tree.Root, "pc", "1.0");
        var text = WorkspaceManifestBuilder.ToMarkdown(abbild);

        Assert.Contains("Arbeitsordner-Abbild", text);
        Assert.Contains("50 Dateien", text);
        Assert.Contains("| `src` | 50 |", text);
        // Der Baum steht in der JSON-Datei, die lesbare Fassung bleibt kurz genug zum Lesen.
        Assert.True(text.Length < 4000, $"Zu lang: {text.Length}");
    }

    [Fact]
    public void Das_Abbild_enthaelt_keine_Schluessel_aus_dem_Protokoll()
    {
        using var tree = new Support.TempTree();
        tree.Write("a.txt", "x");
        var abbild = WorkspaceManifestBuilder.Build(tree.Root,
            logZeilen: ["Lade Konfiguration", "API_KEY=sk-abcdefghijklmnopqrstuvwx"]);

        Assert.DoesNotContain("sk-abcdefghijklmnopqrstuvwx", string.Join('\n', abbild.Log));
        Assert.Contains(SecretRedaction.Platzhalter, string.Join('\n', abbild.Log));
    }

    [Fact]
    public void Das_JSON_ist_kompakt_genug_fuer_viele_Dateien()
    {
        using var tree = new Support.TempTree();
        for (var i = 0; i < 500; i++)
        {
            tree.Write($"src/tief/modul{i:000}/{new string('n', 20)}{i:000}.cs", "x");
        }

        var json = WorkspaceManifestBuilder.ToJson(WorkspaceManifestBuilder.Build(tree.Root));
        var proDatei = (double)json.Length / 500;
        Assert.True(proDatei < 200, $"Zu gross: {proDatei:0} Zeichen je Datei");
    }

    // ============================ Auftragsprüfung ============================

    private static BridgeJob Job(string kind, string? command = null, string? path = null, string? content = null,
        string token = Token, string id = "j1", string? note = null)
        => new() { Id = id, Kind = kind, Command = command, Path = path, Content = content, Token = token, Note = note };

    [Fact]
    public void Ohne_richtiges_Kennwort_passiert_nichts()
    {
        var (ok, fehler) = BridgeProtocol.Pruefe(Job("manifest", token: "falsch"), Token, Projekt, true, true, true);
        Assert.False(ok);
        Assert.Contains("Kennwort", fehler);
    }

    [Fact]
    public void Ein_unbekannter_Befehl_wird_abgelehnt()
    {
        var (ok, fehler) = BridgeProtocol.Pruefe(Job("run", "rm -rf /"), Token, Projekt, true, true, true);
        Assert.False(ok);
        Assert.Contains("nicht in der Liste", fehler);
    }

    [Fact]
    public void Ein_Befehl_aus_der_Liste_ist_erlaubt_wenn_freigegeben()
    {
        Assert.True(Pruefe(Job("run", "test"), befehle: true));
        Assert.False(Pruefe(Job("run", "test"), befehle: false));
    }

    /// <summary>Prüft einen Auftrag so, wie es der Dienst tut – mit festem Projektordner.</summary>
    private static bool Pruefe(BridgeJob job, bool befehle = true, bool schreibend = true, bool vorschlaege = true)
        => BridgeProtocol.Pruefe(job, Token, Projekt, befehle, schreibend, vorschlaege).Ok;

    private static string Projekt => Path.Combine(Path.GetTempPath(), "bridge-projekt");

    [Fact]
    public void Schreibende_Befehle_brauchen_die_zweite_Freigabe()
    {
        Assert.False(Pruefe(Job("run", "publish"), schreibend: false));
        Assert.True(Pruefe(Job("run", "publish"), schreibend: true));
    }

    [Theory]
    [InlineData("../ausserhalb.txt")]
    [InlineData("../../etc/passwd")]
    [InlineData("C:/Windows/system32/config")]
    [InlineData("/etc/passwd")]
    [InlineData(@"\\server\share\datei")]
    public void Ein_Pfad_ausserhalb_des_Projektordners_wird_abgelehnt(string pfad)
    {
        var (ok, fehler) = BridgeProtocol.Pruefe(Job("read", path: pfad), Token, @"C:\projekt", true, true, true);
        Assert.False(ok);
        Assert.Contains("Projektordner", fehler, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Ein_Pfad_im_Projektordner_ist_erlaubt()
    {
        var (ok, _) = BridgeProtocol.Pruefe(Job("read", path: "src/app.py"), Token, Projekt, true, true, true);
        Assert.True(ok);
    }

    /// <summary>
    /// Master-Check 24.09.2026 (M1): Der Schreibweg der Brücke hatte eine eigene, schwächere
    /// Pfadprüfung. Verbote, die der Import seit Phase 1 kennt, gingen dort durch – darunter der
    /// Doppelpunkt, mit dem Windows einen zweiten Datenstrom in derselben Datei anlegt.
    /// </summary>
    [Theory]
    [InlineData("datei.txt:geheim")]
    [InlineData("unterordner/bericht.txt:zwei")]
    [InlineData("CON")]
    [InlineData("unterordner/NUL")]
    [InlineData("com1")]
    [InlineData("bericht.")]
    public void Auch_der_Brueckenweg_verbietet_was_der_Import_verbietet(string pfad)
    {
        var (ok, fehler) = BridgeProtocol.Pruefe(Job("write", path: pfad, content: "x"), Token, Projekt, true, true, true);
        Assert.False(ok);
        Assert.Contains("Projektordner", fehler, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Ein_gewoehnlicher_Schreibpfad_bleibt_erlaubt()
    {
        var (ok, fehler) = BridgeProtocol.Pruefe(
            Job("write", path: "docs/bericht-2026.md", content: "inhalt"), Token, Projekt, true, true, true);
        Assert.True(ok, fehler);
    }

    [Fact]
    public void Ein_Auftrag_ohne_Art_wird_abgelehnt()
    {
        var (ok, fehler) = BridgeProtocol.Pruefe(Job(""), Token, Projekt, true, true, true);
        Assert.False(ok);
        Assert.Contains("Auftragsart", fehler);
    }

    [Fact]
    public void Ein_Schreibauftrag_ohne_Inhalt_wird_abgelehnt()
    {
        var (ok, fehler) = BridgeProtocol.Pruefe(Job("write", path: "a.txt"), Token, Projekt, true, true, true);
        Assert.False(ok);
        Assert.Contains("content", fehler);
    }

    [Fact]
    public void Ein_zu_grosser_Schreibauftrag_wird_abgelehnt()
    {
        var gross = new string('x', 5 * 1024 * 1024);
        var (ok, fehler) = BridgeProtocol.Pruefe(Job("write", path: "a.txt", content: gross), Token, Projekt, true, true, true);
        Assert.False(ok);
        Assert.Contains("zu gross", fehler);
    }

    [Theory]
    [InlineData("manifest")]
    [InlineData("look")]
    public void Die_harmlosen_Arten_brauchen_keine_weitere_Angabe(string art)
        => Assert.True(Pruefe(Job(art)));

    // ============================ Briefkasten ============================

    [Fact]
    public void Der_Briefkastenname_ist_zufaellig_und_pruefbar()
    {
        var a = RelayClient.NeuerName();
        var b = RelayClient.NeuerName();
        Assert.NotEqual(a, b);
        Assert.StartsWith("adc-", a, StringComparison.Ordinal);
        Assert.True(RelayClient.NameIstGueltig(a));
        Assert.True(RelayClient.NameIstGueltig(b));
    }

    [Fact]
    public void Die_Kennung_gegenueber_dem_Briefkasten_ist_abruftauglich()
    {
        // filebin gibt Downloads nur an erkannte Abrufprogramme heraus. Fehlt der Zusatz, kommt
        // statt der Datei eine HTML-Seite – die Bruecke bliebe stumm. Der Test haelt das fest.
        Assert.Contains("curl/", RelayClient.Kennung, StringComparison.Ordinal);

        using var http = new HttpClient();
        using var relay = new RelayClient(http);
        Assert.Equal(RelayClient.Kennung, http.DefaultRequestHeaders.UserAgent.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("kurz")]
    [InlineData("../../../etc/passwd")]
    [InlineData("mit leerzeichen und mehr")]
    [InlineData("adc-ok-aber-zu-lang-.............................................................................................................................................")]
    public void Ein_unsicherer_Briefkastenname_wird_abgelehnt(string name)
        => Assert.False(RelayClient.NameIstGueltig(name));

    [Fact]
    public void Auftrags_und_Ergebnisdateien_tragen_die_Kennung()
    {
        Assert.Equal("job-abc.json", BridgeJob.DateiName("abc"));
        Assert.Equal("result-abc.json", BridgeJob.ErgebnisName("abc"));
    }

    [Fact]
    public void Ein_Auftrag_ueberlebt_das_Hinund_Her_der_Datei()
    {
        var original = Job("run", "test", note: "Bitte die Tests laufen lassen");
        var zurueck = BridgeProtocol.FromJson<BridgeJob>(BridgeProtocol.ToJson(original));
        Assert.NotNull(zurueck);
        Assert.Equal(original.Id, zurueck!.Id);
        Assert.Equal(original.Command, zurueck.Command);
        Assert.Equal(JobKind.Run, zurueck.KindAlsEnum);
    }

    [Fact]
    public void Kaputtes_JSON_ergibt_null_statt_einer_Ausnahme()
    {
        Assert.Null(BridgeProtocol.FromJson<BridgeJob>("{kaputt"));
        Assert.Null(BridgeProtocol.FromJson<BridgeJob>(""));
        Assert.Null(BridgeProtocol.FromJson<BridgeJob>(null));
    }

    [Fact]
    public void Ein_Ergebnis_sagt_was_passiert_ist()
    {
        var json = BridgeProtocol.ToJson(new BridgeResult
        {
            Id = "j1", Status = "ok", Kind = "run", Ausgabe = "315 bestanden", ExitCode = 0,
            DauerSekunden = 12.5, Zeit = "2026-09-21 01:00:00",
        });
        var zurueck = BridgeProtocol.FromJson<BridgeResult>(json)!;
        Assert.True(zurueck.Ok);
        Assert.Contains("bestanden", zurueck.Ausgabe);
        Assert.Contains("dauerSekunden", json);
    }

    [Fact]
    public void Die_Arten_des_Protokolls_stehen_fest()
    {
        Assert.Contains("run", BridgeProtocol.JobArten);
        Assert.Contains("manifest", BridgeProtocol.JobArten);
        Assert.Contains("write", BridgeProtocol.JobArten);
        Assert.Contains("vorschlag", BridgeProtocol.JobArten);
        Assert.Equal(7, BridgeProtocol.JobArten.Count);
    }

    [Fact]
    public void Die_Abbilddateien_haben_feste_Namen()
    {
        Assert.Equal("manifest.json", BridgeProtocol.ManifestDatei);
        Assert.Equal("workspace.md", BridgeProtocol.ManifestLesbarDatei);
        Assert.Equal("hello.json", BridgeProtocol.HelloDatei);
    }

    // ============================ Vorschläge (PowerShell, cmd …) ============================

    private static BridgeJob Vorschlag(string program, string[] argumente, string zweck = "Test")
        => new()
        {
            Id = "20260921-010000-abcdef", Kind = "vorschlag", Program = program,
            Argumente = argumente, Zweck = zweck, Token = Token,
        };

    [Theory]
    [InlineData("pwsh")]
    [InlineData("powershell")]
    [InlineData("cmd")]
    [InlineData("git")]
    [InlineData("dotnet")]
    public void Ein_Vorschlag_mit_erlaubtem_Programm_wartet_auf_die_Freigabe(string program)
    {
        // Kein Ausführen – die Prüfung sagt nur, dass der Auftrag überhaupt gestellt werden darf.
        // Die Entscheidung trifft danach der Nutzer im Freigabefenster.
        Assert.True(Pruefe(Vorschlag(program, ["-NoProfile", "-Command", "Get-Date"])));
    }

    [Theory]
    [InlineData("bash")]
    [InlineData("sh")]
    [InlineData("rm")]
    [InlineData("C:/Windows/System32/cmd.exe")]
    [InlineData("")]
    [InlineData(null)]
    public void Ein_Vorschlag_mit_fremdem_Programm_wird_abgelehnt(string? program)
        => Assert.False(Pruefe(Vorschlag(program!, ["-x"])));

    /// <summary>
    /// Fund aus dem Master-Check (24.09.2026): <c>cmd</c> und <c>pwsh</c> sind selbst eine Shell,
    /// <c>python</c> und <c>node</c> nehmen Code an. Bis rc18 gingen Argumente wie
    /// <c>dir &amp;&amp; del *</c> durch – und im Freigabefenster stand dabei „läuft ohne Shell“.
    /// Jetzt werden Zeichen abgelehnt, die weitere Befehle verketten; jeder einzelne Aufruf bleibt
    /// erlaubt (die Entscheidung trifft weiterhin der Nutzer im Fenster).
    /// </summary>
    [Theory]
    [InlineData("cmd", "dir && del *")]
    [InlineData("pwsh", "Get-ChildItem | Remove-Item")]
    [InlineData("python", "-c import os; os.remove('x')")]
    public void Ein_Shell_Vorschlag_mit_Verkettung_wird_abgelehnt(string program, string argument)
    {
        var (ok, fehler) = BridgeProtocol.Pruefe(Vorschlag(program, ["-x", argument]), Token, Projekt, true, true, true);
        Assert.False(ok);
        Assert.Contains("verketten", fehler, StringComparison.Ordinal);
    }

    [Fact]
    public void Ein_Shell_Vorschlag_ohne_Verkettung_bleibt_erlaubt()
    {
        Assert.True(Pruefe(Vorschlag("cmd", ["/c", "dir"])));
        Assert.True(Pruefe(Vorschlag("python", ["-m", "pytest"])));

        // Windows-übliche .exe-Schreibweise: der Roblox-Start bleibt im geprüften
        // Projekt-Skript; Explorer wird dafür nicht zur allgemeinen Programmliste geöffnet.
        Assert.True(Pruefe(Vorschlag("powershell.exe",
            ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "scripts/start-roblox.ps1"])));

        Assert.False(Pruefe(Vorschlag("explorer.exe", ["scripts/start-roblox.ps1"])));
        Assert.False(Pruefe(Vorschlag(@"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe", ["-NoProfile"])));
    }

    [Fact]
    public void Ein_Vorschlag_ohne_Zweck_wird_abgelehnt()
        => Assert.False(Pruefe(Vorschlag("pwsh", ["-NoProfile"], zweck: "   ")));

    [Fact]
    public void Ein_Vorschlag_mit_zu_vielen_oder_zu_langen_Argumenten_wird_abgelehnt()
    {
        Assert.False(Pruefe(Vorschlag("pwsh", Enumerable.Repeat("-x", CommandCatalog.VorschlagMaxArgumente + 1).ToArray())));
        Assert.False(Pruefe(Vorschlag("pwsh", [new string('a', CommandCatalog.VorschlagMaxArgumentLaenge + 1)])));
    }

    /// <summary>
    /// Ein Vorschlag hat seinen <b>eigenen</b> Schalter. Bis rc9 hing er zusätzlich an der
    /// Befehlsfreigabe: Wer nur Vorschläge erlauben wollte, bekam „Vorschläge sind in den
    /// Einstellungen nicht freigegeben“ zu sehen – obwohl er genau das angekreuzt hatte. Der
    /// Nutzer entscheidet ohnehin im Freigabefenster, und ohne Fenster läuft nichts.
    /// </summary>
    [Fact]
    public void Vorschlaege_haengen_an_ihrem_eigenen_Schalter()
    {
        Assert.True(Pruefe(Vorschlag("pwsh", ["-NoProfile"]), befehle: false, vorschlaege: true));
        Assert.False(Pruefe(Vorschlag("pwsh", ["-NoProfile"]), befehle: true, vorschlaege: false));
        // Die Programmliste bleibt auch mit eigenem Schalter die Grenze.
        Assert.False(Pruefe(Vorschlag("bash", ["-c", "id"]), befehle: true, vorschlaege: true));
    }

    /// <summary>
    /// Ein kalter Lauf (erster Restore, Pakete aus dem Netz, WPF) darf nicht an einer zu knappen
    /// Zeitgrenze scheitern. Am 21.09.2026 standen hier fünf Minuten – auf einem fremden Rechner
    /// kann das zu wenig sein, und das Ergebnis wäre ein abgebrochener Bauauftrag ohne Schuld des
    /// Nutzers.
    /// </summary>
    [Fact]
    public void Bauauftraege_haben_Luft_fuer_einen_kalten_Lauf()
    {
        foreach (var id in new[] { "build", "test", "restore" })
        {
            var spec = CommandCatalog.Find(id);
            Assert.NotNull(spec);
            Assert.True(spec!.Timeout >= TimeSpan.FromMinutes(10), $"{id}: {spec.Timeout} ist zu knapp.");
        }
    }

    [Fact]
    public void Aus_einem_Vorschlag_wird_ein_Befehl_ohne_Shell_und_mit_fester_Dauer()
    {
        var spec = CommandCatalog.Vorschlag("pwsh", ["-NoProfile", "-Command", "Get-Date"], "Datum zeigen");

        Assert.Equal("pwsh", spec.FileName);
        Assert.Equal(3, spec.Arguments.Count);
        Assert.Contains("Get-Date", spec.Arguments[2]);      // bleibt ein Argument, keine Zeichenkette
        Assert.Equal(CommandKind.Write, spec.Kind);
        Assert.Equal(CommandCatalog.VorschlagTimeout, spec.Timeout);
        Assert.True(spec.Timeout <= TimeSpan.FromMinutes(10), "Ein freigegebener Aufruf darf nicht ewig laufen.");
    }

    /// <summary>
    /// Der Fehler, der den ganzen Zweck unterlaufen hätte: Steckt der Zeitstempel im Fingerabdruck,
    /// ist er bei jedem Takt anders – das schwere Abbild ginge alle 20 Sekunden raus, obwohl sich
    /// nichts geändert hat. Er gehört deshalb nicht hinein, die Protokollzeilen auch nicht.
    /// </summary>
    [Fact]
    public void Der_Fingerabdruck_ignoriert_Zeitstempel_und_Protokollzeilen()
    {
        var ordner = Directory.CreateTempSubdirectory("adc-finger");
        try
        {
            File.WriteAllText(Path.Combine(ordner.FullName, "a.cs"), "// a");
            var erst = WorkspaceManifestBuilder.Build(ordner.FullName, "rechner", AppInfo.Version,
                logZeilen: ["Zeile eins"]);
            Thread.Sleep(1100);
            var zweit = WorkspaceManifestBuilder.Build(ordner.FullName, "rechner", AppInfo.Version,
                logZeilen: ["Zeile eins", "Zeile zwei – nur das Protokoll ist neu"]);

            Assert.NotEqual(erst.Time, zweit.Time);                      // das Abbild ist ein anderes …
            Assert.Equal(WorkspaceManifestBuilder.StandHash(erst),
                WorkspaceManifestBuilder.StandHash(zweit));               // … der Bestand aber nicht
            Assert.NotEqual(WorkspaceManifestBuilder.KleinHash(erst),
                WorkspaceManifestBuilder.KleinHash(zweit));               // die kleinen Dinge ändern sich

            File.WriteAllText(Path.Combine(ordner.FullName, "b.cs"), "// b");
            var dritt = WorkspaceManifestBuilder.Build(ordner.FullName, "rechner", AppInfo.Version);
            Assert.NotEqual(WorkspaceManifestBuilder.StandHash(zweit),
                WorkspaceManifestBuilder.StandHash(dritt));               // neue Datei -> neuer Bestand
        }
        finally
        {
            ordner.Delete(recursive: true);
        }
    }

    [Fact]
    public void Das_Gedaechtnis_merkt_sich_erledigte_Auftraege_ueber_den_Neustart()
    {
        var ordner = Directory.CreateTempSubdirectory("adc-gedaechtnis");
        try
        {
            var pfad = Path.Combine(ordner.FullName, "bridge-erledigt.txt");
            var erstes = new DateiGedaechtnis(pfad);
            Assert.False(erstes.Kenne("20260921-010000-aaaaaa"));
            erstes.Merke("20260921-010000-aaaaaa");
            erstes.Merke("20260921-010000-aaaaaa"); // doppelt ändert nichts

            // Zweiter Start: dieselbe Datei, neues Objekt – der Auftrag gilt weiterhin als erledigt.
            var zweites = new DateiGedaechtnis(pfad);
            Assert.True(zweites.Kenne("20260921-010000-aaaaaa"));
            Assert.False(zweites.Kenne("20260921-020000-bbbbbb"));
            Assert.Single(File.ReadAllLines(pfad));
        }
        finally
        {
            ordner.Delete(recursive: true);
        }
    }

    [Fact]
    public void Ein_kaputtes_Gedaechtnis_verhindert_nichts()
    {
        var ordner = Directory.CreateTempSubdirectory("adc-gedaechtnis-kaputt");
        try
        {
            // Kein Verzeichnis, keine Rechte, Unsinn im Pfad: es darf trotzdem weitergehen.
            var gedaechtnis = new DateiGedaechtnis(Path.Combine(ordner.FullName, "gibts-nicht", "tief", "liste.txt"));
            gedaechtnis.Merke("egal");
            Assert.True(gedaechtnis.Kenne("egal"));

            File.WriteAllText(Path.Combine(ordner.FullName, "leer.txt"), "\n\n   \n");
            var leer = new DateiGedaechtnis(Path.Combine(ordner.FullName, "leer.txt"));
            Assert.Empty(leer.Bekannt);
        }
        finally
        {
            ordner.Delete(recursive: true);
        }
    }

    [Fact]
    public void Die_Chronik_vergibt_fortlaufende_Nummern()
    {
        var chronik = new BridgeChronik();
        chronik.Aufnehmen(["src/a.cs", "src/b.cs"], ["src/c.cs"], ["alt.txt"], DateTime.Now,
            pfad => (123, "abcd1234"));

        Assert.Equal(4, chronik.Sequenz);
        Assert.Equal(0, chronik.Von);
        Assert.Equal(["neu", "neu", "geaendert", "weg"], chronik.Eintraege.Select(e => e.Art).ToList());
        Assert.Equal("src/c.cs", chronik.Eintraege[2].Pfad);
        Assert.Equal(123, chronik.Eintraege[2].Bytes);
        Assert.Equal("abcd1234", chronik.Eintraege[2].Hash);
        Assert.Equal(0, chronik.Eintraege[3].Bytes);
        Assert.Null(chronik.Eintraege[3].Hash);   // eine gelöschte Datei hat keinen Hash
    }

    /// <summary>
    /// Der Ring darf nie wachsen – sonst wäre der „billige Weg“ nach ein paar Tagen teurer als das
    /// volle Abbild, das er vermeiden soll.
    /// </summary>
    [Fact]
    public void Die_Chronik_bleibt_ein_Ring()
    {
        var chronik = new BridgeChronik();
        for (var i = 0; i < 100; i++)
        {
            chronik.Aufnehmen([$"datei_{i:000}.cs"], [], [], DateTime.Now, _ => (10, "h"));
        }

        Assert.Equal(100, chronik.Eintraege.Count);   // noch nicht voll
        Assert.Equal(100, chronik.Sequenz);

        chronik.Aufnehmen(Enumerable.Range(0, 900).Select(i => $"spaeter_{i:000}.cs").ToList(), [], [], DateTime.Now, _ => (10, "h"));
        Assert.Equal(BridgeChronik.MaxEintraege, chronik.Eintraege.Count);
        Assert.Equal(1000, chronik.Sequenz);
        Assert.Equal(1000 - BridgeChronik.MaxEintraege, chronik.Von);
        Assert.Equal(1000 - BridgeChronik.MaxEintraege + 1, chronik.Eintraege[0].Sequenz);
    }

    /// <summary>
    /// Die Obergrenze ist der ganze Punkt: ein voller Ring muss deutlich kleiner bleiben als ein
    /// volles Abbild eines mittleren Projekts (2 500 Dateien ≈ 286 KB).
    /// </summary>
    [Fact]
    public void Die_Chronik_bleibt_klein_genug()
    {
        var chronik = new BridgeChronik();
        for (var i = 0; i < BridgeChronik.MaxEintraege + 50; i++)
        {
            chronik.Aufnehmen([], [$"src/Core/Import/modul_{i:0000}.cs"], [], DateTime.Now,
                _ => (3500, "1a2b3c4d5e6f7a8b"));
        }

        var json = chronik.ToJson(new BridgeStand(2500, 20_000_000, "example-pc", "0.2.0-rc7", "main @ 3897190", 3, 1, "warten=0/erledigt=12"));

        Assert.True(json.Length < 30_000, $"Chronik ist {json.Length} Bytes groß – zu viel für den Dauerbetrieb.");
        Assert.True(json.Length < 286_155 / 9, $"{json.Length} Bytes: die Chronik muss ein Bruchteil eines vollen Abbilds bleiben.");
        Assert.Contains("\"aenderungen\"", json);
        Assert.Contains("\"seq\"", json);
        Assert.DoesNotContain("\"geaendert\":", json);   // Kurzform: Art als Wert, nicht als Feldname

        // Und die Gegenprobe: die Kurzform muss sich wieder auslesen lassen.
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal(chronik.Sequenz, doc.RootElement.GetProperty("seq").GetInt64());
        Assert.Equal(BridgeChronik.MaxEintraege, doc.RootElement.GetProperty("aenderungen").GetArrayLength());
        Assert.Equal(4, doc.RootElement.GetProperty("aenderungen")[0].GetArrayLength());
        Assert.Equal("geaendert", doc.RootElement.GetProperty("aenderungen")[0][0].GetString());
        Assert.Equal("1a2b3c4d", doc.RootElement.GetProperty("aenderungen")[0][3].GetString());
    }

    [Fact]
    public void Das_Lebenszeichen_verraet_die_Nummer_des_letzten_Stands()
    {
        var json = BridgeProtocol.ToJson(new BridgeHello
        {
            AppVersion = AppInfo.Version, Host = "example-pc", Zeit = "2026-09-21 01:00:00",
            Tools = ["dotnet", "git", "node"],
            Sequenz = 4711,
            Stand = new BridgeStand(2500, 20_000_000, "example-pc", AppInfo.Version, "main @ 3897190", 3, 1, "warten=0"),
        });

        var zurueck = BridgeProtocol.FromJson<BridgeHello>(json)!;
        Assert.Equal(4711, zurueck.Sequenz);
        Assert.Equal(2500, zurueck.Stand!.Dateien);
        Assert.Contains("main", zurueck.Stand.Git);
        Assert.Contains("dotnet", zurueck.Tools);
        Assert.True(json.Length < 600, "Das Lebenszeichen muss winzig bleiben.");

        // Das Kennwort gehört NICHT in den Briefkasten: er ist für jeden lesbar, der den Namen kennt.
        // Stünde es dort, wäre es als zweiter Faktor wertlos. (Bis rc7 stand es drin.)
        Assert.DoesNotContain("token", json);
    }

    /// <summary>
    /// Kern der Sache: Nach einem Neustart muss die Brücke wissen, was sich in der Pause geändert
    /// hat. Mit gespeichertem Stand entstehen echte Änderungseinträge; ohne ihn wird der erste
    /// Stand nicht als „alles neu“ gemeldet (das wäre Rauschen statt Information).
    /// </summary>
    [Fact]
    public void Mit_gespeichertem_Stand_kennt_die_Chronik_die_Aenderungen_aus_der_Pause()
    {
        var ordner = Directory.CreateTempSubdirectory("adc-stand");
        try
        {
            File.WriteAllText(Path.Combine(ordner.FullName, "a.cs"), "// alt");
            File.WriteAllText(Path.Combine(ordner.FullName, "b.cs"), "// bleibt");
            var vorherJson = WorkspaceManifestBuilder.ToJson(
                WorkspaceManifestBuilder.Build(ordner.FullName, "rechner", AppInfo.Version));

            // Während der Companion aus ist: eine Datei geändert, eine neu, eine weg.
            File.WriteAllText(Path.Combine(ordner.FullName, "a.cs"), "// neu");
            File.WriteAllText(Path.Combine(ordner.FullName, "c.cs"), "// dazu");
            File.Delete(Path.Combine(ordner.FullName, "b.cs"));

            var speicher = new SpeicherStand();
            speicher.Schreiben(vorherJson);

            var chronik = new BridgeChronik();
            var jetzt = WorkspaceManifestBuilder.Build(ordner.FullName, "rechner", AppInfo.Version);
            var vorher = BridgeProtocol.FromJson<WorkspaceManifest>(speicher.Lesen()!)!;
            var (neu, geaendert, weg) = WorkspaceManifestBuilder.Diff(vorher, jetzt);
            chronik.Aufnehmen(neu, geaendert, weg, DateTime.Now,
                pfad => jetzt.Files.Where(f => f.Path == pfad).Select(f => (f.Bytes, f.Hash)).FirstOrDefault());

            Assert.Equal(["c.cs"], neu);
            Assert.Equal(["a.cs"], geaendert);
            Assert.Equal(["b.cs"], weg);
            Assert.Equal(3, chronik.Sequenz);
            Assert.Single(chronik.Eintraege.Where(e => e.Art == "neu"));
        }
        finally
        {
            ordner.Delete(recursive: true);
        }
    }

    /// <summary>
    /// Der Befund vom 21.09.2026 aus dem echten Betrieb (Abnahmepunkt 8.9): Der <b>Stand</b> überlebte
    /// den Neustart, der <b>Verlauf</b> nicht. Zwei neue und eine geänderte Datei waren vor dem Beenden
    /// erkannt worden; nach dem Neustart stand nur noch die danach erkannte Löschung in der Chronik.
    /// Hier wird der Ring deshalb durch den Speicher geschickt und wieder aufgenommen.
    /// </summary>
    [Fact]
    public void Der_Verlauf_uebersteht_den_Neustart()
    {
        var chronik = new BridgeChronik();
        chronik.Aufnehmen(["neu-a.txt"], ["geaendert-b.txt"], ["weg-c.txt"], DateTime.Now,
            _ => (42, "1234567890abcdef"));

        var gespeichert = chronik.ToSpeicher();
        Assert.Equal(3, chronik.Sequenz);

        var nachNeustart = new BridgeChronik();
        Assert.True(nachNeustart.AusSpeicher(gespeichert));
        Assert.Equal(3, nachNeustart.Sequenz);
        Assert.Equal(3, nachNeustart.Eintraege.Count);
        Assert.Equal(["neu", "geaendert", "weg"], nachNeustart.Eintraege.Select(e => e.Art).ToArray());
        Assert.Equal("neu-a.txt", nachNeustart.Eintraege[0].Pfad);

        // Und die Nummer läuft weiter, sie fängt nicht wieder bei eins an.
        nachNeustart.Aufnehmen([], ["nochmal.txt"], [], DateTime.Now, _ => (1, "abcdef12"));
        Assert.Equal(4, nachNeustart.Sequenz);

        // Ein kaputter Speicher ist kein Grund aufzugeben – dann beginnt der Verlauf eben neu.
        var kaputt = new BridgeChronik();
        Assert.False(kaputt.AusSpeicher("{kein json"));
        Assert.False(kaputt.AusSpeicher(null));
        Assert.Equal(0, kaputt.Sequenz);
    }

    /// <summary>
    /// Der ganze Weg in einem Takt: Der Verlauf, den der Companion vor dem Beenden geschickt hat,
    /// steht nach dem Neustart immer noch da – ergänzt um das, was in der Pause passiert ist.
    /// </summary>
    [Fact]
    public async Task Ein_Neustart_verliert_die_Eintraege_aus_der_Pause_nicht()
    {
        var ordner = Directory.CreateTempSubdirectory("adc-neustart");
        try
        {
            File.WriteAllText(Path.Combine(ordner.FullName, "a.cs"), "// alt");

            var briefkasten = new SammelBriefkasten();
            using var http = new HttpClient(briefkasten);
            using var relay = new RelayClient(http) { Bin = "adc-aaaaaaaaaaaaaaaaaaaaaaaa" };
            var speicher = new SpeicherStand();   // wie bridge-stand.json auf der Platte

            BridgeService Dienst() => new(
                relay,
                new CommandRunner(),
                _ => null,
                (_, inhalt) => inhalt,
                () => Token,
                () => WorkspaceManifestBuilder.Build(ordner.FullName, "test-pc", AppInfo.Version),
                standSpeicher: speicher)
            {
                ProjectRoot = ordner.FullName,
                AbbildAbstand = TimeSpan.Zero,   // im Test darf jeder Takt etwas tun
            };

            // Lauf 1: erst die Vergleichsbasis …
            var restart1 = Dienst(); await restart1.TaktAsync(CancellationToken.None); await restart1.WaitForPublicationAsync();

            // … dann kommt eine Datei dazu -> ein Eintrag im Verlauf.
            File.WriteAllText(Path.Combine(ordner.FullName, "b.cs"), "// neu");
            var restart2 = Dienst(); await restart2.TaktAsync(CancellationToken.None); await restart2.WaitForPublicationAsync();
            Assert.Contains("b.cs", briefkasten.Abgelegt["chronik.json"]);

            // Pause: die Datei verschwindet, während der Companion aus ist.
            File.Delete(Path.Combine(ordner.FullName, "b.cs"));

            // Neustart mit demselben Speicher (neuer Dienst, wie ein neuer Prozeß).
            briefkasten.Abgelegt.Remove("chronik.json");
            var restart3 = Dienst(); await restart3.TaktAsync(CancellationToken.None); await restart3.WaitForPublicationAsync();
            var verlauf = briefkasten.Abgelegt["chronik.json"];

            Assert.Contains("b.cs", verlauf);                                  // Eintrag von vorher
            Assert.Contains("\"weg\"", verlauf);                              // und der aus der Pause
            Assert.Contains("\"seq\":2", verlauf);                            // Nummern laufen weiter
            Assert.DoesNotContain("alles neu", verlauf);
        }
        finally
        {
            ordner.Delete(recursive: true);
        }
    }

    [Fact]
    public void Der_Standspeicher_schreibt_und_liest_ueber_den_Neustart()
    {
        var ordner = Directory.CreateTempSubdirectory("adc-standdatei");
        try
        {
            var pfad = Path.Combine(ordner.FullName, "tief", "bridge-stand.json");
            var datei = new DateiStand(pfad);
            Assert.Null(datei.Lesen());
            Assert.Null(datei.LesenChronik());
            datei.Schreiben("{\"v\":1}");
            datei.SchreibenChronik("{\"v\":1,\"seq\":7}");
            Assert.Equal("{\"v\":1}", new DateiStand(pfad).Lesen());
            Assert.Equal("{\"v\":1}", File.ReadAllText(pfad));

            // Der Verlauf liegt in einer eigenen, kleinen Datei daneben.
            var chronikPfad = Path.Combine(Path.GetDirectoryName(pfad)!, "bridge-chronik.json");
            Assert.True(File.Exists(chronikPfad));
            Assert.Equal("{\"v\":1,\"seq\":7}", new DateiStand(pfad).LesenChronik());
        }
        finally
        {
            ordner.Delete(recursive: true);
        }
    }

    [Fact]
    public void Ein_Lebenszeichen_legt_offen_wer_da_ist()
    {
        var json = BridgeProtocol.ToJson(new BridgeHello
        {
            AppVersion = AppInfo.Version, Host = "example-pc", Zeit = "2026-09-21 01:00:00",
            Befehle = ["build", "test"],
        });
        var zurueck = BridgeProtocol.FromJson<BridgeHello>(json)!;
        Assert.Equal("example-pc", zurueck.Host);
        Assert.Contains("test", zurueck.Befehle);
    }

    // ============================ Der Takt gegen einen erfundenen Briefkasten ============================

    /// <summary>
    /// Sammelt alles, was der Companion in den Briefkasten legt – damit ein ganzer Takt ohne Netz
    /// prüfbar ist. Genau dieser Weg hat in rc7 gefehlt: der Prüfstand setzte den Projektordner
    /// selbst, die App aber nicht, und so ging ein Fehler durch, den erst der echte Betrieb zeigte.
    /// </summary>
    private sealed class SammelBriefkasten : HttpMessageHandler
    {
        public Dictionary<string, string> Abgelegt { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Wie oft jede Datei abgelegt wurde – nötig, um „nur einmal gemeldet" zu prüfen.</summary>
        public Dictionary<string, int> Zaehler { get; } = new(StringComparer.OrdinalIgnoreCase);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var name = request.RequestUri!.AbsolutePath.Split('/').Last();
            if (request.Method == HttpMethod.Put && request.Content is not null)
            {
                Abgelegt[name] = await request.Content.ReadAsStringAsync(cancellationToken);
                Zaehler[name] = Zaehler.TryGetValue(name, out var bisher) ? bisher + 1 : 1;
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") };
            }

            // Die Liste: alles, was im Briefkasten liegt – so sieht der Companion seine Aufträge.
            if (request.RequestUri.AbsolutePath.TrimEnd('/').EndsWith("adc-aaaaaaaaaaaaaaaaaaaaaaaa", StringComparison.Ordinal))
            {
                var dateien = Abgelegt.Keys
                    .Select(name => $"{{\"filename\":{System.Text.Json.JsonSerializer.Serialize(name)},\"bytes\":{Abgelegt[name].Length}}}")
                    .ToList();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"files\":[" + string.Join(',', dateien) + "]}"),
                };
            }

            // Ein Abruf: die abgelegte Datei zurückgeben, sonst »nicht vorhanden«.
            if (request.Method == HttpMethod.Get)
            {
                var abruf = request.RequestUri.AbsolutePath.Split('/').Last();
                if (Abgelegt.TryGetValue(abruf, out var inhalt))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(inhalt) };
                }
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("") };
        }
    }

    [Fact]
    public async Task Ein_Takt_legt_Abbild_und_Lebenszeichen_ab_und_verraet_kein_Kennwort()
    {
        var ordner = Directory.CreateTempSubdirectory("adc-takt");
        try
        {
            var briefkasten = new SammelBriefkasten();
            using var http = new HttpClient(briefkasten);
            using var relay = new RelayClient(http) { Bin = "adc-aaaaaaaaaaaaaaaaaaaaaaaa" };

            var dienst = new BridgeService(
                relay,
                new CommandRunner(),
                _ => null,
                (_, inhalt) => inhalt,
                () => Token,
                () => WorkspaceManifestBuilder.Build(
                    ordner.FullName, "test-pc", AppInfo.Version,
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["dotnet"] = @"C:\dotnet.exe" }))
            {
                ProjectRoot = ordner.FullName,
            };

            await dienst.TaktAsync(CancellationToken.None);
            await dienst.WaitForPublicationAsync();

            Assert.True(briefkasten.Abgelegt.ContainsKey("manifest.json"), "Das Abbild fehlt.");
            Assert.True(briefkasten.Abgelegt.ContainsKey("hello.json"), "Das Lebenszeichen fehlt.");
            Assert.True(briefkasten.Abgelegt.ContainsKey("chronik.json"), "Der Verlauf fehlt.");

            var hello = briefkasten.Abgelegt["hello.json"];
            Assert.Contains("tools", hello);                       // Werkzeuge stehen drin …
            // … und der Projektordner. Unter Windows stehen im JSON doppelte Trennzeichen
            // (C:\\…), deshalb wird der Wert geparst statt als Textstück gesucht.
            using (var lebenszeichen = System.Text.Json.JsonDocument.Parse(hello))
            {
                Assert.Equal(ordner.FullName, lebenszeichen.RootElement.GetProperty("projekt").GetString());
            }
            Assert.DoesNotContain("token", hello);                   // … aber nie das Kennwort
        }
        finally
        {
            ordner.Delete(recursive: true);
        }
    }

    /// <summary>
    /// Befund vom 21.09.2026 beim Vordenken für die restliche Abnahme: Die Schreibfreigabe prüfte nur
    /// die feste Befehlsliste (<c>publish</c>). Ein Datei-Schreibauftrag lief auch dann durch, wenn
    /// „Schreibende Befehle erlauben" <b>aus</b> war – der Schalter war schwächer als sein Hilfetext.
    /// </summary>
    [Fact]
    public void Ohne_schreibende_Freigabe_werden_auch_keine_Dateien_geschrieben()
    {
        var schreiben = Job("write", path: "notiz.txt", content: "hallo");

        Assert.False(Pruefe(schreiben, befehle: true, schreibend: false));
        Assert.True(Pruefe(schreiben, befehle: true, schreibend: true));

        // Und ein Schreibbefehl der festen Liste bleibt ebenfalls gesperrt.
        Assert.False(Pruefe(Job("run", "publish"), schreibend: false));

        var (ok, fehler) = BridgeProtocol.Pruefe(schreiben, Token, Projekt, true, false, true);
        Assert.False(ok);
        Assert.Contains("schreiben", fehler, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Befund vom 21.09.2026 (Abnahmepunkt 8.8): Wurde die App mitten in einem Auftrag beendet, lag die
    /// Auftragsdatei noch im Briefkasten und der Auftrag lief nach dem Start ein zweites Mal los. Jetzt
    /// wird er eingangs gemerkt: er läuft nicht erneut, und die andere Seite bekommt einmal Bescheid.
    /// </summary>
    [Fact]
    public async Task Ein_abgebrochener_Auftrag_laeuft_nach_dem_Neustart_nicht_noch_einmal()
    {
        var ordner = Directory.CreateTempSubdirectory("adc-abbruch");
        try
        {
            var kennung = "20260921-120000-abc123";
            var briefkasten = new SammelBriefkasten();
            // Der Auftrag liegt noch da (er wurde nie abgeholt) …
            briefkasten.Abgelegt[BridgeJob.DateiName(kennung)] =
                BridgeProtocol.ToJson(Job("write", path: "soll-nicht-entstehen.txt", content: "x") with { Id = kennung });

            // … und das Gedächtnis weiß, dass er schon begonnen wurde (App wurde beendet).
            var gedaechtnis = new BridgeGedaechtnis();
            gedaechtnis.Merke(kennung);

            using var http = new HttpClient(briefkasten);
            using var relay = new RelayClient(http) { Bin = "adc-aaaaaaaaaaaaaaaaaaaaaaaa" };
            var dienst = new BridgeService(
                relay,
                new CommandRunner(),
                _ => null,
                (pfad, inhalt) => { File.WriteAllText(pfad, inhalt); return "geschrieben"; },
                () => Token,
                () => WorkspaceManifestBuilder.Build(ordner.FullName, "test-pc", AppInfo.Version),
                gedaechtnis: gedaechtnis)
            {
                ProjectRoot = ordner.FullName,
                AbbildAbstand = TimeSpan.Zero,
                BefehleErlaubt = true,
                SchreibendErlaubt = true,
            };

            await dienst.TaktAsync(CancellationToken.None);
            await dienst.WaitForPublicationAsync();

            // Nichts wurde ausgeführt …
            Assert.False(File.Exists(Path.Combine(ordner.FullName, "soll-nicht-entstehen.txt")));

            // … aber die andere Seite erfährt, warum nichts kommt.
            var ergebnisName = BridgeJob.ErgebnisName(kennung);
            Assert.True(briefkasten.Abgelegt.ContainsKey(ergebnisName), "Es gab keine Meldung zum Abbruch.");
            var ergebnis = BridgeProtocol.FromJson<BridgeResult>(briefkasten.Abgelegt[ergebnisName])!;
            Assert.Equal("fehler", ergebnis.Status);
            Assert.Contains("nicht wiederholt", ergebnis.Hinweis!, StringComparison.OrdinalIgnoreCase);

            // Beim nächsten Takt gibt es keine zweite Meldung (das Ergebnis liegt ja schon da).
            await dienst.TaktAsync(CancellationToken.None);
            await dienst.WaitForPublicationAsync();
            Assert.Equal(1, briefkasten.Zaehler[ergebnisName]);
        }
        finally
        {
            ordner.Delete(recursive: true);
        }
    }

    /// <summary>
    /// Befund vom 21.09.2026 beim Vordenken: Das schwere Abbild geht höchstens alle fünf Minuten raus –
    /// stand es in dieser Zeit aus, wurde es bis rc10 vergessen (der Fingerabdruck wurde trotzdem
    /// fortgeschrieben). Der billige Weg blieb aktuell, der volle Stand hing minutenlang hinterher.
    /// </summary>
    [Fact]
    public async Task Ein_aufgeschobenes_schweres_Abbild_wird_nachgeholt()
    {
        var ordner = Directory.CreateTempSubdirectory("adc-nachholen");
        try
        {
            File.WriteAllText(Path.Combine(ordner.FullName, "a.cs"), "// erst");

            var briefkasten = new SammelBriefkasten();
            using var http = new HttpClient(briefkasten);
            using var relay = new RelayClient(http) { Bin = "adc-aaaaaaaaaaaaaaaaaaaaaaaa" };
            var dienst = new BridgeService(
                relay,
                new CommandRunner(),
                _ => null,
                (_, inhalt) => inhalt,
                () => Token,
                () => WorkspaceManifestBuilder.Build(ordner.FullName, "test-pc", AppInfo.Version))
            {
                ProjectRoot = ordner.FullName,
                AbbildAbstand = TimeSpan.Zero,
                SchwerAbstand = TimeSpan.FromSeconds(3),
            };

            // Takt 1: erster Stand, das schwere Abbild geht raus (nichts steht aus).
            await dienst.TaktAsync(CancellationToken.None);
            await dienst.WaitForPublicationAsync();
            Assert.Contains("a.cs", briefkasten.Abgelegt["manifest.json"]);

            // Direkt danach ändert sich etwas – innerhalb der drei Sekunden: es wird aufgeschoben.
            File.WriteAllText(Path.Combine(ordner.FullName, "b.cs"), "// dazu");
            await dienst.TaktAsync(CancellationToken.None);
            await dienst.WaitForPublicationAsync();
            Assert.DoesNotContain("b.cs", briefkasten.Abgelegt["manifest.json"]);
            Assert.Contains("b.cs", briefkasten.Abgelegt["workspace.md"]);   // der billige Weg ist sofort aktuell

            // Nach Ablauf des Abstands muss es nachgeholt werden – auch ohne neue Änderung.
            await Task.Delay(TimeSpan.FromSeconds(3.2));
            await dienst.TaktAsync(CancellationToken.None);
            await dienst.WaitForPublicationAsync();
            Assert.Contains("b.cs", briefkasten.Abgelegt["manifest.json"]);
        }
        finally
        {
            ordner.Delete(recursive: true);
        }
    }

    /// <summary>
    /// Der Kern des Fehlers aus rc7: Der Projektordner darf nicht einmal beim Start übernommen
    /// werden, sondern muss bei jedem Zugriff frisch abgefragt werden. Sonst laufen alle Aufträge
    /// ins Leere („Projektordner nicht gefunden"), obwohl das Fenster den Ordner längst kennt.
    /// </summary>
    [Fact]
    public void Der_Projektordner_wird_bei_jedem_Zugriff_frisch_abgefragt()
    {
        var ordner = Directory.CreateTempSubdirectory("adc-frisch");
        try
        {
            var aktuell = string.Empty;   // beim Start der Brücke noch unbekannt
            var dienst = new BridgeService(
                new RelayClient(new HttpClient(new SammelBriefkasten())) { Bin = "adc-aaaaaaaaaaaaaaaaaaaaaaaa" },
                new CommandRunner(),
                _ => null,
                (_, inhalt) => inhalt,
                () => Token,
                () => WorkspaceManifestBuilder.Build(ordner.FullName, "test-pc", AppInfo.Version))
            {
                ProjektOrdner = () => aktuell,
            };

            Assert.Equal(string.Empty, dienst.ProjektOrdnerJetzt());

            aktuell = ordner.FullName;   // das Fenster kennt ihn jetzt
            Assert.Equal(ordner.FullName, dienst.ProjektOrdnerJetzt());

            // Und der gespeicherte Wert allein zählt nicht mehr.
            dienst.ProjectRoot = Path.GetTempPath();
            Assert.Equal(ordner.FullName, dienst.ProjektOrdnerJetzt());
        }
        finally
        {
            ordner.Delete(recursive: true);
        }
    }

    // ============================ rc13: Lesen, Schreiben, Binärinhalt ============================

    /// <summary>
    /// Befund vom 21.09.2026 (TODO 8.18): Bis rc12 redigierte der Companion beim Lesen und zerlegte
    /// dabei Code – wer den Inhalt zurückschrieb, verlor Zeilen (fünf weg, vier Compilerfehler).
    /// Jetzt kommt der Inhalt <b>unverändert</b> an; wie verdächtig er ist, steht als Hinweis dabei.
    /// </>
    [Fact]
    public async Task Ein_gelesener_Inhalt_kommt_unveraendert_und_mit_Hinweis_zurueck()
    {
        var ordner = Directory.CreateTempSubdirectory("adc-lesen");
        try
        {
            var inhalt = "var token = \"geheim-abcdefgh\";\nvar harmlos = 1;\n";
            var briefkasten = new SammelBriefkasten();
            using var http = new HttpClient(briefkasten);
            using var relay = new RelayClient(http) { Bin = "adc-aaaaaaaaaaaaaaaaaaaaaaaa" };
            briefkasten.Abgelegt[BridgeJob.DateiName("j-lesen")] =
                BridgeProtocol.ToJson(Job("read", path: "notiz.cs", id: "j-lesen"));

            var dienst = new BridgeService(
                relay,
                new CommandRunner(),
                _ => inhalt,
                (_, t) => "ok",
                () => Token,
                () => WorkspaceManifestBuilder.Build(ordner.FullName, "test-pc", AppInfo.Version))
            {
                ProjectRoot = ordner.FullName,
                AbbildAbstand = TimeSpan.FromSeconds(30),
            };

            await dienst.TaktAsync(CancellationToken.None);
            await dienst.WaitForPublicationAsync();

            var ergebnis = BridgeProtocol.FromJson<BridgeResult>(
                briefkasten.Abgelegt[BridgeJob.ErgebnisName("j-lesen")])!;
            Assert.Equal("ok", ergebnis.Status);
            Assert.Equal(inhalt, ergebnis.Ausgabe);                            // unverändert …
            Assert.DoesNotContain(SecretRedaction.Platzhalter, ergebnis.Ausgabe!);
            Assert.Contains("Stelle", ergebnis.Hinweis!);                      // … mit Auskunft dazu
        }
        finally
        {
            ordner.Delete(recursive: true);
        }
    }

    /// <summary>
    /// Befund vom 21.09.2026 (TODO 8.18): Ein Inhalt mit dem Redaktionsplatzhalter ist mit hoher
    /// Wahrscheinlichkeit beim Lesen entstanden. Ihn zurückzuschreiben zerstört die Datei – deshalb
    /// lehnt die Brücke das ab, statt stillschweigend Code zu verlieren.
    /// </summary>
    [Fact]
    public void Ein_redigierter_Inhalt_wird_nicht_zurueckgeschrieben()
    {
        var schreiben = Job("write", path: "notiz.cs", content: "var x = " + SecretRedaction.Platzhalter + ";");

        var (ok, fehler) = BridgeProtocol.Pruefe(schreiben, Token, Projekt, true, true, true);
        Assert.False(ok);
        Assert.Contains(SecretRedaction.Platzhalter, fehler);
        Assert.Contains("zurückgeschrieben", fehler, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// TODO 8.20, gefunden am 21.09.2026 beim Hochladen der Datei, die die Regel beschreibt: Der
    /// Wächter wies auch Texte ab, die den Platzhalter nur <b>erwähnen</b> – TODO, CHANGELOG,
    /// <c>SecretRedaction.cs</c> selbst. Deshalb gibt es jetzt <c>platzhalterErlaubt</c>: mit diesem
    /// Feld schreibt der Absender bewusst, ohne es bleibt die Sperre scharf.
    /// </summary>
    [Fact]
    public void Mit_platzhalterErlaubt_geht_ein_Text_mit_dem_Wort_durch()
    {
        var text = "Die Brücke ersetzt Geheimnisse durch " + SecretRedaction.Platzhalter + " – siehe Handbuch.";
        var bewusst = Job("write", path: "docs/BRUECKE.md", content: text) with { PlatzhalterErlaubt = true };

        Assert.True(Pruefe(bewusst), "Ein bewusst mitschickender Auftrag darf nicht blockiert werden.");

        // Und ohne das Feld bleibt es bei der Ablehnung.
        var ohne = Job("write", path: "docs/BRUECKE.md", content: text);
        var (ok, fehler) = BridgeProtocol.Pruefe(ohne, Token, Projekt, true, true, true);
        Assert.False(ok);
        Assert.Contains("platzhalterErlaubt", fehler, StringComparison.OrdinalIgnoreCase);

        // Der Hinweis nennt den Ausweg – sonst raten wir beim nächsten Mal wieder.
        Assert.Contains(SecretRedaction.Platzhalter, fehler);
    }

    /// <summary>
    /// rc13: Ein Anwendungszeichen, ein Bild oder eine Archivdatei lässt sich nicht als Text
    /// übertragen. Über <c>"kodierung": "base64"</c> geht der Inhalt byteweise durch – sonst müsste
    /// jede Binärdatei von Hand eingespielt werden.
    /// </summary>
    [Fact]
    public async Task Ein_Binaerinhalt_geht_ueber_Base64()
    {
        var ordner = Directory.CreateTempSubdirectory("adc-binaer");
        try
        {
            var bytes = new byte[] { 0, 1, 2, 250, 251, 252, 137, 80, 78, 71, 13, 10, 26, 10 };
            var briefkasten = new SammelBriefkasten();
            using var http = new HttpClient(briefkasten);
            using var relay = new RelayClient(http) { Bin = "adc-aaaaaaaaaaaaaaaaaaaaaaaa" };
            briefkasten.Abgelegt[BridgeJob.DateiName("j-bild")] = BridgeProtocol.ToJson(
                Job("write", path: "Assets/zeichen.png", content: Convert.ToBase64String(bytes), id: "j-bild")
                    with { Kodierung = "base64" });

            var dienst = new BridgeService(
                relay,
                new CommandRunner(),
                _ => null,
                (_, t) => "text",
                () => Token,
                () => WorkspaceManifestBuilder.Build(ordner.FullName, "test-pc", AppInfo.Version),
                dateiSchreibenBytes: (pfad, inhalt) =>
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(pfad)!);
                    File.WriteAllBytes(pfad, inhalt);
                    return $"geschrieben: {Path.GetFileName(pfad)} ({inhalt.Length} Bytes)";
                })
            {
                ProjectRoot = ordner.FullName,
                AbbildAbstand = TimeSpan.FromSeconds(30),
                SchreibendErlaubt = true,
            };

            await dienst.TaktAsync(CancellationToken.None);
            await dienst.WaitForPublicationAsync();

            var ziel = Path.Combine(ordner.FullName, "Assets", "zeichen.png");
            Assert.True(File.Exists(ziel), "Die Binärdatei wurde nicht angelegt.");
            Assert.Equal(bytes, File.ReadAllBytes(ziel));
            var ergebnis = BridgeProtocol.FromJson<BridgeResult>(
                briefkasten.Abgelegt[BridgeJob.ErgebnisName("j-bild")])!;
            Assert.Equal("ok", ergebnis.Status);
            Assert.Contains("Bytes", ergebnis.Ausgabe!);
        }
        finally
        {
            ordner.Delete(recursive: true);
        }
    }

    [Fact]
    public void Ungueltiges_oder_unbekanntes_wird_abgelehnt()
    {
        var kaputt = Job("write", path: "a.bin", content: "das ist kein base64 !!")
            with { Kodierung = "base64" };
        var (ok, fehler) = BridgeProtocol.Pruefe(kaputt, Token, Projekt, true, true, true);
        Assert.False(ok);
        Assert.Contains("Base64", fehler);

        var fremd = Job("write", path: "a.txt", content: "hallo") with { Kodierung = "quoted-printable" };
        var (ok2, fehler2) = BridgeProtocol.Pruefe(fremd, Token, Projekt, true, true, true);
        Assert.False(ok2);
        Assert.Contains("Kodierung", fehler2);

        // Text bleibt Text – und ein Binärinhalt ohne verdrahtete Ablage wird gemeldet, nicht verschluckt.
        Assert.True(Pruefe(Job("write", path: "a.txt", content: "hallo")));
    }

    // ============================ Windows-Fehlerpfade in Klartext (rc18, Abnahmefund) ============================

    /// <summary>
    /// Beim Abnahmelauf über die Brücke gemessen: Ein Schreibauftrag auf die laufende
    /// Anwendungsdatei kam als roher englischer Windows-Satz zurück. Der Nutzer soll lesen können,
    /// was los ist – und was er tun kann.
    /// </summary>
    [Fact]
    public void Eine_gesperrte_Datei_bekommt_einen_deutschen_Satz()
    {
        var gesperrt = new IOException(
            "The process cannot access the file 'C:\\x\\ArenaDesktopCompanion.dll' because it is being used by another process.");

        var meldung = DateiFehler.Aufklaren(gesperrt);

        Assert.Contains("gesperrt", meldung, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Fenster", meldung, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("being used by another process", meldung, StringComparison.Ordinal);   // die Ursprungszeile bleibt

        // Und die Erkennung ist nicht auf diesen einen Wortlaut angewiesen.
        Assert.True(DateiFehler.IstSperre("… error MSB3027: … konnte nicht kopiert werden"));
        Assert.False(DateiFehler.IstSperre("Datei nicht gefunden"));
        Assert.False(DateiFehler.IstSperre(null));
    }

    [Fact]
    public void Verweigerter_Zugriff_und_fehlender_Ordner_werden_erklaert()
    {
        Assert.Contains("Zugriff verweigert", DateiFehler.Aufklaren(new UnauthorizedAccessException("Access to the path is denied.")));
        Assert.Contains("Zielordner", DateiFehler.Aufklaren(new DirectoryNotFoundException("Could not find a part of the path.")));

        // Was wir nicht kennen, bleibt unangetastet – lieber roh als falsch erklärt.
        Assert.Equal("irgendwas anderes", DateiFehler.Aufklaren(new InvalidOperationException("irgendwas anderes")));
        Assert.Null(DateiFehler.Klar(new InvalidOperationException("irgendwas anderes")));
    }
}
