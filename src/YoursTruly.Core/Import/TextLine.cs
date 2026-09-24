using System.Text;
using UglyToad.PdfPig.Content;

namespace YoursTruly.Core.Import;

/// <summary>One word and the horizontal span it occupies, in PDF points.</summary>
public sealed record Word(double X, double End, string Text);

/// <summary>One run of glyphs sharing a text baseline, ordered left to right.</summary>
public sealed class TextLine
{
    /// <summary>Widest horizontal gap, in points, that still counts as the same word.
    /// Measured between advance boxes, so glyphs inside a word are touching.</summary>
    private const double WordGap = 1.2;

    /// <summary>Gap wide enough that a space belongs between two glyphs. A backstop:
    /// the report writes real space characters, which are kept and emitted as-is.</summary>
    private const double SpaceGap = 1.2;

    public double Baseline { get; }
    public IReadOnlyList<Letter> Letters { get; }

    public TextLine(double baseline, IEnumerable<Letter> letters)
    {
        Baseline = baseline;
        Letters = letters.OrderBy(l => l.StartBaseLine.X).ToList();
    }

    public string Text => Cell(double.NegativeInfinity, double.PositiveInfinity);

    /// <summary>The text of this line falling within a column's horizontal span.
    /// A glyph belongs to the column its left edge starts in.</summary>
    public string Cell(double left, double right)
    {
        var sb = new StringBuilder();
        double? previousRight = null;
        foreach (var l in Letters)
        {
            var x = l.StartBaseLine.X;
            if (x < left || x >= right) continue;
            if (previousRight is { } p && x - p > SpaceGap) sb.Append(' ');
            sb.Append(l.Value);
            previousRight = l.EndBaseLine.X;
        }
        return Collapse(sb.ToString());
    }

    /// <summary>Whitespace-separated words with the span each one occupies, used to
    /// find the file's column headings and to tell a space inside a heading apart from
    /// the gap to the next column.</summary>
    public IReadOnlyList<Word> Words()
    {
        var words = new List<Word>();
        var sb = new StringBuilder();
        double start = 0, end = 0;
        double? previousRight = null;

        void Flush()
        {
            if (sb.Length > 0) words.Add(new Word(start, end, sb.ToString()));
            sb.Clear();
        }

        foreach (var l in Letters)
        {
            var x = l.StartBaseLine.X;
            var blank = string.IsNullOrWhiteSpace(l.Value);
            if (blank || (previousRight is { } p && x - p > WordGap)) Flush();
            if (!blank)
            {
                if (sb.Length == 0) start = x;
                sb.Append(l.Value);
                end = l.EndBaseLine.X;
            }
            previousRight = l.EndBaseLine.X;
        }
        Flush();
        return words;
    }

    /// <summary>The words of this line joined up while they are less than
    /// <paramref name="maxGap"/> apart, so a phrase comes back as one run and the thing
    /// across the page from it comes back as another.</summary>
    public IReadOnlyList<Word> Runs(double maxGap)
    {
        var joined = new List<Word>();
        foreach (var word in Words())
            if (joined.Count > 0 && word.X - joined[^1].End <= maxGap)
                joined[^1] = new Word(joined[^1].X, word.End, $"{joined[^1].Text} {word.Text}");
            else
                joined.Add(word);
        return joined;
    }

    /// <summary>The typical size of the text on this line, used to judge whether a
    /// horizontal gap is a space or the edge of a column.</summary>
    public double TextSize
    {
        get
        {
            var sizes = Letters.Select(l => l.PointSize).Where(s => s > 0).OrderBy(s => s).ToList();
            return sizes.Count == 0 ? 0 : sizes[sizes.Count / 2];
        }
    }

    internal static string Collapse(string s) =>
        string.Join(' ', s.Split((char[])[' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries));
}
