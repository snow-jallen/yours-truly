using System.Text.RegularExpressions;

namespace YoursTruly.Core.Import;

/// <summary>Where each column of a table starts, in PDF points, read from the file's
/// own heading row rather than hard-coded, so a re-styled export does not silently
/// shift every field by one column.
///
/// Columns arrive left to right. A layout whose positions do not run strictly left to
/// right is rejected: it means a heading was matched somewhere it does not belong.</summary>
public sealed partial class ColumnLayout
{
    /// <summary>Cells are left-aligned on these x positions, but a glyph can start a
    /// fraction of a point to the left of its column. Without this slack the opening
    /// bracket of "(435) 555-0100" lands in the column before the phone.</summary>
    private const double Slack = 1.5;

    private readonly IReadOnlyList<double> _x;

    private ColumnLayout(IReadOnlyList<double> x) => _x = x;

    public int Count => _x.Count;

    /// <summary>Where the first column starts. Everything in the table is at or right
    /// of it, so anything printed to the left belongs to the page.</summary>
    public double Left => _x[0];

    /// <summary>Null when there are no columns, or when their positions do not
    /// increase left to right.</summary>
    public static ColumnLayout? From(IReadOnlyList<double> positions)
    {
        if (positions.Count == 0) return null;
        for (var i = 1; i < positions.Count; i++)
            if (positions[i] <= positions[i - 1]) return null;
        return new ColumnLayout(positions);
    }

    /// <summary>One column's text, gathered from every line of a row group, so a cell
    /// that wraps over several lines comes back as one string.</summary>
    public string Cell(IReadOnlyList<TextLine> group, int column)
    {
        if (column < 0 || column >= _x.Count) return "";

        var left = _x[column] - Slack;
        var right = column + 1 < _x.Count ? _x[column + 1] - Slack : double.PositiveInfinity;

        var parts = new List<string>();
        foreach (var line in group)
        {
            var text = line.Cell(left, right);
            if (text.Length > 0) parts.Add(text);
        }

        return Rejoin(parts);
    }

    /// <summary>Puts the lines a cell wrapped over back together.
    ///
    /// Words are rejoined with a space: "812 North 700" and "East" are two words of one
    /// address. An e-mail address is not words — a long one wrapped mid-address becomes
    /// unsendable if a space goes back between the halves — and neither is a phone
    /// number split after its area code. Both are recognised by what the halves make
    /// when they are joined with nothing, which is a fact about the value rather than
    /// about the file it came out of.</summary>
    private static string Rejoin(IReadOnlyList<string> parts)
    {
        if (parts.Count <= 1) return parts.Count == 0 ? "" : parts[0];

        var tight = string.Concat(parts);
        if (EmailShaped().IsMatch(tight)) return tight;
        if (PhoneShaped().IsMatch(tight) && !parts.Any(p => p.Any(char.IsLetter))) return tight;

        return string.Join(' ', parts);
    }

    [GeneratedRegex(@"^\S+@\S+\.\S+$")]
    private static partial Regex EmailShaped();

    [GeneratedRegex(@"^[-+()\d. ]{7,20}$")]
    private static partial Regex PhoneShaped();
}
