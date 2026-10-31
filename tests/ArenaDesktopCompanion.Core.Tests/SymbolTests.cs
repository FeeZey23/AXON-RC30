using System.Text.RegularExpressions;
using ArenaDesktopCompanion.Core.Icons;

namespace ArenaDesktopCompanion.Core.Tests;

/// <summary>
/// Die Lehre aus dem Fenster, das nur Kästchen zeigte: Ein Symbol, das von einer Schrift auf dem
/// Zielrechner abhängt, ist keine Zusage. Diese Prüfungen halten den Weg frei, der ohne jede
/// Schrift auskommt – Vektoren im Programm – und verhindern den Rückfall in Symbolschrift und Emoji.
/// </summary>
public class SymbolTests
{
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ArenaDesktopCompanion.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repo-Wurzel nicht gefunden.");
    }

    private static IEnumerable<string> Quelldateien()
    {
        var root = RepositoryRoot();
        foreach (var ordner in new[] { "src" })
        {
            foreach (var datei in Directory.GetFiles(Path.Combine(root, ordner), "*.*", SearchOption.AllDirectories)
                         .Where(d => !d.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                                     && !d.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)))
            {
                if (datei.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)
                    || datei.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                    || datei.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
                {
                    yield return datei;
                }
            }
        }
    }

    /// <summary>
    /// Kein Emoji und kein Zeichen aus dem privaten Unicode-Bereich (dort liegen die Symbolschriften:
    /// U+E700… sind „Segoe MDL2 Assets“, U+E000… „Segoe Fluent Icons“). Beides war im Fenster als
    /// leeres Kästchen zu sehen, weil die Schrift auf dem Zielrechner fehlte.
    /// </summary>
    [Fact]
    public void Kein_Emoji_und_keine_Symbolschrift_im_Programm()
    {
        var verboten = new Regex(@"[\uE000-\uF8FF]|[\uD83C-\uDBFF][\uDC00-\uDFFF]|\u26D4|\uFE0F", RegexOptions.Compiled);
        var funde = new List<string>();

        foreach (var datei in Quelldateien())
        {
            var text = File.ReadAllText(datei);
            foreach (Match treffer in verboten.Matches(text))
            {
                var zeile = text[..treffer.Index].Count(c => c == '\n') + 1;
                funde.Add($"{Path.GetFileName(datei)}:{zeile} U+{char.ConvertToUtf32(treffer.Value, 0):X4}");
            }
        }

        Assert.Empty(funde);
    }

    /// <summary>
    /// Jedes Symbol muss eine Zeichnung haben – sonst zeigt der <c>Icon</c> eine Ausnahme oder nichts.
    /// </summary>
    [Fact]
    public void Jedes_Symbol_hat_eine_Zeichnung()
    {
        var fehlend = new List<string>();
        foreach (var kind in Enum.GetValues<IconKind>())
        {
            if (!IconGlyphs.Alle.Contains(kind))
            {
                fehlend.Add(kind.ToString());
                continue;
            }

            var glyph = IconGlyphs.For(kind);
            Assert.False(string.IsNullOrWhiteSpace(glyph.Path), $"{kind} hat einen leeren Pfad.");
        }

        Assert.Empty(fehlend);
    }

    /// <summary>
    /// Die Zeichenbefehle müssen die Syntax treffen, die WPF und SVG verstehen. Ein verlorenes
    /// Komma oder ein Buchstabe zu viel fällt sonst erst beim Zeichnen auf – also im Fenster.
    /// </summary>
    [Fact]
    public void Alle_Zeichenbefehle_sind_syntaktisch_gueltig()
    {
        var erlaubt = new Regex(@"^\s*(?:[MLHVCSQZTAmlhvcsqzt][-0-9.,\s]*)+$", RegexOptions.Compiled);
        var fehler = new List<string>();

        foreach (var kind in IconGlyphs.Alle)
        {
            var pfad = IconGlyphs.For(kind).Path;
            if (!erlaubt.IsMatch(pfad))
            {
                fehler.Add($"{kind}: {pfad}");
            }

            // Ein Pfad ohne Startpunkt (M/m) zeichnet nichts.
            if (!Regex.IsMatch(pfad, @"^\s*[Mm]"))
            {
                fehler.Add($"{kind}: beginnt nicht mit M");
            }
        }

        Assert.Empty(fehler);
    }

    /// <summary>
    /// Die Symbole liegen auf einem 24×24-Raster (wie Symbolschriften). Zeichnungen außerhalb
    /// werden am Rand abgeschnitten und sehen dann kaputt aus.
    /// </summary>
    [Fact]
    public void Alle_Zeichnungen_bleiben_im_24x24_Raster()
    {
        var zahl = new Regex(@"-?\d+(?:\.\d+)?", RegexOptions.Compiled);
        var ausreisser = new List<string>();

        foreach (var kind in IconGlyphs.Alle)
        {
            foreach (Match m in zahl.Matches(IconGlyphs.For(kind).Path))
            {
                if (double.TryParse(m.Value, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var wert)
                    && (wert < -1 || wert > 25))
                {
                    ausreisser.Add($"{kind}: {wert}");
                }
            }
        }

        Assert.Empty(ausreisser);
    }
}
