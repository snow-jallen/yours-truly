namespace YoursTruly.Core.Import;

/// <summary>The columns a file prints, read off its own heading row.
///
/// The app used to be told the headings to expect, one set per report it had been
/// taught. It is told nothing now: any line that names two of the things a list of
/// people is built around — a name, an e-mail address, a phone number — is taken as the
/// heading row, and every other phrase on it becomes a column too. That is what lets a
/// soccer roster and a class list work as well as the report this started with; a
/// column nothing recognises is offered to the user as a group rather than refused.</summary>
public sealed record HeadingRow(IReadOnlyList<string> Headings, ColumnLayout Layout, double Baseline)
{
    /// <summary>How many of this row's headings name something a list of people is
    /// built around. Two is the bar for believing it is the heading row at all.</summary>
    public int NamesPeople => Headings.Count(FieldGuess.NamesAPerson);
}

public static class Headings
{
    /// <summary>Below this many of a file's own field names on one row, it is some
    /// other table — a summary, a key, a smaller table printed above the people.</summary>
    private const int Needed = 2;

    /// <summary>A horizontal gap wider than this fraction of the text size is the edge
    /// of a column rather than a space inside a heading. A space is about a quarter of
    /// the point size; the gap between two columns is several times it.</summary>
    private const double ColumnGap = 0.6;

    /// <summary>Two phrases starting within this fraction of the text size of each other
    /// are the same column, stacked. "Preferred" over "Name" is one heading, printed on
    /// two lines because the column is narrow.</summary>
    private const double SameColumn = 0.5;

    /// <summary>The heading row this group of lines is, or null for any other group —
    /// which is every group on a body page.</summary>
    public static HeadingRow? Detect(IReadOnlyList<TextLine> group)
    {
        if (group.Count == 0) return null;

        var size = group.Select(l => l.TextSize).Where(s => s > 0).DefaultIfEmpty(12).Max();
        var tolerance = Math.Max(2.0, size * SameColumn);

        // Phrases from every line of the group, top line first, so a column printed on
        // two lines reads in the order it was written.
        var columns = new List<(double X, List<string> Parts)>();
        foreach (var line in group.OrderByDescending(l => l.Baseline))
            foreach (var phrase in Phrases(line, size))
            {
                var existing = columns.FirstOrDefault(c => Math.Abs(c.X - phrase.X) <= tolerance);
                if (existing.Parts is not null) existing.Parts.Add(phrase.Text);
                else columns.Add((phrase.X, [phrase.Text]));
            }

        if (columns.Count < Needed) return null;

        var ordered = columns.OrderBy(c => c.X).ToList();
        var headings = ordered.Select(c => string.Join(' ', c.Parts)).ToList();
        var layout = ColumnLayout.From([.. ordered.Select(c => c.X)]);
        if (layout is null) return null;

        var row = new HeadingRow(headings, layout, group.Min(l => l.Baseline));
        return row.NamesPeople >= Needed ? row : null;
    }

    /// <summary>The headings on one line: words joined up while they are only a space
    /// apart, so "Phone Number" is one column and not two.
    ///
    /// A phrase carrying an @ or made mostly of digits is not a heading. Without that,
    /// a body row holding somebody called <c>name@example.com</c> reads as a line that
    /// names a person, and the first row of data is mistaken for the heading.</summary>
    private static IReadOnlyList<Word> Phrases(TextLine line, double size)
    {
        var gap = size * ColumnGap;
        var joined = new List<Word>();

        foreach (var word in line.Words())
        {
            if (joined.Count > 0 && word.X - joined[^1].End <= gap)
                joined[^1] = new Word(joined[^1].X, word.End, $"{joined[^1].Text} {word.Text}");
            else
                joined.Add(word);
        }

        return [.. joined.Where(CouldBeAHeading)];
    }

    private static bool CouldBeAHeading(Word word) =>
        !word.Text.Contains('@', StringComparison.Ordinal)
        && word.Text.Count(char.IsDigit) <= 2
        && word.Text.Any(char.IsLetter);
}
