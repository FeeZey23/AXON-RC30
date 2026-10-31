using ArenaDesktopCompanion.Core.Settings;

namespace ArenaDesktopCompanion.Core.Tests;

/// <summary>
/// Die Reiter (Arbeitsbereiche): je einer mit eigenem Projektordner und eigenem Brücken-Paar.
/// </summary>
/// <remarks>
/// Wunsch vom 24.09.2026: „Möchte zwischen mehreren Chats und Projekten wechseln können … zweiten
/// Tab im Companion öffnen mit anderem Chat und anderem Projektordner." Entschieden: eigenes Paar je
/// Reiter, Freigaben gemeinsam. Diese Prüfungen halten die Regeln fest, die beim Umbau leicht
/// verloren gehen: der Bestandsnutzer bekommt seinen vorhandenen Ordner in Reiter 1, der letzte
/// Reiter lässt sich nicht schließen, und der aktive Reiter bleibt immer gültig.
/// </remarks>
public class ArbeitsbereichTests
{
    private static AppSettings MitBestand() => new()
    {
        ProjectFolder = @"C:\AI\Arena Workspace\empty",
        BridgeBin = "adc-alt",
        BridgeToken = "aaaa-bbbb-cccc-dddd",
    };

    /// <summary>Wer schon Ordner und Paar hatte, findet beides unverändert in Reiter 1.</summary>
    [Fact]
    public void Der_Bestand_landet_im_ersten_Reiter()
    {
        var s = MitBestand();
        s.ArbeitsbereicheSicherstellen();

        Assert.Single(s.Arbeitsbereiche);
        Assert.Equal(0, s.AktiverArbeitsbereich);
        Assert.Equal(@"C:\AI\Arena Workspace\empty", s.Arbeitsbereiche[0].ProjectFolder);
        Assert.Equal("adc-alt", s.Arbeitsbereiche[0].BridgeBin);
        Assert.Equal("Chat 1", s.Arbeitsbereiche[0].Name);
    }

    /// <summary>Zweimal aufrufen darf nichts doppeln (Start ruft es bei jedem Fenster auf).</summary>
    [Fact]
    public void Sicherstellen_doppelt_ergibt_keinen_zweiten_Reiter()
    {
        var s = MitBestand();
        s.ArbeitsbereicheSicherstellen();
        s.ArbeitsbereicheSicherstellen();
        Assert.Single(s.Arbeitsbereiche);
    }

    /// <summary>
    /// Der Kern des Umbaus: Reiter wechseln heißt sichern und laden. Was in Reiter 1 stand, steht
    /// nach dem Wechsel zu Reiter 2 dort wieder – und umgekehrt.
    /// </summary>
    [Fact]
    public void Wechseln_sichert_und_laedt_den_richtigen_Stand()
    {
        var s = MitBestand();
        s.ArbeitsbereicheSicherstellen();

        // Reiter 2 anlegen und ihm ein eigenes Paar samt Ordner geben.
        var zweiter = s.ArbeitsbereichAnlegen("Zweiter Chat");
        s.AktiverArbeitsbereich = zweiter;
        s.ProjectFolder = @"C:\Projekte\Zwei";
        s.BridgeBin = "adc-zwei";
        s.BridgeToken = "eeee-ffff-gggg-hhhh";
        s.InArbeitsbereichSpeichern();

        // Zurück zu Reiter 1: dort muss der alte Stand stehen.
        s.AusArbeitsbereichLaden(0);
        Assert.Equal(@"C:\AI\Arena Workspace\empty", s.ProjectFolder);
        Assert.Equal("adc-alt", s.BridgeBin);

        // Und wieder zu Reiter 2.
        s.AusArbeitsbereichLaden(1);
        Assert.Equal(@"C:\Projekte\Zwei", s.ProjectFolder);
        Assert.Equal("adc-zwei", s.BridgeBin);
        Assert.Equal("eeee-ffff-gggg-hhhh", s.BridgeToken);
        Assert.Equal(1, s.AktiverArbeitsbereich);
    }

    /// <summary>
    /// Ein neuer Reiter ist **sofort verbindungsfähig** (Wunsch vom 26.09.2026): Sein Paar entsteht
    /// beim Anlegen, nur der Projektordner bleibt leer – die Brücke läuft also ohne Zutun.
    /// </summary>
    [Fact]
    public void Ein_neuer_Reiter_hat_sofort_sein_eigenes_Paar()
    {
        var s = MitBestand();
        s.ArbeitsbereicheSicherstellen();
        var zweiter = s.ArbeitsbereichAnlegen();

        Assert.True(s.Arbeitsbereiche[zweiter].Verbunden);
        Assert.NotEqual(s.Arbeitsbereiche[0].BridgeBin, s.Arbeitsbereiche[zweiter].BridgeBin);
        Assert.NotEqual(s.Arbeitsbereiche[0].BridgeToken, s.Arbeitsbereiche[zweiter].BridgeToken);
        Assert.Null(s.Arbeitsbereiche[zweiter].ProjectFolder);
        Assert.Equal("Chat 2", s.Arbeitsbereiche[zweiter].Name);
    }

    /// <summary>Das Paar eines neuen Reiters ist brauchbar (Form und Kennwortlänge).</summary>
    [Fact]
    public void Das_Paar_des_neuen_Reiters_ist_gueltig()
    {
        var s = new AppSettings();
        s.ArbeitsbereicheSicherstellen();
        var zweiter = s.ArbeitsbereichAnlegen();

        var bereich = s.Arbeitsbereiche[zweiter];
        Assert.True(ArenaDesktopCompanion.Core.Bridge.RelayClient.NameIstGueltig(bereich.BridgeBin));
        Assert.True((bereich.BridgeToken ?? string.Empty).Length >= 8);
    }

    /// <summary>Der letzte Reiter bleibt stehen – sonst gäbe es keinen Projektordner mehr.</summary>
    [Fact]
    public void Der_letzte_Reiter_laesst_sich_nicht_schliessen()
    {
        var s = MitBestand();
        s.ArbeitsbereicheSicherstellen();

        var aktiv = s.ArbeitsbereichEntfernen(0);
        Assert.Single(s.Arbeitsbereiche);
        Assert.Equal(0, aktiv);
    }

    /// <summary>
    /// Nach dem Schließen eines Reiters muss der aktive Reiter weiter auf einen vorhandenen zeigen –
    /// sonst stünde die Oberfläche auf einem Reiter, den es nicht mehr gibt.
    /// </summary>
    [Fact]
    public void Nach_dem_Schliessen_zeigt_der_aktive_Reiter_auf_einen_vorhandenen()
    {
        var s = MitBestand();
        s.ArbeitsbereicheSicherstellen();
        s.ArbeitsbereichAnlegen();
        s.ArbeitsbereichAnlegen();
        s.AktiverArbeitsbereich = 2;

        var aktiv = s.ArbeitsbereichEntfernen(2);
        Assert.Equal(2, s.Arbeitsbereiche.Count);
        Assert.InRange(aktiv, 0, s.Arbeitsbereiche.Count - 1);

        // Und beim Schließen eines Reiters vor dem aktiven rutscht der aktive mit.
        s.AktiverArbeitsbereich = 1;
        var nach = s.ArbeitsbereichEntfernen(0);
        Assert.Single(s.Arbeitsbereiche);
        Assert.Equal(0, nach);
    }

    [Fact]
    public void Umbenennen_nimmt_brauchbare_Namen_und_weist_leere_ab()
    {
        var s = MitBestand();
        s.ArbeitsbereicheSicherstellen();

        Assert.True(s.ArbeitsbereichUmbenennen(0, "  Arena Compadre  "));
        Assert.Equal("Arena Compadre", s.Arbeitsbereiche[0].Name);

        Assert.False(s.ArbeitsbereichUmbenennen(0, "   "));
        Assert.False(s.ArbeitsbereichUmbenennen(0, new string('x', 25)));
        Assert.Equal("Arena Compadre", s.Arbeitsbereiche[0].Name);
    }

    /// <summary>Ohne eigenen Namen zeigt der Reiter den Ordnernamen – nie eine leere Beschriftung.</summary>
    [Fact]
    public void Die_Beschriftung_faellt_auf_den_Ordner_zurueck()
    {
        var ohneName = new Arbeitsbereich { Name = "", ProjectFolder = @"C:\Projekte\Zwei" };
        Assert.Equal("Zwei", ohneName.Beschriftung());

        var leer = new Arbeitsbereich { Name = "   " };
        Assert.Equal("Chat", leer.Beschriftung());

        var mitName = new Arbeitsbereich { Name = "Analyse", ProjectFolder = @"C:\Projekte\Zwei" };
        Assert.Equal("Analyse", mitName.Beschriftung());
    }

    /// <summary>
    /// Ein Stand aus einer älteren Fassung (nur flache Felder, kein „workspaces“-Feld) muss sich
    /// laden lassen, ohne dass Ordner oder Paar verloren gehen.
    /// </summary>
    [Fact]
    public void Ein_alte_Einstellungsdatei_verliert_nichts()
    {
        const string alt = """
            { "version": 1, "projectFolder": "C:\\AI\\Arena Workspace\\empty",
              "bridgeEnabled": true, "bridgeBin": "adc-alt", "bridgeToken": "aaaa-bbbb-cccc-dddd",
              "bridgeAllowCommands": true, "bridgeAllowWrites": false }
            """;

        var s = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(alt)!;
        Assert.Empty(s.Arbeitsbereiche);

        s.ArbeitsbereicheSicherstellen();
        Assert.Equal(@"C:\AI\Arena Workspace\empty", s.ProjectFolder);
        Assert.Equal("adc-alt", s.Arbeitsbereiche[0].BridgeBin);

        // Seit rc17: Die alten Rechte-Häkchen werden nicht mehr gelesen – sie sind immer offen.
        // Solch eine Datei darf trotzdem anstandslos laden, und der Not-Aus ist aus.
        Assert.Null(typeof(AppSettings).GetProperty("NotAus"));
        Assert.NotEmpty(s.Arbeitsbereiche[0].Id);
    }
}
