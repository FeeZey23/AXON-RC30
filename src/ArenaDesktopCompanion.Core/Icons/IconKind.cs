namespace ArenaDesktopCompanion.Core.Icons;

/// <summary>
/// Die Symbole der Oberfläche.
/// </summary>
/// <remarks>
/// Warum eigene Zeichnungen und keine Symbolschrift („Segoe Fluent Icons“)?
/// Genau daran ist die Oberfläche gescheitert: Die Schrift gibt es erst ab Windows 11; auf einem
/// Windows 10 ohne dieses Paket liefert die Ersatzschrift „Segoe MDL2 Assets“ für einen Teil der
/// Codes nichts – im Fenster stand dann ein leeres Kästchen. Ein Symbol, das nur auf manchen
/// Rechnern existiert, ist kein Symbol. Die Umrisse hier liegen als Geometrie im Programm, werden
/// bei jeder Größe aus den Vektoren gezeichnet und sind damit auch bei 150 % Skalierung scharf.
///
/// Die Koordinaten liegen alle auf einem 24×24-Raster (wie bei Symbolschriften üblich), damit sich
/// die Zeichnungen untereinander mischen lassen, ohne dass die Strichstärken auseinanderlaufen.
///
/// Die Daten liegen im Core, obwohl sie zum Aussehen gehören: Dort sind sie ohne Windows prüfbar
/// (Pfadsyntax, Rastergrenzen, Vollständigkeit) – siehe <c>SymbolTests</c>. Gezeichnet werden sie
/// im WPF-Projekt (<c>Icons/Icon.cs</c>); der Core kennt kein WPF.
/// </remarks>
public enum IconKind
{
    Folder,
    FolderEmpty,
    Download,
    Search,
    Copy,
    Save,
    Bolt,
    Back,
    Forward,
    Refresh,
    Home,
    Sliders,
    File,
    Code,
    Image,
    Archive,
    Markdown,
    Config,
    Check,
    Warn,
    Info,
    Chat,
    History,
    Terminal,
}

/// <summary>Die Zeichenbefehle zu jedem Symbol (SVG-artige Pfadsyntax, gefüllt oder gestrichelt).</summary>
public static class IconGlyphs
{
    /// <summary>Ein Symbol: Pfad plus die Angabe, ob er gefüllt oder als Linie gezeichnet wird.</summary>
    public readonly record struct Glyph(string Path, bool Filled);

    /// <summary>
    /// Strichstärke im 24×24-Raster. Gilt nur für die gestrichelten Symbole; bei 16 px
    /// Bildgröße ergibt das rund 1,2 px und bleibt auch bei 150 % Skalierung sauber.
    /// </summary>
    public const double StrokeWidth = 1.9;

    private static readonly Dictionary<IconKind, Glyph> Tabelle = new()
    {
        [IconKind.Folder] = new("M3,6.5 L8.5,6.5 L10.5,9 L21,9 L21,18 L3,18 Z", true),
        [IconKind.FolderEmpty] = new("M3,6.5 L8.5,6.5 L10.5,9 L21,9 L21,18 L3,18 Z", false),
        [IconKind.Download] = new("M12,4 L12,14.5 M7.5,10.5 L12,15 L16.5,10.5 M4,19 L20,19", false),
        [IconKind.Search] = new(
            "M10.5,4 C14.1,4 17,6.9 17,10.5 C17,14.1 14.1,17 10.5,17 C6.9,17 4,14.1 4,10.5 C4,6.9 6.9,4 10.5,4 Z M15.6,15.6 L20.5,20.5", false),
        [IconKind.Copy] = new("M9,3.5 L20,3.5 L20,14.5 M15,8 L4,8 L4,20.5 L15,20.5 Z", false),
        [IconKind.Save] = new("M4,4 L17,4 L20,7 L20,20 L4,20 Z M8,4 L8,10 L16,10 L16,4 M8,20 L8,14 L16,14 L16,20", false),
        [IconKind.Bolt] = new("M13.5,2 L6,13.5 L11.5,13.5 L10.5,22 L18,10.5 L12.5,10.5 Z", true),
        [IconKind.Back] = new("M14.5,5 L7.5,12 L14.5,19", false),
        [IconKind.Forward] = new("M9.5,5 L16.5,12 L9.5,19", false),
        [IconKind.Refresh] = new(
            "M20,12 C20,7.6 16.4,4 12,4 C9.2,4 6.7,5.4 5.2,7.6 M4,12 C4,16.4 7.6,20 12,20 C14.8,20 17.3,18.6 18.8,16.4 M5.2,3.6 L5.2,8 L9.6,8 M18.8,20.4 L18.8,16 L14.4,16", false),
        [IconKind.Home] = new("M4,11.5 L12,4 L20,11.5 M6.5,10 L6.5,20 L17.5,20 L17.5,10", false),
        [IconKind.Sliders] = new("M4,7 L20,7 M4,17 L20,17 M9,4.5 L9,9.5 M15,14.5 L15,19.5", false),
        [IconKind.File] = new("M6,3 L14,3 L18,7 L18,21 L6,21 Z M14,3 L14,7 L18,7", false),
        [IconKind.Code] = new(
            "M6,3 L14,3 L18,7 L18,21 L6,21 Z M14,3 L14,7 L18,7 M10.5,12 L8.5,15 L10.5,18 M13.5,12 L15.5,15 L13.5,18", false),
        [IconKind.Image] = new("M4,5 L20,5 L20,19 L4,19 Z M4,15.5 L9,11 L13.5,15 L16.5,12.5 L20,15.5", false),
        [IconKind.Archive] = new("M4,6 L20,6 L20,9 L4,9 Z M5.5,9 L5.5,20 L18.5,20 L18.5,9 M10,13 L14,13", false),
        [IconKind.Markdown] = new("M6,3 L14,3 L18,7 L18,21 L6,21 Z M14,3 L14,7 L18,7 M9,11.5 L9,17.5 M9,17.5 L7,15.5 M9,17.5 L11,15.5 M15,17.5 L15,11.5 M15,11.5 L13,13.5 M15,11.5 L17,13.5", false),
        [IconKind.Config] = new(
            "M6,3 L14,3 L18,7 L18,21 L6,21 Z M14,3 L14,7 L18,7 M9,11.5 L15,11.5 M9,15 L15,15 M9,18.5 L12.5,18.5", false),
        [IconKind.Check] = new("M5,12.5 L10,17.5 L19,6.5", false),
        [IconKind.Warn] = new("M12,3.5 L22,20.5 L2,20.5 Z", true),
        [IconKind.Chat] = new(
            "M4,6 C4,4.9 4.9,4 6,4 L18,4 C19.1,4 20,4.9 20,6 L20,14 C20,15.1 19.1,16 18,16 L11.5,16 L6.5,20 L6.5,16 L6,16 C4.9,16 4,15.1 4,14 Z", false),
        [IconKind.History] = new(
            "M12,4.6 C16.1,4.6 19.4,7.9 19.4,12 C19.4,16.1 16.1,19.4 12,19.4 C8.6,19.4 5.8,17.2 4.9,14.2 M12,7.6 L12,12.2 L15.6,14.2 M4,5.4 L4,9.8 L8.4,9.8", false),
        [IconKind.Terminal] = new(
            "M3.6,4.6 L20.4,4.6 L20.4,19.4 L3.6,19.4 Z M7.2,9.6 L10,12 L7.2,14.4 M11.8,15.2 L16,15.2", false),
        [IconKind.Info] = new(
            "M12,4 C16.4,4 20,7.6 20,12 C20,16.4 16.4,20 12,20 C7.6,20 4,16.4 4,12 C4,7.6 7.6,4 12,4 Z M12,10.5 L12,16.5 M12,7.5 L12,8.6", false),
    };

    public static Glyph For(IconKind kind)
        => Tabelle.TryGetValue(kind, out var glyph)
            ? glyph
            : throw new ArgumentOutOfRangeException(nameof(kind), kind, "Für dieses Symbol gibt es keine Zeichnung.");

    /// <summary>Alle Symbole – für die Prüfung im Test, dass keines vergessen wurde.</summary>
    public static IReadOnlyCollection<IconKind> Alle => Tabelle.Keys;
}
