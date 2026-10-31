using ArenaDesktopCompanion.Core.Agents;

namespace ArenaDesktopCompanion.Core.Tests;

/// <summary>
/// Die Werkzeuge, die der Companion selbst benutzen darf (Phase 8). Zwei Zusagen werden hier
/// festgehalten: Es gibt <b>keine</b> freie Kommandozeile (nur diese Liste), und der Aufruf geht
/// <b>ohne Shell</b> vonstatten – damit ein Pfad mit <c>&amp;</c> oder <c>|</c> niemals als Befehl
/// wirkt. Dazu: kein Schlüssel in einer Ausgabe, die angezeigt oder kopiert wird.
/// </summary>
public class AgentToolingTests
{
    // ============================ Schlüssel entfernen ============================

    [Theory]
    [InlineData("OPENAI_API_KEY=sk-proj-abcdef1234567890abcdef", "sk-proj")]
    [InlineData("ANTHROPIC_API_KEY: sk-ant-api03-abcdefghijklmnopqrst", "sk-ant")]
    [InlineData("Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9", "eyJhbGci")]
    [InlineData("GITHUB_TOKEN=ghp_abcdefghijklmnopqrstuvwxyz0123456789", "ghp_")]
    [InlineData("key=AIzaSyA1234567890abcdefghijklmnopqrst", "AIzaSy")]
    [InlineData("token: xoxb-1234567890-abcdefghijkl", "xoxb-")]
    public void Schluessel_werden_aus_Ausgaben_entfernt(string zeile, string darfNichtVorkommen)
    {
        var sauber = SecretRedaction.Clean(zeile);
        Assert.DoesNotContain(darfNichtVorkommen, sauber);
        Assert.Contains(SecretRedaction.Platzhalter, sauber);
        Assert.True(SecretRedaction.EnthaeltVerdaechtiges(zeile));
    }

    /// <summary>
    /// Befund vom 21.09.2026 auf dem Zielrechner: Dort ist nur Windows PowerShell installiert, kein
    /// <c>pwsh</c>. Die drei Skriptbefehle (Selbsttest, Publish, Absturzbericht) standen in der Liste
    /// als freigegeben – und endeten sofort mit „Das System kann die angegebene Datei nicht finden".
    /// Jetzt kommt das erste vorhandene Ersatzprogramm zum Zug.
    /// </summary>
    [Fact]
    public void Ein_fehlendes_Programm_faellt_auf_das_Ersatzprogramm_zurueck()
    {
        var selbsttest = CommandCatalog.Find("selftest")!;
        Assert.Equal("pwsh", selbsttest.FileName);
        Assert.Contains("powershell", selbsttest.Ersatz);

        // pwsh fehlt, powershell ist da -> powershell wird genommen.
        Assert.Equal(@"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe",
            CommandRunner.WaehleProgramm(selbsttest, name => name == "powershell"
                ? @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe"
                : null));

        // pwsh ist da -> pwsh gewinnt.
        Assert.Equal(@"C:\Program Files\PowerShell\7\pwsh.exe",
            CommandRunner.WaehleProgramm(selbsttest, name => name == "pwsh"
                ? @"C:\Program Files\PowerShell\7\pwsh.exe"
                : null));

        // Nichts gefunden -> der gewünschte Name bleibt, damit die Meldung ihn nennt.
        Assert.Equal("pwsh", CommandRunner.WaehleProgramm(selbsttest, _ => null));

        // Ohne Auflöser (Prüfungen, fremde Systeme) bleibt alles wie vorher.
        Assert.Equal("dotnet", CommandRunner.WaehleProgramm(CommandCatalog.Find("dotnet-version")!, null));
    }

    /// <summary>
    /// Jeder Skriptbefehl muss einen Ersatz nennen – sonst fällt derselbe Fehler bei der nächsten
    /// Gelegenheit wieder auf. Ein Wächter, damit die Liste nicht auseinanderläuft.
    /// </summary>
    [Fact]
    public void Jeder_Skriptbefehl_nennt_einen_Ersatz()
    {
        foreach (var spec in CommandCatalog.Alle.Where(c => c.FileName is "pwsh" or "powershell"))
        {
            Assert.True(spec.Ersatz.Count > 0, $"{spec.Id} nennt keinen Ersatz für {spec.FileName}.");
            Assert.NotEqual(spec.FileName, spec.Ersatz[0]);
        }
    }

    [Fact]
    public void Ein_privater_Schluessel_verschwindet_vollstaendig()
    {
        var text = "-----BEGIN RSA PRIVATE KEY-----\nMIIEowIBAAKCAQEA\nmehrZeug\n-----END RSA PRIVATE KEY-----";
        var sauber = SecretRedaction.Clean(text);
        Assert.DoesNotContain("MIIEowIBAAKCAQEA", sauber);
        Assert.DoesNotContain("mehrZeug", sauber);
    }

    [Fact]
    public void Eine_Umgebungsvariable_mit_Schlüsselnamen_wird_entfernt()
    {
        var zeile = "Claude meldet: ANTHROPIC_API_KEY=abc123def456ghi789";
        var sauber = SecretRedaction.Clean(zeile);
        Assert.DoesNotContain("abc123def456ghi789", sauber);
        Assert.Contains("ANTHROPIC_API_KEY", sauber); // der Name darf bleiben, er hilft beim Suchen
    }

    [Theory]
    // Diese Zeilen sehen nur aus wie Geheimnisse – sie sind Diagnose und müssen lesbar bleiben.
    [InlineData("Commit 38971903aabbccddeeff00112233445566778899")]
    [InlineData("SHA256: 9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08")]
    [InlineData("MSBUILD : error MSB1003: Specify a project or solution file.")]
    [InlineData("Testlauf: 315 bestanden, 0 fehlgeschlagen")]
    [InlineData("Pfad: C:\\Users\\example-user\\Desktop\\ArenaDesktopCompanion\\ADC")]
    public void Hashwerte_und_normaler_Text_bleiben_unangetastet(string zeile)
        => Assert.Equal(zeile, SecretRedaction.Clean(zeile));

    [Fact]
    public void Aufraeumen_ist_wiederholbar()
    {
        var einmal = SecretRedaction.Clean("KEY=sk-abcdefghijklmnopqrstuvwx");
        Assert.Equal(einmal, SecretRedaction.Clean(einmal));
    }

    // ============================ Werkzeuge finden ============================

    [Fact]
    public void Findet_ein_Programm_mit_Endung_auf_dem_suchpfad()
    {
        using var tree = new Support.TempTree();
        tree.Write("git.cmd", string.Empty);
        var treffer = ToolDiscovery.Find("git", [tree.Root], ToolDiscovery.Endungen(windows: true));
        Assert.True(treffer.Found);
        Assert.Equal(Path.Combine(tree.Root, "git.cmd"), treffer.Path);
    }

    [Fact]
    public void Meldet_ein_fehlt_Programm_ohne_Ausnahme()
    {
        using var tree = new Support.TempTree();
        var treffer = ToolDiscovery.Find("opencode", [tree.Root], ToolDiscovery.Endungen(windows: true));
        Assert.False(treffer.Found);
        Assert.Equal(string.Empty, treffer.Path);
    }

    [Fact]
    public void Sucht_in_allen_Bekannten_und_behaelt_die_Reihenfolge()
    {
        using var tree = new Support.TempTree();
        tree.Write("dotnet.cmd", string.Empty);
        tree.Write("git.cmd", string.Empty);
        var alle = ToolDiscovery.FindeAlle([tree.Root], ToolDiscovery.Endungen(windows: true));
        Assert.Equal(ToolDiscovery.Bekannt.Count, alle.Count);
        Assert.Equal("dotnet", alle[0].Name);
        Assert.True(alle[0].Found);
        Assert.True(alle[1].Found);
        Assert.False(alle[2].Found); // node
    }

    [Fact]
    public void Ein_kaputter_PATH_Eintrag_bricht_die_Suche_nicht_ab()
    {
        var treffer = ToolDiscovery.Find("git", ["\0ungueltig", string.Empty], ToolDiscovery.Endungen(windows: true));
        Assert.False(treffer.Found);
    }

    [Theory]
    [InlineData(@"C:\a;C:\b", 2)]
    [InlineData(@"/usr/bin:/usr/local/bin", 1)]   // unter Windows ist »:« kein Trenner
    [InlineData("", 0)]
    public void Der_PATH_wird_plattformgerecht_zerlegt(string path, int erwartet)
    {
        var teile = ToolDiscovery.SplitPath(path, windows: true);
        Assert.Equal(erwartet == 1 ? 1 : erwartet, teile.Count);
    }

    [Fact]
    public void Die_ueblichen_Windows_Ordner_stehen_in_der_Liste()
    {
        // Path.Combine setzt auf diesem Prüfrechner "/" – für den Vergleich vereinheitlichen.
        var ordner = ToolDiscovery.StandardOrdner(windows: true, @"C:\Users\example-user", @"C:\Program Files")
            .Select(o => o.Replace('\\', '/'))
            .ToList();
        Assert.Contains(ordner, o => o.EndsWith(".dotnet/tools", StringComparison.Ordinal));
        Assert.Contains(ordner, o => o.EndsWith("/npm", StringComparison.Ordinal));
        Assert.Contains(ordner, o => o.EndsWith("Microsoft VS Code/bin", StringComparison.Ordinal));
    }

    // ============================ Befehlsliste ============================

    [Fact]
    public void Jeder_Befehl_ist_vollstaendig_beschrieben()
    {
        foreach (var befehl in CommandCatalog.Alle)
        {
            Assert.False(string.IsNullOrWhiteSpace(befehl.Title), befehl.Id);
            Assert.False(string.IsNullOrWhiteSpace(befehl.Description), befehl.Id);
            Assert.False(string.IsNullOrWhiteSpace(befehl.FileName), befehl.Id);
            Assert.True(befehl.Timeout > TimeSpan.Zero, befehl.Id);
            Assert.NotEmpty(befehl.Arguments);
        }
    }

    [Fact]
    public void Die_Kennungen_sind_eindeutig()
    {
        var ids = CommandCatalog.Alle.Select(c => c.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Kein_Befehl_enthaelt_ein_Schaltenzeichen_der_Shell()
    {
        foreach (var befehl in CommandCatalog.Alle)
        {
            Assert.False(CommandCatalog.ArgumentIstUnbedenklich(befehl.FileName) is false, befehl.Id);
            foreach (var argument in befehl.Arguments)
            {
                Assert.True(CommandCatalog.ArgumentIstUnbedenklich(argument),
                    $"{befehl.Id}: Argument »{argument}« enthält ein Zeichen, das eine Shell ausführen würde.");
            }
        }
    }

    [Fact]
    public void Ein_Argument_mit_Schaltenzeichen_wird_erkannt()
    {
        Assert.False(CommandCatalog.ArgumentIstUnbedenklich("x && del *"));
        Assert.False(CommandCatalog.ArgumentIstUnbedenklich("a|b"));
        Assert.False(CommandCatalog.ArgumentIstUnbedenklich("$(whoami)"));
        Assert.True(CommandCatalog.ArgumentIstUnbedenklich("-p:TreatWarningsAsErrors=true"));
    }

    [Fact]
    public void Ohne_Freigabe_gibt_es_keinen_einzigen_Befehl()
        => Assert.Empty(CommandCatalog.Verfuegbar(windows: true, befehleErlaubt: false, schreibendErlaubt: true));

    [Fact]
    public void Schreibende_Befehle_erscheinen_nur_mit_eigener_Freigabe()
    {
        var ohne = CommandCatalog.Verfuegbar(windows: true, befehleErlaubt: true, schreibendErlaubt: false);
        Assert.DoesNotContain(ohne, c => c.Kind == CommandKind.Write);

        var mit = CommandCatalog.Verfuegbar(windows: true, befehleErlaubt: true, schreibendErlaubt: true);
        Assert.Contains(mit, c => c.Id == "publish");
    }

    [Fact]
    public void Auf_Linux_erscheinen_die_PowerShell_Befehle_nicht()
    {
        var linux = CommandCatalog.Verfuegbar(windows: false, befehleErlaubt: true, schreibendErlaubt: true);
        Assert.DoesNotContain(linux, c => c.NurWindows);
        Assert.Contains(linux, c => c.Id == "build");
    }

    [Fact]
    public void Die_Lesenden_Befehle_sind_immer_dieselben()
    {
        var lesend = CommandCatalog.Verfuegbar(windows: true, befehleErlaubt: true, schreibendErlaubt: false)
            .Where(c => c.Kind == CommandKind.Read).Select(c => c.Id).ToList();
        Assert.Contains("git-status", lesend);
        Assert.Contains("git-diff-stat", lesend);
        Assert.Contains("crash-report", lesend);
    }

    // ============================ Ausführen ============================

    [Fact]
    public async Task Fuehrt_einen_echten_Befehl_aus_und_liefert_die_Ausgabe()
    {
        using var tree = new Support.TempTree();
        var spec = new CommandSpec("test-echo", "Test", "Test", "dotnet", ["--version"], CommandKind.Read, TimeSpan.FromMinutes(1));
        var ergebnis = await new CommandRunner().RunAsync(tree.Root, spec);

        Assert.Null(ergebnis.StartError);
        Assert.False(ergebnis.TimedOut);
        Assert.Equal(0, ergebnis.ExitCode);
        Assert.True(ergebnis.Ok);

        // Der Test prüft, dass wirklich ein Programm gelaufen ist – NICHT, welche Version auf dem
        // Rechner liegt. Ein früherer Fassung verlangte "8.0" und fiel auf dem Rechner des Nutzers
        // durch, wo .NET 10 installiert ist (gefunden am 21.09.2026: "10.0.401" statt "8.0.425").
        Assert.Matches(@"^\d+\.\d+\.\d+", ergebnis.Output.Trim());
        Assert.Contains("erfolgreich", ergebnis.Zusammenfassung());
    }

    [Fact]
    public async Task Ein_fehlt_Programm_ergibt_eine_Meldung_statt_einer_Ausnahme()
    {
        using var tree = new Support.TempTree();
        var spec = new CommandSpec("fehlt", "Fehlt", "Fehlt", "gibt-es-garantiert-nicht-xyz", ["--version"],
            CommandKind.Read, TimeSpan.FromSeconds(5));
        var ergebnis = await new CommandRunner().RunAsync(tree.Root, spec);

        Assert.NotNull(ergebnis.StartError);
        Assert.False(ergebnis.Ok);
        Assert.Contains("nicht ausführbar", ergebnis.Zusammenfassung());
    }

    [Fact]
    public async Task Ein_fehlender_Projektordner_wird_gemeldet()
    {
        var spec = CommandCatalog.Find("git-status")!;
        var ergebnis = await new CommandRunner().RunAsync("/gibt/es/nicht", spec);
        Assert.Contains("Projektordner nicht gefunden", ergebnis.StartError);
    }

    [Fact]
    public async Task Ein_Rueckgabewert_ungleich_null_wird_gemeldet()
    {
        using var tree = new Support.TempTree();
        var spec = new CommandSpec("git-fehler", "Git", "Git im leeren Ordner",
            "git", ["log", "--oneline"], CommandKind.Read, TimeSpan.FromSeconds(20));
        var ergebnis = await new CommandRunner().RunAsync(tree.Root, spec);

        // Kein Git-Repo: entweder Git fehlt (StartError) oder es meldet einen Fehler – beides ist
        // ein sauberes Ergebnis und keine Ausnahme.
        Assert.False(ergebnis.Ok);
        Assert.True(ergebnis.StartError is not null || ergebnis.ExitCode != 0);
    }

    [Fact]
    public async Task Schluessel_in_der_Ausgabe_kommen_nicht_heraus()
    {
        using var tree = new Support.TempTree();
        // »git config --get« gibt es nicht überall; deshalb ein Programm, das es sicher gibt:
        // dotnet mit einem Argument, das auf eine Datei zeigt, deren Inhalt wir kennen.
        tree.Write("geheim.txt", "API_KEY=sk-abcdefghijklmnopqrstuvwx");
        var spec = new CommandSpec("cat", "Anzeigen", "Inhalt zeigen", "cat", ["geheim.txt"], CommandKind.Read, TimeSpan.FromSeconds(10));
        var ergebnis = await new CommandRunner().RunAsync(tree.Root, spec);

        if (ergebnis.StartError is not null)
        {
            return; // Windows ohne cat: der Fall wird von den Redaktions-Tests oben abgedeckt
        }

        Assert.DoesNotContain("sk-abcdefghijklmnopqrstuvwx", ergebnis.Output);
        Assert.True(ergebnis.Redacted);
    }

    [Fact]
    public void Zu_lange_Ausgaben_werden_in_der_Mitte_gekuerzt()
    {
        var text = new string('a', CommandRunner.MaxZeichen + 5_000);
        var (gekuerzt, warGekuerzt) = CommandRunner.Kuerzen(text);
        Assert.True(warGekuerzt);
        Assert.True(gekuerzt.Length <= CommandRunner.MaxZeichen + 80);
        Assert.Contains("ausgelassen", gekuerzt);
        Assert.StartsWith("aaa", gekuerzt);
    }

    [Fact]
    public void Kurze_Ausgaben_bleiben_vollstaendig()
    {
        var (text, warGekuerzt) = CommandRunner.Kuerzen("kurz und gut");
        Assert.False(warGekuerzt);
        Assert.Equal("kurz und gut", text);
    }

    // ============================ rc13: Zählung und Vorprüfung ============================

    /// <summary>
    /// rc13 (TODO 8.18): Die Redaktion nennt künftig, wie viele Stellen sie ersetzen <i>würde</i>.
    /// Genau diese Auskunft ersetzt das verstümmelnde Verhalten beim Lesen: Der Inhalt bleibt heil,
    /// der Hinweis sagt trotzdem, dass etwas verdächtig ist.
    /// </summary>
    [Fact]
    public void Die_Redaktion_zaehlt_ihre_Stellen_und_laesst_die_uebrigen_Zeilen_in_Ruhe()
    {
        var text = "zeile1 = 1;\napi_key = \"abcdefghijklmnop\"\nzeile3 = 3;\n";
        var (sauber, anzahl) = SecretRedaction.CleanMitAnzahl(text);

        Assert.Equal(1, anzahl);
        Assert.Contains(SecretRedaction.Platzhalter, sauber);
        Assert.Contains("zeile1 = 1;", sauber);
        Assert.Contains("zeile3 = 3;", sauber);

        var (unveraendert, keine) = SecretRedaction.CleanMitAnzahl("nur harmloser Text");
        Assert.Equal(0, keine);
        Assert.Equal("nur harmloser Text", unveraendert);
    }

    /// <summary>
    /// Befund vom 21.09.2026 (TODO 8.16): Im Projektordner lag kein vollständiges Projekt – nur die
    /// Update-Teilmenge in einem Unterordner. »Selbsttest« startete trotzdem, und Windows PowerShell
    /// antwortete mit „Das Argument »scripts/selftest.ps1« für den -File-Parameter ist nicht
    /// vorhanden“. Diese Meldung nennt den Grund nicht. Jetzt prüft der Ausführer vorab und sagt,
    /// was fehlt und wo es erwartet wird.
    /// </summary>
    [Fact]
    public void Fehlt_das_Skript_sagt_der_Ausfuehrer_was_und_wo()
    {
        using var tree = new Support.TempTree();

        var selbsttest = CommandCatalog.Find("selftest")!;
        var meldung = CommandRunner.FehlendeVoraussetzung(tree.Root, selbsttest);
        Assert.NotNull(meldung);
        Assert.Contains("liegt das Projekt nicht", meldung, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("scripts/selftest.ps1 fehlt", meldung);
        Assert.Contains(tree.Root, meldung);          // der erwartete Ort steht dabei

        // Auch das Bauen hängt an einer Datei: ohne Lösung gibt es die klare Meldung statt eines
        // dotnet-Fehlers, den man erst deuten muss.
        Assert.Contains("ArenaDesktopCompanion.sln",
            CommandRunner.FehlendeVoraussetzung(tree.Root, CommandCatalog.Find("build")!));

        // Sobald das Projekt daliegt, ist nichts zu klagen – und Befehle ohne Dateiargument
        // (dotnet --version, git status) werden gar nicht erst geprüft.
        tree.Write("scripts/selftest.ps1", "# selbsttest");
        tree.Write("ArenaDesktopCompanion.sln", "Microsoft Visual Studio Solution File");
        Assert.Null(CommandRunner.FehlendeVoraussetzung(tree.Root, selbsttest));
        Assert.Null(CommandRunner.FehlendeVoraussetzung(tree.Root, CommandCatalog.Find("build")!));
        Assert.Null(CommandRunner.FehlendeVoraussetzung(tree.Root, CommandCatalog.Find("dotnet-version")!));
        Assert.Null(CommandRunner.FehlendeVoraussetzung(tree.Root, CommandCatalog.Find("git-status")!));
    }
    /// <summary>
    /// Befund vom 21.09.2026, gefunden im ersten autonomen Zyklus über die Brücke: `build` scheiterte,
    /// weil der Companion sein eigenes Fenster offen hatte und damit seine Ausgabedatei sperrte. Die
    /// MSBuild-Meldung nennt die Datei, aber nicht die Ursache – der Ausführer sagt sie jetzt dazu.
    /// </summary>
    [Fact]
    public void Eine_Dateisperre_wird_in_Worten_erklaert()
    {
        var echteMeldung = "error MSB3027: Die zulässige Anzahl von Wiederholungen von 10 wurde überschritten. "
                           + "Die Datei wird durch \"Arena.ai Desktop Companion (26580)\" gesperrt.";
        var erklaerung = CommandRunner.ErklaerungFuerDateisperre(echteMeldung);
        Assert.NotNull(erklaerung);
        Assert.Contains("schließen", erklaerung);
        Assert.Contains("Tests", erklaerung);          // was trotzdem geht, steht dabei

        Assert.Null(CommandRunner.ErklaerungFuerDateisperre("Bestanden! 433 Tests, 0 Fehler."));
        Assert.Null(CommandRunner.ErklaerungFuerDateisperre("warning CS1591: Missing XML comment"));
    }

    /// <summary>
    /// Master-Check 24.09.2026: Für <c>cmd</c>, <c>pwsh</c>, <c>python</c> &amp; Co. stimmte der Satz
    /// „läuft ohne Shell" nicht – sie <b>sind</b> eine Shell bzw. nehmen Code an. Der Hinweis und die
    /// Laufweise kommen deshalb aus dem Kern und werden hier festgehalten.
    /// </summary>
    [Fact]
    public void Der_Shell_Hinweis_kommt_nur_bei_Programmen_die_selbst_Befehle_ausfuehren()
    {
        Assert.NotNull(CommandCatalog.FreigabeHinweis("cmd"));
        Assert.NotNull(CommandCatalog.FreigabeHinweis("PYTHON"));
        Assert.Null(CommandCatalog.FreigabeHinweis("git"));
        Assert.Null(CommandCatalog.FreigabeHinweis("dotnet"));

        Assert.Contains("ohne Shell", CommandCatalog.Laufweise("git"), StringComparison.Ordinal);
        Assert.Contains("selbst kann Befehle ausführen", CommandCatalog.Laufweise("cmd"), StringComparison.Ordinal);

        // Und die Zeichenprüfung, die bisher nur Tests benutzten, hängt jetzt am Auftrag (siehe
        // BridgeTests). Hier bleibt sie als Werkzeug geprüft.
        Assert.False(CommandCatalog.ArgumentIstUnbedenklich("a && b"));
        Assert.True(CommandCatalog.ArgumentIstUnbedenklich("--nologo"));
    }
}
