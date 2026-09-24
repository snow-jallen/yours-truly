using UglyToad.PdfPig.Content;

namespace YoursTruly.Core.Import;

/// <summary>Turns a page's glyphs back into lines and rows.
///
/// A directory report is usually a rendered HTML table with no rules or delimiters in
/// its text, so the only structure available is geometry. Two facts drive everything
/// here, both measured from real exports:
///
///  * Cells are vertically CENTRED, not top-aligned. A cell wrapping to three lines
///    sits at the row's centre +/- one line height, a two-line cell at +/- half of
///    one. Glyph baselines therefore land on a grid of half the line height, and a
///    row's first line does not line up with its neighbours' first lines.
///  * Rows are separated by more vertical space than the lines inside them, by a ratio
///    that differs from file to file. How much more used to be a number written down
///    per report; it is now read off the page — see <see cref="RowBreak"/>.
/// </summary>
public static class PdfTableReader
{
    /// <summary>Two baselines closer than this are the same line of text. PdfPig
    /// reports a true baseline per glyph, so descenders need no special handling.</summary>
    private const double BaselineTolerance = 1.5;

    /// <summary>Space glyphs are kept: they carry the report's own word breaks, which
    /// is more reliable than inferring every space from a horizontal gap.</summary>
    public static IReadOnlyList<TextLine> ReadLines(Page page)
    {
        var groups = new List<(double Sum, int Count, List<Letter> Letters)>();

        foreach (var letter in page.Letters.OrderByDescending(l => l.StartBaseLine.Y))
        {
            var y = letter.StartBaseLine.Y;
            var placed = false;
            for (var i = 0; i < groups.Count; i++)
            {
                var g = groups[i];
                if (Math.Abs(g.Sum / g.Count - y) > BaselineTolerance) continue;
                g.Letters.Add(letter);
                groups[i] = (g.Sum + y, g.Count + 1, g.Letters);
                placed = true;
                break;
            }
            if (!placed) groups.Add((y, 1, [letter]));
        }

        return groups
            .Select(g => new TextLine(g.Sum / g.Count, g.Letters))
            .OrderByDescending(l => l.Baseline)
            .ToList();
    }

    /// <summary>The vertical gap at which one row ends and the next begins, worked out
    /// from the page itself.
    ///
    /// This used to be a measured constant per report, which is what stopped the app
    /// reading anything but the three reports somebody had measured. The page can be
    /// asked instead: the gaps between consecutive lines fall into two heaps — the
    /// small one inside a wrapped row, the big one between rows — and the threshold is
    /// the split between them. Otsu's method finds it, which is the same one-dimensional
    /// two-means question as choosing black from white in a scan.
    ///
    /// The catch that a relative rule normally falls into is a page where every row is
    /// a single line: there is only one heap, and any split of it cuts rows in half.
    /// So the two heaps have to be far enough apart to be believed —
    /// <see cref="Separated"/> — and when they are not, every line is its own row,
    /// which is exactly right for a page with nothing wrapped on it.</summary>
    public static double RowBreak(IReadOnlyList<TextLine> lines)
    {
        var size = MedianTextSize(lines);
        var gaps = Gaps(lines);
        if (gaps.Count == 0) return double.MaxValue;

        // Gaps far larger than the text are a heading, a footer or the end of the
        // table, never a wrapped cell. Leaving them in drags the split up the page.
        var considered = gaps.Where(g => g <= size * 4).ToList();

        return TwoHeaps(considered) ?? size * LineAndAHalf;
    }

    /// <summary>What to use when the page will not say: a gap of more than one and a
    /// half times the text size starts a new row.
    ///
    /// Measured, not guessed. Across every report to hand, the lines inside one row sit
    /// 0.98 to 1.44 times the text size apart — a line height, give or take — and
    /// consecutive rows sit 1.68 to 3.5 times it apart. Nothing observed falls between,
    /// and a page with a single spacing on it is one line per row, which is what this
    /// gives.</summary>
    private const double LineAndAHalf = 1.55;

    /// <summary>How much bigger the gap between rows has to be than the gap inside one
    /// before the two are believed to be different things. A page of single-line rows
    /// splits into two heaps whose averages are within a few percent of each other;
    /// a page with wrapped cells does not come close to this.</summary>
    private const double Separated = 1.35;

    /// <summary>The smallest either heap may be before the split is disbelieved, as a
    /// count and as a share. A page whose rows are all one line has no small heap, and
    /// the largest gap on it — the last row before the footer, say — is otherwise split
    /// off on its own and every row on the page swallows the next.</summary>
    private const int Fewest = 3;
    private const double LeastShare = 0.2;

    /// <summary>The threshold between two heaps of measurements, or null when there is
    /// only one heap and nothing to split.
    ///
    /// Asked twice, of two different things: the gaps between lines, where the heaps
    /// are "inside a wrapped row" and "between rows", and the gaps between words on a
    /// heading, where they are "a space inside a heading" and "the edge of a column".
    /// Both are the same question, and neither has an answer that can be written down
    /// in advance — one real export packs its headings under two spaces apart.</summary>
    internal static double? TwoHeaps(IReadOnlyList<double> values)
    {
        if (values.Count < Fewest * 2) return null;

        var sorted = values.OrderBy(v => v).ToList();
        var total = sorted.Sum();

        double best = 0, belowSum = 0;
        double? at = null;
        for (var i = 0; i < sorted.Count - 1; i++)
        {
            belowSum += sorted[i];
            if (sorted[i + 1] - sorted[i] <= 0) continue;

            var below = i + 1;
            var above = sorted.Count - below;
            var meanBelow = belowSum / below;
            var meanAbove = (total - belowSum) / above;

            // Between-class variance: the split that separates the heaps best.
            var spread = below * (double)above * Math.Pow(meanBelow - meanAbove, 2);
            if (spread <= best) continue;

            // The strongest split wins, and then has to be worth believing — not the
            // strongest believable one. If the clearest division in the data is one
            // that cannot be trusted, the data is not cleanly in two heaps and no
            // division should be trusted: two wrapped rows among forty is not evidence
            // of anything, and the fallback handles it far better than the next-best
            // split, which would put the line between two kinds of row gap.
            best = spread;
            at = Believable(below, above, sorted.Count) && meanAbove / meanBelow >= Separated
                ? (sorted[i] + sorted[i + 1]) / 2
                : null;
        }

        return at;
    }

    private static bool Believable(int below, int above, int total) =>
        below >= Fewest && above >= Fewest
        && below >= total * LeastShare && above >= total * LeastShare;

    private static IReadOnlyList<double> Gaps(IReadOnlyList<TextLine> lines)
    {
        var gaps = new List<double>();
        for (var i = 1; i < lines.Count; i++)
        {
            var gap = lines[i - 1].Baseline - lines[i].Baseline;
            if (gap > 0.1) gaps.Add(gap);
        }
        return gaps;
    }

    private static double MedianTextSize(IReadOnlyList<TextLine> lines)
    {
        var sizes = lines.SelectMany(l => l.Letters)
            .Select(l => l.PointSize)
            .Where(s => s > 0)
            .OrderBy(s => s)
            .ToList();
        return sizes.Count == 0 ? 12 : sizes[sizes.Count / 2];
    }

    /// <summary>Groups lines into table rows on the vertical gap between them.</summary>
    public static IReadOnlyList<IReadOnlyList<TextLine>> GroupIntoRows(IReadOnlyList<TextLine> lines, double threshold)
    {
        var rows = new List<IReadOnlyList<TextLine>>();
        if (lines.Count == 0) return rows;

        var current = new List<TextLine> { lines[0] };
        for (var i = 1; i < lines.Count; i++)
        {
            if (lines[i - 1].Baseline - lines[i].Baseline > threshold)
            {
                rows.Add(current);
                current = [];
            }
            current.Add(lines[i]);
        }
        rows.Add(current);
        return rows;
    }
}
