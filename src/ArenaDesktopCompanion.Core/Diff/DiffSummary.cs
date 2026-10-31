namespace ArenaDesktopCompanion.Core.Diff;

public sealed record DiffResult(
    IReadOnlyList<string> Lines,
    int Added,
    int Removed,
    bool IsBinary,
    bool Truncated)
{
    public bool Identical => !IsBinary && Added == 0 && Removed == 0;
    public string Label => IsBinary
        ? "Binärdatei – kein Zeilenvergleich"
        : $"+{Added} / -{Removed} Zeilen{(Truncated ? " (gekürzt)" : string.Empty)}";
}

/// <summary>
/// Kleiner, testbarer Zeilenvergleich für den Konflikt-Dialog – diff-patch-artige Anzeige,
/// damit man auf einen Blick sieht, was Arena anders macht. Kein externes Werkzeug nötig.
/// </summary>
public static class DiffSummary
{
    private const int MaxLinesPerSide = 2000;
    private const int MaxCellCount = 4_000_000;

    public static DiffResult Build(string? mine, string? arena, int maxLines = 60)
    {
        var left = Split(mine);
        var right = Split(arena);

        if (IsBinary(left) || IsBinary(right))
        {
            return new DiffResult(Array.Empty<string>(), 0, 0, IsBinary: true, Truncated: false);
        }

        var truncated = left.Length > MaxLinesPerSide || right.Length > MaxLinesPerSide
                        || (long)left.Length * right.Length > MaxCellCount;
        if (truncated)
        {
            left = Take(left, MaxLinesPerSide);
            right = Take(right, MaxLinesPerSide);
        }

        var ops = Diff(left, right);
        var added = ops.Count(o => o.Kind == '+');
        var removed = ops.Count(o => o.Kind == '-');

        // Kontextzeilen bleiben drin – ohne sie ist nicht erkennbar, WO sich die Dateien unterscheiden.
        var changes = ops.Count(o => o.Kind != ' ');
        var lines = new List<string>();
        foreach (var op in ops.Take(maxLines))
        {
            lines.Add(op.Kind == ' ' ? $"  {op.Text}" : $"{op.Kind} {op.Text}");
        }
        var dropped = ops.Count - lines.Count;
        if (dropped > 0 || truncated)
        {
            lines.Add($"  … {dropped + (truncated && dropped <= 0 ? 1 : 0)} weitere Zeilen abgeschnitten (gekürzt)");
        }

        return new DiffResult(lines, added, removed, IsBinary: false, Truncated: dropped > 0 || truncated);
    }

    private static string[] Split(string? text)
        => string.IsNullOrEmpty(text)
            ? Array.Empty<string>()
            : text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');

    private static string[] Take(string[] lines, int max)
        => lines.Length <= max ? lines : lines[..max];

    private static bool IsBinary(string[] lines)
    {
        foreach (var line in lines)
        {
            if (line.Contains('\0'))
            {
                return true;
            }
        }
        return false;
    }

    private readonly record struct Op(char Kind, string Text);

    /// <summary>Classical LCS-DP, dann Rückwärtslauf zu +/-/ -Opfolgen.</summary>
    private static IReadOnlyList<Op> Diff(string[] left, string[] right)
    {
        var n = left.Length;
        var m = right.Length;
        var lcs = new int[n + 1, m + 1];

        for (var i = n - 1; i >= 0; i--)
        {
            for (var j = m - 1; j >= 0; j--)
            {
                lcs[i, j] = string.Equals(left[i], right[j], StringComparison.Ordinal)
                    ? lcs[i + 1, j + 1] + 1
                    : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            }
        }

        var ops = new List<Op>(n + m);
        int x = 0, y = 0;
        while (x < n && y < m)
        {
            if (string.Equals(left[x], right[y], StringComparison.Ordinal))
            {
                ops.Add(new Op(' ', left[x]));
                x++; y++;
            }
            else if (lcs[x + 1, y] >= lcs[x, y + 1])
            {
                ops.Add(new Op('-', left[x]));
                x++;
            }
            else
            {
                ops.Add(new Op('+', right[y]));
                y++;
            }
        }
        while (x < n)
        {
            ops.Add(new Op('-', left[x++]));
        }
        while (y < m)
        {
            ops.Add(new Op('+', right[y++]));
        }
        return ops;
    }
}
