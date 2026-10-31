using ArenaDesktopCompanion.Core.Conflicts;
using ArenaDesktopCompanion.Core.Diff;
using ArenaDesktopCompanion.Core.Sync;
using ArenaDesktopCompanion.Core.Tests.Support;

namespace ArenaDesktopCompanion.Core.Tests;

public class DiffSummaryTests
{
    [Fact]
    public void Zaehlt_Zeilen_und_zeigt_was_weg_und_was_neu_ist()
    {
        var diff = DiffSummary.Build("a1\nb2\nc3\n", "a1\nX\nc3\n");

        Assert.Equal(1, diff.Added);
        Assert.Equal(1, diff.Removed);
        Assert.Contains("+ X", diff.Lines, StringComparer.Ordinal);
        Assert.Contains("- b2", diff.Lines, StringComparer.Ordinal);
        Assert.Contains("  a1", diff.Lines, StringComparer.Ordinal);
        Assert.False(diff.IsBinary);
        Assert.True(diff.Identical == false);
    }

    [Fact]
    public void Identisch_ergibt_keine_Zeilen()
    {
        var diff = DiffSummary.Build("gleich\n", "gleich\n");

        Assert.True(diff.Identical);
        Assert.Equal(0, diff.Added + diff.Removed);
    }

    [Fact]
    public void Nur_andere_Zeilenenden_sind_kein_Unterschied()
    {
        var diff = DiffSummary.Build("a\r\nb\r\n", "a\nb\n");

        Assert.Equal(0, diff.Added + diff.Removed);
    }

    [Fact]
    public void Binaerdaten_bekommen_keinen_Zeilensalat()
    {
        var diff = DiffSummary.Build("a\0b", "c\0d");

        Assert.True(diff.IsBinary);
        Assert.Contains("Binär", diff.Label, StringComparison.Ordinal);
        Assert.Empty(diff.Lines);
    }

    [Fact]
    public void Grosse_Unterschiede_werden_gekuerzt_und_das_wird_gesagt()
    {
        var big = string.Concat(Enumerable.Repeat("zeile\n", 60));
        var other = string.Concat(Enumerable.Repeat("andere\n", 60));

        var diff = DiffSummary.Build(big, other, maxLines: 6);

        Assert.True(diff.Truncated);
        Assert.True(diff.Lines.Count <= 7);
        Assert.Contains("gekürzt", diff.Lines[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void Anzeigename_zaehlt_fuer_die_UI()
    {
        var diff = DiffSummary.Build("a\nb\n", "a\n");

        Assert.Contains("-1", diff.Label, StringComparison.Ordinal);
        Assert.Contains("+0", diff.Label, StringComparison.Ordinal);
    }
}
