using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using ArenaDesktopCompanion.Core.Icons;

namespace ArenaDesktopCompanion.Icons;

/// <summary>
/// Zeichnet ein Symbol aus <see cref="IconGlyphs"/> – als Vektor, in der Farbe des umgebenden
/// Textes. Statt in XAML steht hier Code, weil Geometrien aus Zeichenketten kommen: XAML kann das
/// zwar auch, aber dann stünde das Aussehen an jeder Verwendungsstelle erneut.
/// </summary>
/// <remarks>
/// Die Farbe wird vererbt (<c>TextElement.Foreground</c>): Steht das Symbol in einem Knopf mit
/// heller Schrift, ist es hell; in der Statuszeile grau, ohne Zusatzangabe.
/// </remarks>
public sealed class Icon : FrameworkElement
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(IconKind), typeof(Icon),
        new FrameworkPropertyMetadata(IconKind.File, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(Icon),
        new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
        typeof(Icon), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    public IconKind Kind
    {
        get => (IconKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    /// <summary>Kantenlänge in geräteunabhängigen Pixeln (Standard 16).</summary>
    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(Size, Size);

    protected override void OnRender(DrawingContext drawingContext)
    {
        var glyph = IconGlyphs.For(Kind);
        if (string.IsNullOrEmpty(glyph.Path) || Size <= 0)
        {
            return;
        }

        // Pull, damit die Strichstärke mitskaliert und die Ränder nicht beschnitten werden.
        var skalierung = Size / 24.0;
        var geometrie = Geometry.Parse(glyph.Path);

        drawingContext.PushTransform(new ScaleTransform(skalierung, skalierung));
        if (glyph.Filled)
        {
            drawingContext.DrawGeometry(Foreground, null, geometrie);
        }
        else
        {
            var stift = new Pen(Foreground, IconGlyphs.StrokeWidth)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round,
            };
            stift.Freeze();
            drawingContext.DrawGeometry(null, stift, geometrie);
        }

        drawingContext.Pop();
    }
}
