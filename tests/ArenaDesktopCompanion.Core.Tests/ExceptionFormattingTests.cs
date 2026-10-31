using ArenaDesktopCompanion.Core.Diagnostics;
using System.IO;
using System.Reflection;

namespace ArenaDesktopCompanion.Core.Tests;

public class ExceptionFormattingTests
{
    [Fact]
    public void RootCause_geht_bis_zum_letzten_Glied()
    {
        var root = new IOException("Datendatei gesperrt");
        var mid = new InvalidOperationException("kann nicht lesen", root);
        var outer = new TargetInvocationException("Exception has been thrown by the target of an invocation.", mid);

        Assert.Same(root, outer.RootCause());
    }

    [Fact]
    public void RootCause_hoert_nach_kette_auf_keine_endlosschleife()
    {
        var a = new Exception("a");
        var b = new Exception("b", a);
        a.Data["kein-zyklus"] = b;   // echte Zyklen baut der Compiler nicht; Grenze ist der Zaehler

        Assert.Same(a, b.RootCause());
    }

    [Fact]
    public void Kurzmeldung_nimmt_typ_und_meldung_der_Ursache()
    {
        var wrapped = new TargetInvocationException(
            "Exception has been thrown by the target of an invocation.",
            new InvalidOperationException("WebView2-Lader nicht gefunden"));

        Assert.Equal("InvalidOperationException: WebView2-Lader nicht gefunden", wrapped.ShortMessage());
    }

    [Fact]
    public void Kurzmeldung_kuerzt_und_verliert_nichts()
    {
        var longMessage = new Exception(new string('y', 900));

        var shorted = longMessage.ShortMessage();

        Assert.True(shorted.Length <= ExceptionFormatting.MaxLineLength + 1, $"Länge war {shorted.Length}");
        Assert.EndsWith("…", shorted, StringComparison.Ordinal);
    }

    [Fact]
    public void Kurzmeldung_mit_leerer_Meldung_zeigt_wenigstens_den_Typ()
    {
        Assert.Equal("InvalidOperationException", new InvalidOperationException(string.Empty).ShortMessage());
    }

    [Fact]
    public void Kurzmeldung_bricht_nicht_wegen_eigener_Exception()
    {
        var bose = new BoomException();

        var text = bose.ShortMessage();

        Assert.False(string.IsNullOrWhiteSpace(text));
    }

    [Fact]
    public void Vollbericht_enthaelt_Jede_Stufe_und_Stack()
    {
        var error = new InvalidOperationException("oben", new IOException("unten"));

        var report = ExceptionFormatting.FullReport(error, "UI-Thread");

        Assert.Contains("UNERWARTETER FEHLER in UI-Thread", report, StringComparison.Ordinal);
        Assert.Contains("System.InvalidOperationException: oben", report, StringComparison.Ordinal);
        Assert.Contains("System.IO.IOException: unten", report, StringComparison.Ordinal);
        Assert.Contains("Zeit:", report, StringComparison.Ordinal);
    }

    [Fact]
    public void Zielinvocation_ohne_Innenmeldung_wird_erklaert()
    {
        var report = ExceptionFormatting.FullReport(
            new TargetInvocationException("niemand sagt was", null!), "Start");

        Assert.Contains("keine innere Exception", report, StringComparison.Ordinal);
    }

    [Fact]
    public void TopFrames_nennt_Datei_Zeile_und_Methode_des_eigenen_Codes()
    {
        var error = Fange(() => Wirft_von_hier());

        var frames = ExceptionFormatting.TopFrames(error);

        Assert.NotEmpty(frames);
        var erste = frames[0];
        Assert.Contains("ExceptionFormattingTests.cs:", erste, StringComparison.Ordinal);
        Assert.Contains("Wirft_von_hier", erste, StringComparison.Ordinal);
        Assert.Contains(":", erste, StringComparison.Ordinal);
    }

    [Fact]
    public void TopFrames_bleibt_leer_wenn_kein_eigener_Frame_im_Stack_steht()
    {
        // Eine Ausnahme ohne Stack (z. B. aus einem anderen Kontext) darf nicht werfen.
        Assert.Empty(ExceptionFormatting.TopFrames(new InvalidOperationException("ohne Stack")));
    }

    [Fact]
    public void TopFrames_bricht_nicht_bei_kaputter_Exception()
    {
        var frames = ExceptionFormatting.TopFrames(new BoomException());

        Assert.NotNull(frames);
    }

    [Fact]
    public void Absturzdialog_zeigt_Ursache_Fundstelle_und_Logpfad()
    {
        var error = Fange(() => Wirft_von_hier());

        var text = ExceptionFormatting.CrashDialogText(error, @"C:\Temp\companion.log");

        Assert.Contains("unerwarteten Fehler", text, StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException: Testfehler aus der Tiefe", text, StringComparison.Ordinal);
        Assert.Contains("Fundstelle (unser Code):", text, StringComparison.Ordinal);
        Assert.Contains("Wirft_von_hier", text, StringComparison.Ordinal);
        Assert.Contains(@"C:\Temp\companion.log", text, StringComparison.Ordinal);
        Assert.Contains("Die App", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Absturzdialog_sagt_es_wenn_kein_Log_geschrieben_werden_konnte()
    {
        var text = ExceptionFormatting.CrashDialogText(new IOException("platt"), null);

        Assert.Contains("Ein Log konnte nicht geschrieben werden", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Fundstelle", text, StringComparison.Ordinal);   // ohne Stack nichts behaupten
    }

    /// <summary>Führt aus und gibt die gefangene Ausnahme zurück (Stacktrace inklusive) –
    /// nur so hat der Test einen echten Stack zum Auswerten.</summary>
    private static Exception Fange(Action aktion)
    {
        try
        {
            aktion();
        }
        catch (Exception ex)
        {
            return ex;
        }

        throw new InvalidOperationException("Der Test erwartet eine Ausnahme – es kam keine.");
    }

    private static void Wirft_von_hier() => throw new InvalidOperationException("Testfehler aus der Tiefe");

    private sealed class BoomException : Exception
    {
        public override string Message => throw new InvalidOperationException("Message selbst wirft");
    }
}

public class XamlLocationTests
{
        private sealed class FakeXamlParseException : Exception
        {
            public int LineNumber { get; } = 152;
            public int LinePosition { get; } = 21;
            public FakeXamlParseException(Exception inner) : base("Die Angabe wurde nicht gefunden.", inner) { }
        }

        [Fact]
        public void Kurzmeldung_nimmt_die_Zeilennummer_der_XamlAusnahme_mit()
        {
            var error = new System.Reflection.TargetInvocationException(
                "Exception has been thrown by the target of an invocation.",
                new FakeXamlParseException(new InvalidOperationException("Wert außerhalb des gültigen Bereichs. (Parameter 'index')")));

            var message = error.ShortMessage();

            Assert.Contains("InvalidOperationException: Wert außerhalb", message, StringComparison.Ordinal);
            Assert.Contains("Zeile 152, Position 21", message, StringComparison.Ordinal);
        }

        [Fact]
        public void XamlLocation_liefert_null_ohne_XamlAusnahme()
        {
            Assert.Null(ExceptionFormatting.XamlLocation(new IOException("normal")));
        }
    }
