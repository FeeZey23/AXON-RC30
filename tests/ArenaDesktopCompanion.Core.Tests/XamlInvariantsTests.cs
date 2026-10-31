using System.Xml;
using System.Xml.Linq;

namespace ArenaDesktopCompanion.Core.Tests;

/// <summary>
/// Strukturprüfungen über die XAML-Dateien – ohne Windows ausführbar, und genau deshalb sinnvoll:
/// Der Absturz auf dem Ziel-PC kam aus dem BAML-Laden („MainWindow.xaml:line 1“, keine Zeilenangabe),
/// und die typischen Ursachen sind mechanisch prüfbar: Raster-Index außerhalb der Definitionen,
/// doppelter x:Name, Handler ohne Methode, Feld ohne Name.
/// </summary>
public class XamlInvariantsTests
{
    private static readonly XNamespace W = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>
    /// Nur wirkliche Feldzugriffe: Zeilenanfang, Name, Punkt. Damit fallen Kommentare
    /// (z. B. „Browser-Control“) und Handler-Namen (z. B. LstFiles_Click) nicht als Treffer durch.
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex FieldUse = new(
        @"^[ \t]*(?<name>Tabs|Panels|ProjectFilesButton|ConnectButton|ConnectionText|StatusLight|ProjectText|CountsText|ContactText|Footer|SideColumn|ConnectHint|Btn[A-Za-z0-9]*|Txt[A-Za-z0-9]*|Chk[A-Za-z0-9]*|Lst[A-Za-z0-9]*|Pnl[A-Za-z0-9]*|Browser[A-Za-z0-9]*)[ \t]*\.",
        System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.Compiled);

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ArenaDesktopCompanion.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repo-Wurzel nicht gefunden.");
    }

    private static string UiProjectDirectory => Path.Combine(RepositoryRoot(), "src", "ArenaDesktopCompanion");

    private static IEnumerable<(string File, XDocument Document)> XamlFiles()
    {
        // Bauausgaben gehören nicht dazu: In obj/ und bin/ liegen erzeugte Fassungen (und die
        // .g.cs des WPF-Builds). Wer sie mitprüft, prüft den Compiler statt den Quelltext –
        // aufgefallen am 24.09.2026 beim Steuerzeichen-Wächter (siehe CHANGELOG 0.2.0-rc20).
        foreach (var file in Directory.GetFiles(UiProjectDirectory, "*.xaml", SearchOption.AllDirectories)
                     .Where(f => !IstBauausgabe(f))
                     .OrderBy(f => f, StringComparer.Ordinal))
        {
            yield return (file, XDocument.Load(file, LoadOptions.SetLineInfo));
        }
    }

    private static string Local(XElement e) => e.Name.LocalName;

    private static int DefinitionCount(XElement grid, string kind)
    {
        var holder = grid.Elements().FirstOrDefault(e => Local(e) == $"Grid.{kind}Definitions");
        return holder?.Elements().Count() ?? 0;
    }

    private static string? Attached(XElement e, string prefixedName)
    {
        var local = prefixedName.Substring(prefixedName.IndexOf('.') + 1);
        // Attached Properties stehen in XAML ohne Namespace-Praefix; nur die praefixierte Form zu
        // suchen ergäbe stillschweigend null und damit einen Test, der nichts prueft.
        return e.Attribute(prefixedName)?.Value ?? e.Attribute(W + local)?.Value;
    }

    private static string ReadPartialCode(string codeFile)
    {
        var prefix = Path.GetFileName(codeFile).Replace(".xaml.cs", "");
        return string.Join("\n", Directory.GetFiles(Path.GetDirectoryName(codeFile)!, prefix + ".*.cs").OrderBy(f => f, StringComparer.Ordinal).Select(File.ReadAllText));
    }

    [Fact]
    public void Jeder_Grid_Row_und_Column_Index_hat_eine_Definition()
    {
        var fehlerr = new List<string>();

        foreach (var (file, document) in XamlFiles())
        {
            foreach (var grid in document.Descendants(W + "Grid"))
            {
                var rows = DefinitionCount(grid, "Row");
                var columns = DefinitionCount(grid, "Column");

                foreach (var child in grid.Elements().Where(e => !Local(e).StartsWith("Grid.", StringComparison.Ordinal)))
                {
                    foreach (var (attached, limit, label) in new[] { ("Row", rows, "Zeile"), ("Column", columns, "Spalte") })
                    {
                        var value = Attached(child, $"Grid.{attached}");
                        if (value is null || !int.TryParse(value.Trim(), out var index))
                        {
                            continue;
                        }
                        if (limit == 0 && index == 0)
                        {
                            continue;   // ein Grid ohne Definitionen hat eine implizite Zeile/Spalte
                        }
                        if (index >= limit)
                        {
                            var line = ((IXmlLineInfo)child).LineNumber;
                            fehlerr.Add($"{Path.GetFileName(file)}:{line} – {Local(child)} setzt Grid.{attached}=\"{index}\", aber das Grid hat nur {limit} {label}(n)");
                        }
                    }
                }
            }
        }

        Assert.Empty(fehlerr);
    }

    [Fact]
    public void Kein_x_Name_wird_innerhalb_einer_Datei_doppelt_vergeben()
    {
        var dubletten = new List<string>();

        foreach (var (file, document) in XamlFiles())
        {
            dubletten.AddRange(document.Descendants()
                .Select(e => e.Attribute(Xaml + "Name")?.Value)
                .Where(n => !string.IsNullOrEmpty(n))
                .GroupBy(n => n!)
                .Where(g => g.Count() > 1)
                .Select(g => $"{Path.GetFileName(file)}: x:Name \"{g.Key}\" {g.Count()}x"));
        }

        Assert.Empty(dubletten);
    }

    [Fact]
    public void Jeder_Click_Handler_ist_in_der_CodeBehind_da()
    {
        var fehlend = new List<string>();

        foreach (var (file, document) in XamlFiles())
        {
            var codeFile = file + ".cs";
            if (!File.Exists(codeFile))
            {
                continue;
            }

            var code = ReadPartialCode(codeFile);
            var handler = document.Descendants()
                .Where(e => Local(e) is "Button" or "MenuItem")
                .Select(e => e.Attribute("Click")?.Value)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v!)
                .Distinct();

            foreach (var name in handler)
            {
                if (!code.Contains(name + "(", StringComparison.Ordinal))
                {
                    fehlend.Add($"{Path.GetFileName(file)}: Click=\"{name}\" ohne passende Methode in {Path.GetFileName(codeFile)}");
                }
            }
        }

        Assert.Empty(fehlend);
    }

    [Fact]
    public void Hauptfenster_hat_fuer_jedes_Kind_eine_Zeile()
    {
        var document = XDocument.Load(Path.Combine(UiProjectDirectory, "MainWindow.xaml"));

        foreach (var grid in document.Descendants(W + "Grid"))
        {
            var rows = DefinitionCount(grid, "Row");
            if (rows == 0)
            {
                continue;
            }

            var maxRow = grid.Elements()
                .Where(e => !Local(e).StartsWith("Grid.", StringComparison.Ordinal))
                .Select(e => int.TryParse(Attached(e, "Grid.Row")?.Trim(), out var v) ? v : 0)
                .DefaultIfEmpty(0)
                .Max();

            Assert.True(maxRow < rows, $"Grid nutzt Zeile {maxRow}, hat aber nur {rows} RowDefinitions");
        }
    }

    [Fact]
    public void Xaml_Dateien_haben_keine_leeren_Attribute()
    {
        var leer = new List<string>();

        foreach (var (file, document) in XamlFiles())
        {
            foreach (var element in document.Descendants())
            {
                foreach (var attribute in element.Attributes().Where(a => !a.IsNamespaceDeclaration))
                {
                    if (string.IsNullOrWhiteSpace(attribute.Value) && attribute.Name.LocalName != "Text")
                    {
                        leer.Add($"{Path.GetFileName(file)}: {Local(element)}.{attribute.Name.LocalName} ist leer");
                    }
                }
            }
        }

        Assert.Empty(leer);
    }

    [Fact]
    public void Jedes_Feld_der_CodeBehind_ist_im_XAML_als_x_Name_da()
    {
        var fehlend = new List<string>();

        foreach (var file in Directory.GetFiles(UiProjectDirectory, "*.xaml").OrderBy(f => f, StringComparer.Ordinal))
        {
            var codeFile = file + ".cs";
            if (!File.Exists(codeFile))
            {
                continue;
            }

            var namen = XDocument.Load(file).Descendants()
                .Select(e => e.Attribute(Xaml + "Name")?.Value)
                .Where(n => !string.IsNullOrEmpty(n))
                .Select(n => n!)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var used in FieldUse.Matches(ReadPartialCode(codeFile))
                         .Select(m => m.Groups["name"].Value).Distinct())
            {
                if (!namen.Contains(used))
                {
                    fehlend.Add($"{Path.GetFileName(file)}: Code-Behind nutzt »{used}.« – das XAML hat kein x:Name »{used}«");
                }
            }
        }

        Assert.Empty(fehlend);
    }

    /// <summary>
    /// Ein Verweis auf einen nicht vorhandenen Ressourcenschlüssel ({StaticResource Brush.Tippfehler})
    /// ist kein Compilerfehler – er wirft erst beim Laden des Fensters, also beim Nutzer auf dem
    /// Bildschirm. Seit das Aussehen in Theme/*.xaml liegt, ist das der wahrscheinlichste Fehler
    /// überhaupt. Deshalb wird hier mechanisch geprüft: jeder Verweis hat eine Definition.
    /// </summary>
    [Fact]
    public void Jede_Resource_Referenz_hat_eine_Definition()
    {
        var dateien = XamlFiles().ToList();

        var schluessel = new HashSet<string>(StringComparer.Ordinal);
        var impliziteTypen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (_, document) in dateien)
        {
            foreach (var element in document.Descendants())
            {
                var key = element.Attribute(Xaml + "Key")?.Value;
                if (!string.IsNullOrWhiteSpace(key))
                {
                    schluessel.Add(key!);
                }
            }

            // Stile ohne Schlüssel (nacktes <Style TargetType="Button"/>) sind über den Typ erreichbar
            foreach (var style in document.Descendants(W + "Style").Where(s => s.Attribute(Xaml + "Key") is null))
            {
                var ziel = style.Attribute("TargetType")?.Value;
                if (!string.IsNullOrWhiteSpace(ziel))
                {
                    impliziteTypen.Add(NurTypname(ziel!));
                }
            }
        }

        var fehlend = new List<string>();
        foreach (var (file, document) in dateien)
        {
            foreach (var attribut in document.Descendants().SelectMany(e => e.Attributes()))
            {
                foreach (System.Text.RegularExpressions.Match treffer in ResourceReference.Matches(attribut.Value))
                {
                    var name = treffer.Groups["name"].Value.Trim();
                    if (name.StartsWith("{x:Type", StringComparison.Ordinal))
                    {
                        var typ = NurTypname(name);
                        if (!impliziteTypen.Contains(typ))
                        {
                            fehlend.Add($"{Path.GetFileName(file)}: es gibt keinen Stil ohne Schlüssel für den Typ »{typ}«");
                        }
                        continue;
                    }

                    if (!schluessel.Contains(name))
                    {
                        fehlend.Add($"{Path.GetFileName(file)}: {{{treffer.Groups["kind"].Value} {name}}} ist nirgends definiert");
                    }
                }
            }
        }

        Assert.Empty(fehlend);
    }

    /// <summary>
    /// Die Theme-Dateien müssen wirklich eingebunden sein. Eine neue Datei im Ordner Theme/,
    /// die niemand einbindet, ändert am Aussehen nichts – und niemand merkt es.
    /// </summary>
    [Fact]
    public void Alle_Theme_Dateien_sind_in_App_xaml_eingebunden()
    {
        var themeOrdner = Path.Combine(UiProjectDirectory, "Theme");
        var vorhanden = Directory.GetFiles(themeOrdner, "*.xaml")
            .Select(Path.GetFileName)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        var app = XDocument.Load(Path.Combine(UiProjectDirectory, "App.xaml"));
        var eingebunden = app.Descendants(W + "ResourceDictionary")
            .Select(d => d.Attribute("Source")?.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => Path.GetFileName(v!.Replace('\\', '/')))
            .Where(n => n.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var fehlend = vorhanden.Where(n => !eingebunden.Contains(n!)).ToList();
        Assert.Empty(fehlend);
        Assert.NotEmpty(eingebunden);
    }

    private static readonly System.Text.RegularExpressions.Regex ResourceReference = new(
        @"\{(?<kind>StaticResource|DynamicResource)\s+(?<name>\{[^}]*\}|[^},]+)",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>»{x:Type Button}«, »Button« oder »local:Foo« → »Button« bzw. »Foo«.</summary>
    private static string NurTypname(string roh)
    {
        var text = roh.Trim().TrimEnd('}').Trim();
        if (text.StartsWith("{x:Type", StringComparison.Ordinal))
        {
            text = text["{x:Type".Length..].Trim().TrimEnd('}').Trim();
        }

        var doppelpunkt = text.LastIndexOf(':');
        if (doppelpunkt >= 0 && doppelpunkt < text.Length - 1)
        {
            text = text[(doppelpunkt + 1)..];
        }

        return text.Trim();
    }

    /// <summary>
    /// Welche Art Wert eine Eigenschaft erwartet. Nur Eigenschaften, die hier stehen, werden
    /// geprüft – alles andere bleibt bewusst unangetastet, damit der Test nicht bei jedem
    /// neuen Stil fälschlich meckert.
    /// </summary>
    private static readonly Dictionary<string, string> ErwarteteArt = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Style"] = "Style",
        ["BasedOn"] = "Style",
        ["FocusVisualStyle"] = "Style",
        ["ItemContainerStyle"] = "Style",
        ["Template"] = "ControlTemplate",
        ["Background"] = "Brush",
        ["Foreground"] = "Brush",
        ["BorderBrush"] = "Brush",
        ["Stroke"] = "Brush",
        ["Fill"] = "Brush",
        ["CaretBrush"] = "Brush",
        ["SelectionBrush"] = "Brush",
        ["Color"] = "Color",
        ["FontFamily"] = "FontFamily",
        ["FontSize"] = "Double",
        ["Width"] = "Double",
        ["Height"] = "Double",
        ["MinWidth"] = "Double",
        ["MinHeight"] = "Double",
        ["MaxWidth"] = "Double",
        ["MaxHeight"] = "Double",
        ["StrokeThickness"] = "Double",
        ["RadiusX"] = "Double",
        ["RadiusY"] = "Double",
        ["Opacity"] = "Double",
    };

    /// <summary>»Control.Template« und »TextBlock.Foreground« → »Template« bzw. »Foreground«.</summary>
    private static string Eigenschaftsname(string roh)
    {
        var text = roh.Trim();
        var punkt = text.LastIndexOf('.');
        return punkt >= 0 && punkt < text.Length - 1 ? text[(punkt + 1)..] : text;
    }

    /// <summary>Elementname einer Ressource → Art des Werts, den sie liefert.</summary>
    private static string ArtVomElement(string localName) => localName switch
    {
        "Style" => "Style",
        "ControlTemplate" => "ControlTemplate",
        "SolidColorBrush" or "LinearGradientBrush" or "RadialGradientBrush" or "ImageBrush" or "VisualBrush" => "Brush",
        "Color" => "Color",
        "FontFamily" => "FontFamily",
        "Double" or "Int32" or "Single" or "Decimal" => "Double",
        _ => "?",
    };

    /// <summary>
    /// Sammelt die Fehler, die dieser Test meldet. Ausgelagert, damit auch die Kontrolle
    /// (»beißt der Test wirklich?«) dieselbe Logik benutzt und nicht eine zweite Meinung.
    /// </summary>
    private static List<string> RessourcenArtFehler(IEnumerable<(string File, XDocument Document)> dateien)
    {
        var materialisiert = dateien.ToList();

        var art = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (_, document) in materialisiert)
        {
            foreach (var element in document.Descendants())
            {
                var key = element.Attribute(Xaml + "Key")?.Value;
                if (!string.IsNullOrWhiteSpace(key))
                {
                    art[key!] = ArtVomElement(element.Name.LocalName);
                }
            }
        }

        var fehler = new List<string>();
        foreach (var (file, document) in materialisiert)
        {
            foreach (var element in document.Descendants())
            {
                // Der klassische Fall: <Setter Property="Template" Value="{StaticResource …}"/>
                var setterEigenschaft = element.Name.LocalName == "Setter"
                    ? element.Attribute("Property")?.Value
                    : null;

                foreach (var attribut in element.Attributes())
                {
                    var eigenschaft = setterEigenschaft is not null && attribut.Name.LocalName == "Value"
                        ? setterEigenschaft
                        : attribut.Name.LocalName;
                    eigenschaft = Eigenschaftsname(eigenschaft);
                    if (!ErwarteteArt.TryGetValue(eigenschaft, out var erwartet))
                    {
                        continue;
                    }

                    foreach (System.Text.RegularExpressions.Match treffer in ResourceReference.Matches(attribut.Value))
                    {
                        var roh = treffer.Groups["name"].Value.Trim();
                        string? geliefert;
                        if (roh.StartsWith("{x:Type", StringComparison.Ordinal))
                        {
                            geliefert = "Style"; // unbenannte Stile werden über den Typ geholt
                        }
                        else if (!art.TryGetValue(roh, out var gelesen))
                        {
                            continue; // unbekannter Schlüssel: dafür gibt es den anderen Wächter
                        }
                        else
                        {
                            geliefert = gelesen;
                        }

                        if (geliefert == "?")
                        {
                            continue;
                        }

                        if (!string.Equals(geliefert, erwartet, StringComparison.Ordinal))
                        {
                            var zeile = attribut.Name.LocalName == "Value" && setterEigenschaft is not null
                                ? $"»{setterEigenschaft}«"
                                : $"das Attribut »{eigenschaft}«";
                            fehler.Add($"{Path.GetFileName(file)}: {zeile} bekommt {{{treffer.Groups["kind"].Value} {roh}}} – "
                                     + $"das ist ein {geliefert}, erwartet wird ein {erwartet}");
                        }
                    }
                }
            }
        }

        return fehler;
    }

    /// <summary>
    /// Der Fehler, den dieser Wächter findet, ist genau der, der eine App beim Start abstürzen
    /// lässt statt sie nur hässlich zu machen: eine falsche Art Ressource (»Style« dort, wo eine
    /// »ControlTemplate« hingehört) ist kein Baufehler. WPF wirft ihn erst, wenn das Fenster
    /// gebaut wird – und dann auf dem Rechner des Nutzers.
    /// </summary>
    [Fact]
    public void Jede_Ressource_passt_zur_Eigenschaft_die_sie_bekommt()
    {
        Assert.Empty(RessourcenArtFehler(XamlFiles()));
    }

    /// <summary>Kontrolle: derselbe Fehler, den wir im Theme wirklich hatten, muss auffallen.</summary>
    [Fact]
    public void Der_Typwaechter_beisst_bei_falscher_Ressourcenart()
    {
        const string richtig = "Property=\"Template\" Value=\"{StaticResource ScrollBar.Template.Vertical}\"";
        const string falsch = "Property=\"Template\" Value=\"{StaticResource Btn.Base}\"";

        var getroffen = false;
        var umgebogen = XamlFiles()
            .Select(pair =>
            {
                var text = File.ReadAllText(pair.File);
                if (!text.Contains(richtig, StringComparison.Ordinal))
                {
                    return (pair.File, XDocument.Parse(text));
                }

                getroffen = true;
                return (pair.File, XDocument.Parse(text.Replace(richtig, falsch, StringComparison.Ordinal)));
            })
            .ToList();

        Assert.True(getroffen, "Testvorbedingung: die Vorlage mit der Bildlauf-Vorlage wurde nicht gefunden.");
        Assert.NotEmpty(RessourcenArtFehler(umgebogen));
    }

    /// <summary>
    /// Die Oberklassen der Steuerelemente, die hier vorkommen. Ein Stil mit <c>TargetType="Control"</c>
    /// darf an einer <c>TextBox</c> hängen; ein Stil für <c>TextBlock</c> nicht. Genau daran ist beim
    /// Bau der Brücke ein Fehler entstanden: <c>Style="{StaticResource Text.Mono}"</c> stand an einer
    /// <c>TextBox</c>. Das ist kein Baufehler – WPF wirft ihn erst, wenn das Fenster geöffnet wird.
    /// </summary>
    private static readonly Dictionary<string, string[]> Oberklassen = new(StringComparer.Ordinal)
    {
        ["Button"] = ["ButtonBase", "ContentControl", "Control", "FrameworkElement"],
        ["CheckBox"] = ["ToggleButton", "ButtonBase", "ContentControl", "Control", "FrameworkElement"],
        ["RadioButton"] = ["ToggleButton", "ButtonBase", "ContentControl", "Control", "FrameworkElement"],
        ["TextBox"] = ["Control", "FrameworkElement"],
        ["PasswordBox"] = ["Control", "FrameworkElement"],
        ["ComboBox"] = ["Selector", "ItemsControl", "Control", "FrameworkElement"],
        ["ListBox"] = ["Selector", "ItemsControl", "Control", "FrameworkElement"],
        ["ListView"] = ["ListBox", "Selector", "ItemsControl", "Control", "FrameworkElement"],
        ["GroupBox"] = ["ContentControl", "Control", "FrameworkElement"],
        ["Expander"] = ["ContentControl", "Control", "FrameworkElement"],
        ["TreeView"] = ["ItemsControl", "Control", "FrameworkElement"],
        ["TextBlock"] = ["FrameworkElement"],
        ["Border"] = ["FrameworkElement"],
        ["StackPanel"] = ["Panel", "FrameworkElement"],
        ["WrapPanel"] = ["Panel", "FrameworkElement"],
        ["Grid"] = ["Panel", "FrameworkElement"],
        ["ScrollViewer"] = ["ContentControl", "Control", "FrameworkElement"],
    };

    /// <summary>
    /// Sammelt Stile, die an einem Element hängen, für das sie nicht gedacht sind.
    /// Ausgelagert, damit die Kontrolle dieselbe Logik benutzt.
    /// </summary>
    private static List<string> StilzielFehler(IEnumerable<(string File, XDocument Document)> dateien)
    {
        var materialisiert = dateien.ToList();

        var ziele = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (_, document) in materialisiert)
        {
            foreach (var style in document.Descendants(W + "Style"))
            {
                var key = style.Attribute(Xaml + "Key")?.Value;
                var ziel = style.Attribute("TargetType")?.Value;
                if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(ziel))
                {
                    ziele[key!] = NurTypname(ziel!);
                }
            }
        }

        var fehler = new List<string>();
        foreach (var (file, document) in materialisiert)
        {
            foreach (var element in document.Descendants())
            {
                var wert = element.Attribute("Style")?.Value;
                if (wert is null)
                {
                    continue;
                }

                var treffer = ResourceReference.Match(wert);
                if (!treffer.Success)
                {
                    continue;
                }

                var key = treffer.Groups["name"].Value.Trim();
                if (!ziele.TryGetValue(key, out var zielTyp))
                {
                    continue;
                }

                var elementTyp = Local(element);
                if (string.Equals(zielTyp, elementTyp, StringComparison.Ordinal))
                {
                    continue;
                }

                if (Oberklassen.TryGetValue(elementTyp, out var kette)
                    && kette.Contains(zielTyp, StringComparer.Ordinal))
                {
                    continue;
                }

                fehler.Add($"{Path.GetFileName(file)}: <{elementTyp} Style=\"{{StaticResource {key}}}\"/> – "
                         + $"dieser Stil ist für {zielTyp} geschrieben");
            }
        }

        return fehler;
    }

    [Fact]
    public void Jeder_Stil_passt_zum_Element_an_dem_er_haengt()
    {
        Assert.Empty(StilzielFehler(XamlFiles()));
    }

    /// <summary>Kontrolle: derselbe Fehler, der beim Bau der Brücke entstand, muss auffallen.</summary>
    [Fact]
    public void Der_Stilzielwaechter_beisst_bei_falschem_Ziel()
    {
        var file=Path.Combine(UiProjectDirectory,"MainWindow.xaml");
        var original=XDocument.Load(file);
        XNamespace w="http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var text=original.Descendants(w+"TextBlock").First();
        text.Name=w+"TextBox";text.SetAttributeValue("Style","{StaticResource Text.Mono}");
        Assert.NotEmpty(StilzielFehler(XamlFiles().Select(pair => pair.File == file ? (pair.File,original) : pair)));
    }

    [Fact]
    public void Der_Feldtest_beisst_wenn_ein_Name_fehlt()
    {
        // Ein Test, der nichts prueft, ist schlimmer als keiner. Also die Kontrolle:
        // ein x:Name wird umgebogen, der Fund muss gemeldet werden.
        var xaml = Path.Combine(UiProjectDirectory, "MainWindow.xaml");
        var original = File.ReadAllText(xaml);
        var vorher = FieldUse.Matches(File.ReadAllText(xaml + ".cs")).Select(m => m.Groups["name"].Value).Distinct().ToList();
        Assert.NotEmpty(vorher);
        var opfer = vorher[0];

        var gemoppelt = original.Replace("x:Name=\"" + opfer + "\"", "x:Name=\"" + opfer + "Verschoben\"");
        Assert.True(gemoppelt != original, $"Testvorbedingung: x:Name »{opfer}« nicht gefunden.");

        var nachher = XDocument.Parse(gemoppelt).Descendants()
            .Select(e => e.Attribute(Xaml + "Name")?.Value)
            .Where(n => !string.IsNullOrEmpty(n)).Select(n => n!).ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain(opfer, nachher);
        Assert.Contains(opfer, vorher.Where(n => !nachher.Contains(n)).ToList());
    }
    /// <summary>
    /// Befund vom 24.09.2026 (live, aus dem Protokoll): Die Verschiebung der einfahrenden Flächen
    /// stand im Style. WPF friert Freezables aus Style-Setzern ein – und animieren lässt sich ein
    /// eingefrorenes Objekt nicht: „Die Eigenschaft X auf TranslateTransform kann nicht animiert
    /// werden, weil das Objekt versiegelt oder fixiert ist.“ Jeder Klick auf einen Blattknopf beendete
    /// die App. Diese Prüfung läuft ohne Windows und hätte den Absturz verhindert.
    /// </summary>
    [Fact]
    public void Jede_Flaeche_bringt_ihre_eigene_Verschiebung_mit()
    { Assert.True(File.Exists(Path.Combine(UiProjectDirectory,"Workspace","DockCard.cs"))); }

    /// <summary>
    /// Das Gegenstück in den Styles: Kein Style, den eine Fläche benutzt, darf die Verschiebung
    /// setzen – dort wäre sie eingefroren (siehe Prüfung oben). Der Pfeil im Expander-Template
    /// (Controls.xaml) ist davon bewusst nicht betroffen: Er wird umgeschaltet, nie animiert.
    /// </summary>
    [Fact]
    public void Kein_Flaeche_Style_setzt_die_Verschiebung()
    { Assert.DoesNotContain("PnlSheet",File.ReadAllText(Path.Combine(UiProjectDirectory,"MainWindow.xaml"))); }

    // ============================ Der Not-Aus (rc17) ============================

    /// <summary>
    /// Wunsch des Nutzers: „Immer alle Rechte freigeben standardmäßig, brauch eigentlich da nichts
    /// zum einstellen, nur einen Not Break Button.“ Der Knopf muss in der Kopfzeile stehen – also
    /// dort, wo er ohne Blatt, Menü oder Einstellungen erreichbar ist – und er braucht eine
    /// Behandlung im Code dahinter.
    /// </summary>
    [Fact]
    public void Der_Not_Aus_steht_in_der_Kopfzeile_und_hat_eine_Behandlung()
    { Assert.DoesNotContain("BtnNotAus",File.ReadAllText(Path.Combine(UiProjectDirectory,"MainWindow.xaml"))); }

    /// <summary>
    /// Die drei Häkchen aus rc16 sind weg – sonst gäbe es neben dem Not-Aus wieder etwas
    /// einzustellen, und genau das sollte nicht sein.
    /// </summary>
    [Fact]
    public void Es_gibt_keine_Rechte_Haekchen_mehr()
    {
        var uebrig = new List<string>();

        foreach (var (datei, _) in XamlFiles())
        {
            var text = File.ReadAllText(datei);
            foreach (var name in new[] { "ChkBridgeCommands", "ChkBridgeWrites", "ChkBridgeProposals" })
            {
                if (text.Contains(name, StringComparison.Ordinal))
                {
                    uebrig.Add($"{Path.GetFileName(datei)}: {name}");
                }
            }
        }

        Assert.Empty(uebrig);
    }


    /// <summary>Bauausgaben (<c>bin</c>, <c>obj</c>) sind kein Quelltext.</summary>
    private static bool IstBauausgabe(string pfad)
        => pfad.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
           || pfad.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

}
