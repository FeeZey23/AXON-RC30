using AXON.Connector;
using System.Text.Json;
using ArenaDesktopCompanion.Core.Settings;
using ArenaDesktopCompanion.Core.Tests.Support;
using ArenaDesktopCompanion.Core.Agents;
using ArenaDesktopCompanion.Core.Bridge;
namespace ArenaDesktopCompanion.Core.Tests;
public class NeuerChatTests
{
    private static Arbeitsbereich Tab(string root)=>new(){ProjectFolder=root,BridgeBin="adc-test12345",BridgeToken="test-secret-only",BridgeEncrypted=true};
    [Fact] public void Der_Text_enthaelt_die_vollstaendige_Sitzungsbindung(){using var t=new TempTree();var tab=Tab(t.Root);var prepared=ConnectCapsule.Create(tab);var capsule=JsonSerializer.Deserialize<Capsule>(prepared.CapsuleJson)!;Assert.Equal(tab.BridgeBin,capsule.Connection["Briefkasten"]);Assert.Equal(tab.BridgeToken,capsule.Connection["Kennwort"]);Assert.Equal(t.Root,capsule.Connection["Projektordner"]);Assert.Equal(tab.Id,capsule.Connection["SessionId"]);}
    [Fact] public void Der_Text_installiert_in_einen_versteckten_Ordner(){using var t=new TempTree();var text=ConnectCapsule.Create(Tab(t.Root)).Prompt;Assert.Contains(".axon-rc30",text);Assert.Contains("inflateRawSync",text);Assert.DoesNotContain("pip install",text);}
    [Fact] public void Der_echte_Client_ist_eine_eingebettete_Ressource(){Assert.Contains("aes-256-gcm",ConnectCapsule.RuntimeSource());Assert.Contains("AXON-ACK:",ConnectCapsule.RuntimeSource());}
    [Fact] public void Das_Briefing_hat_keinen_Not_Aus_mehr(){using var t=new TempTree();Assert.DoesNotContain("Not-Aus",ConnectCapsule.Create(Tab(t.Root)).Prompt);Assert.DoesNotContain("notaus",ConnectCapsule.RuntimeSource(),StringComparison.OrdinalIgnoreCase);}
    [Fact] public void Ohne_Projekt_gibt_es_keine_scheinbar_fertige_Uebergabe(){Assert.Throws<InvalidOperationException>(()=>ConnectCapsule.Create(Tab("")));}
    [Fact]
    public void Jeder_Skriptbefehl_umgeht_die_Ausfuehrungsrichtlinie()
    {
        var verstoesse = new List<string>();

        foreach (var befehl in CommandCatalog.Alle)
        {
            var skript = befehl.Arguments.Any(a => a.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase));
            if (!skript)
            {
                continue;
            }

            var hatBypass = befehl.Arguments.Contains("-ExecutionPolicy", StringComparer.Ordinal)
                            && befehl.Arguments.Contains("Bypass", StringComparer.Ordinal);
            var hatNoProfile = befehl.Arguments.Contains("-NoProfile", StringComparer.Ordinal);

            if (!hatBypass || !hatNoProfile)
            {
                verstoesse.Add(befehl.Id + ": " + string.Join(' ', befehl.Arguments));
            }
        }

        Assert.Empty(verstoesse);
        Assert.Contains(CommandCatalog.Alle, b => b.Arguments.Any(a => a.Contains("crash-report.ps1", StringComparison.Ordinal)));
    }
    /// <summary>
    /// Befund vom 24.09.2026, live: Die App meldete „Text steht im Eingabefeld von arena.ai“ – im
    /// Chat stand aber nichts. Sie hatte nur irgendein Feld gefunden und nie nachgesehen. Diese
    /// Prüfungen halten den Bericht ehrlich: Er zählt die Felder, nennt die Strategie, die gegriffen
    /// hat, und zweifelt an, wenn die Seite den Text wieder fallen lässt.
    /// </summary>
    [Fact]
    public void Der_Bericht_liest_die_Antwort_der_Seite()
    {
        var bericht = EinfuegeBericht.AusJson(
            "{\"kandidaten\":2,\"feld\":\"DIV[textbox]\",\"strategie\":\"kleben\",\"sofort\":true,\"zeichen\":3140}");

        Assert.Equal(2, bericht.Kandidaten);
        Assert.Equal("DIV[textbox]", bericht.Feld);
        Assert.Equal("kleben", bericht.Strategie);
        Assert.True(bericht.Bestaetigt);
        Assert.Equal(3140, bericht.Zeichen);
    }

    [Fact]
    public void Kaputte_oder_leere_Antworten_gelten_als_nicht_bestaetigt()
    {
        foreach (var roh in new string?[] { null, "", "   ", "kein json", "{}" })
        {
            var bericht = EinfuegeBericht.AusJson(roh);
            Assert.False(bericht.Bestaetigt);
            Assert.Equal(0, bericht.Kandidaten);
        }
    }

    /// <summary>
    /// Der Satz darf nur Erfolg melden, wenn er stimmt – und sonst sagen, wo der Text zu finden ist
    /// (er liegt immer in der Zwischenablage). Genau das war live falsch.
    /// </summary>
    [Fact]
    public void Der_Satz_sagt_die_Wahrheit()
    {
        var bestaetigt = EinfuegeBericht.AusJson(
            "{\"kandidaten\":1,\"feld\":\"TEXTAREA\",\"strategie\":\"nativ\",\"sofort\":true,\"zeichen\":3140}");
        Assert.Contains("steht im Eingabefeld", bestaetigt.Satz());
        Assert.Contains("Enter", bestaetigt.Satz());

        var keinFeld = EinfuegeBericht.AusJson("{\"kandidaten\":0,\"sofort\":false}");
        Assert.Contains("kein Eingabefeld", keinFeld.Satz());
        Assert.Contains("Zwischenablage", keinFeld.Satz());

        var nichtGegriffen = EinfuegeBericht.AusJson("{\"kandidaten\":3,\"feld\":\"DIV[textbox]\",\"sofort\":false}");
        Assert.Contains("Keine der drei Schreibarten", nichtGegriffen.Satz());
        Assert.Contains("Zwischenablage", nichtGegriffen.Satz());
    }

    /// <summary>Lässt die Seite den Text wieder fallen, muss der Satz das sagen.</summary>
    [Fact]
    public void Verworfenen_Text_erkennt_der_Bericht()
    {
        var bericht = EinfuegeBericht.AusJson(
            "{\"kandidaten\":1,\"feld\":\"DIV[textbox]\",\"strategie\":\"kleben\",\"sofort\":true,\"zeichen\":3140}")
            .MitNachpruefung("nein");

        Assert.True(bericht.Bestaetigt);
        Assert.False(bericht.Nachpruefung);
        Assert.Contains("verworfen", bericht.Satz());

        var bleibt = bericht.MitNachpruefung("ja");
        Assert.True(bleibt.Nachpruefung);
        Assert.Contains("geprüft", bleibt.Satz());
    }

    // ============ Punkt 8.27, Fund vom 24.09.2026: das Promise als Antwort ============
    // Das Einfüge-Skript endete mit "return (async () => { … })();". Die WebView liefert für ein
    // Promise „{}" – und Deserialize<string> warf „The JSON value could not be converted to
    // System.String. Path: $ | LineNumber: 0 | BytePositionInLine: 1." Der Vorgang brach ab, das
    // Blatt schloss sich nie. Diese Fälle halten die Rohantwort-Auswertung fest.

    [Fact]
    public void Die_Rohantwort_der_WebView_wird_ausgewertet_wie_sie_kommt()
    {
        // Normalfall: die WebView packt den Bericht in Anführungszeichen.
        var bericht = EinfuegeBericht.AusWebViewAntwort(
            "\"{\\\"kandidaten\\\":1,\\\"feld\\\":\\\"TEXTAREA\\\",\\\"sofort\\\":true,\\\"zeichen\\\":42}\"",
            out var brauchbar);

        Assert.True(brauchbar);
        Assert.Equal(1, bericht.Kandidaten);
        Assert.True(bericht.Bestaetigt);
        Assert.Equal(42, bericht.Zeichen);
    }

    [Theory]
    // Der Fund von 8.27: das Promise-Ergebnis. Dazu die übrigen „keine Antwort"-Fälle.
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("false")]
    [InlineData("[]")]
    [InlineData("0")]
    [InlineData("kein json")]
    [InlineData("")]
    [InlineData(null)]
    public void Keine_Antwort_ergibt_keinen_Bericht_und_keine_Ausnahme(string? rohAntwort)
    {
        var bericht = EinfuegeBericht.AusWebViewAntwort(rohAntwort, out var brauchbar);

        Assert.False(brauchbar);
        Assert.Equal(0, bericht.Kandidaten);
        Assert.False(bericht.Bestaetigt);
        Assert.Contains("kein Eingabefeld", bericht.Satz());
    }

    [Fact]
    public void Ein_Zwischenstand_ist_brauchbar_aber_noch_kein_Bericht()
    {
        // Das Skript meldet beim Start „gestartet" – das ist eine gültige Antwort (die App blickt
        // weiter nach), aber sie zählt nicht als Ergebnis: Kandidaten bleiben 0.
        var bericht = EinfuegeBericht.AusWebViewAntwort("\"gestartet\"", out var brauchbar);

        Assert.True(brauchbar);
        Assert.Equal(0, bericht.Kandidaten);
    }

}
