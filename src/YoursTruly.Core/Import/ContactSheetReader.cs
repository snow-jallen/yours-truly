using System.Security.Cryptography;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace YoursTruly.Core.Import;

/// <summary>Reads a PDF of people into rows and columns.
///
/// The app used to know three reports by name and refuse everything else. It now reads
/// any table whose heading row names two of the things a list of people is built
/// around, and, failing that, hunts the page for e-mail addresses and phone numbers.
/// What the columns mean is not decided here — that is the user's answer on the import
/// screen, and this class is careful to hand over what the file said rather than what
/// it might have meant.
///
/// Why this takes geometry at all: a PDF of a table contains no rules, no cell
/// boundaries and no reading order that matches the table. Pulling the text out
/// linearly interleaves the columns into nonsense. See docs/architecture.md.</summary>
public static partial class ContactSheetReader
{
    public static ContactSheet Read(string path)
    {
        using var stream = File.OpenRead(path);
        return Read(stream, Path.GetFileName(path));
    }

    public static ContactSheet Read(Stream stream, string fileName)
    {
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var bytes = buffer.ToArray();
        var sha = Convert.ToHexStringLower(SHA256.HashData(bytes));

        using var document = PdfDocument.Open(bytes);
        var pageCount = document.NumberOfPages;

        var pages = new List<IReadOnlyList<TextLine>>();
        for (var number = 1; number <= pageCount; number++)
            pages.Add(PdfTableReader.ReadLines(document.GetPage(number)));

        // The rhythm of the rows is a fact about the document, not about one page of
        // it. Worked out across all of them, because a last page holding two rows has
        // too few gaps on it to tell a wrapped cell from a new row.
        var rowBreak = PdfTableReader.RowBreak([.. pages.SelectMany(p => p)]);

        // A table if it has a heading row; a printed directory if it is blocks of
        // people in bands; and failing both, every line searched for an address and a
        // number. In that order, because each knows more than the one after it.
        var sheet = ReadTable(pages, rowBreak, pageCount, fileName, sha)
                 ?? ReadRecords(pages, pageCount, fileName, sha)
                 ?? ReadLoosely(pages, rowBreak, pageCount, fileName, sha);

        return sheet ?? throw new ImportException(
            $"Yours Truly could not find any people in '{fileName}'. It looks for a table with a heading " +
            "row naming at least two of name, email and phone — or, failing that, for email addresses " +
            "and phone numbers anywhere on the page. A scanned picture of a list has no text in it at " +
            "all, so it cannot be read this way.");
    }

    // --- a table with headings ------------------------------------------------------

    private static ContactSheet? ReadTable(
        IReadOnlyList<IReadOnlyList<TextLine>> pages, double rowBreak,
        int pageCount, string fileName, string sha)
    {
        HeadingRow? first = null;
        var firstPage = 0;
        for (var i = 0; i < pages.Count && first is null; i++)
        {
            first = HeadingOn(pages[i], rowBreak);
            firstPage = i;
        }
        if (first is null) return null;

        var rows = new List<SheetRow>();
        for (var i = firstPage; i < pages.Count; i++)
        {
            // A page after the first with no heading of its own is a continuation and
            // keeps every line; one with its own heading starts below it.
            var own = i == firstPage ? first : HeadingOn(pages[i], rowBreak);
            var heading = own ?? first;
            var floor = own is null ? double.PositiveInfinity : own.Baseline;

            var body = pages[i]
                .Where(l => l.Baseline < floor)
                .Where(l => !IsFurniture(l, heading))
                .ToList();

            foreach (var group in PdfTableReader.GroupIntoRows(body, rowBreak))
            {
                var cells = Enumerable.Range(0, heading.Layout.Count)
                    .Select(c => heading.Layout.Cell(group, c))
                    .ToList();
                var row = new SheetRow(cells, i + 1);
                if (!row.IsEmpty && row.Text.Any(char.IsLetterOrDigit)) rows.Add(row);
            }
        }

        return new ContactSheet(first.Headings, Stitch(rows, NameColumn(first.Headings)), pageCount, fileName, sha);
    }

    private static HeadingRow? HeadingOn(IReadOnlyList<TextLine> lines, double rowBreak)
    {
        foreach (var group in PdfTableReader.GroupIntoRows(lines, rowBreak))
            if (Headings.Detect(group) is { } heading)
                return heading;
        return null;
    }

    /// <summary>A line belonging to the page rather than to a person.
    ///
    /// The strong rule is position: every cell of the first column is left-aligned on
    /// that column, so a line starting to the left of it is not in the table. That is
    /// what a report's footer looks like — a date at the very edge of the page, a
    /// copyright notice, a page number — and it catches them without anybody having to
    /// list the wording, which changes with the report and with the year.</summary>
    private static bool IsFurniture(TextLine line, HeadingRow heading)
    {
        var words = line.Words();
        if (words.Count == 0) return true;
        if (words[0].X < heading.Layout.Left - 1.5) return true;

        var text = line.Text;
        return PageNumber().IsMatch(text) || RowCount().IsMatch(text) || BareLink().IsMatch(text);
    }

    [GeneratedRegex(@"^page\s+\d+(\s+of\s+\d+)?$", RegexOptions.IgnoreCase)]
    private static partial Regex PageNumber();

    [GeneratedRegex(@"^(count|total|records|rows)\s*:?\s*\d+$", RegexOptions.IgnoreCase)]
    private static partial Regex RowCount();

    [GeneratedRegex(@"^\S*://\S*$|^www\.\S+$")]
    private static partial Regex BareLink();

    /// <summary>Re-joins a person split across two row groups. A long name can push a
    /// row's wrapped lines far enough apart to read as a break, leaving a fragment
    /// behind that belongs to the row above it.
    ///
    /// A fragment is a row with nothing in the first column. Where the file writes
    /// names "Ashgrove, Adelaide" — which is checked rather than assumed, because half
    /// the files in the world write "Adelaide Ashgrove" — a first column with no comma
    /// in it is a fragment too.</summary>
    private static IReadOnlyList<SheetRow> Stitch(IReadOnlyList<SheetRow> rows, int nameColumn)
    {
        var commaStyle = CommaNames(rows, nameColumn);
        var stitched = new List<SheetRow>();

        foreach (var row in rows)
        {
            var name = row.Cell(nameColumn);
            var fragment = name.Length == 0
                        || (commaStyle && !name.Contains(',', StringComparison.Ordinal));

            if (fragment && stitched.Count > 0)
            {
                var previous = stitched[^1];
                stitched[^1] = previous with
                {
                    Cells = [.. previous.Cells.Select((c, i) => Merge(c, row.Cell(i)))],
                };
                continue;
            }
            stitched.Add(row);
        }

        return commaStyle
            ? [.. stitched.Where(r => r.Cell(nameColumn).Contains(',', StringComparison.Ordinal))]
            : [.. stitched.Where(r => r.Cell(nameColumn).Length > 0)];
    }

    /// <summary>True when this file writes names surname-first. Decided from the file
    /// rather than assumed, and only when there is enough of it to decide from.</summary>
    internal static bool CommaNames(IReadOnlyList<SheetRow> rows, int nameColumn = 0)
    {
        var named = rows.Select(r => r.Cell(nameColumn)).Where(n => n.Length > 0).ToList();
        if (named.Count == 0) return false;
        return named.Count(n => n.Contains(',', StringComparison.Ordinal)) * 5 >= named.Count * 3;
    }

    /// <summary>Which column holds the name, as far as the headings can say. Stitching
    /// has to know before the user has said anything, because a row that wrapped is
    /// only recognisable by its name cell being empty.</summary>
    private static int NameColumn(IReadOnlyList<string> headings)
    {
        for (var i = 0; i < headings.Count; i++)
            if (FieldGuess.For(headings[i]) is ImportField.Name or ImportField.LastName) return i;
        return 0;
    }

    private static string Merge(string a, string b) =>
        a.Length == 0 ? b : b.Length == 0 ? a : $"{a} {b}";

    // --- a printed directory rather than a table --------------------------------------

    private static ContactSheet? ReadRecords(
        IReadOnlyList<IReadOnlyList<TextLine>> pages, int pageCount, string fileName, string sha)
    {
        var rows = RecordBlockReader.Read(pages);
        return rows is null
            ? null
            : new ContactSheet(["Name", "Email", "Phone"], rows, pageCount, fileName, sha)
            {
                Shape = SheetShape.Records,
            };
    }

    // --- no headings: hunt for addresses --------------------------------------------

    /// <summary>The last resort, and the one that makes "anything with names, e-mails
    /// and phone numbers" true: no heading row, so every line is searched for an
    /// e-mail address and a phone number, and whatever is left of it is the name.
    ///
    /// Only rows carrying a way of reaching somebody are kept. Without a heading there
    /// is nothing to say a line of prose is not a person, and a page of prose would
    /// otherwise import as a hundred people with no contact details.</summary>
    private static ContactSheet? ReadLoosely(
        IReadOnlyList<IReadOnlyList<TextLine>> pages, double rowBreak,
        int pageCount, string fileName, string sha)
    {
        var rows = new List<SheetRow>();

        for (var i = 0; i < pages.Count; i++)
            foreach (var group in PdfTableReader.GroupIntoRows(pages[i], rowBreak))
            {
                var text = TextLine.Collapse(string.Join(' ', group
                    .OrderByDescending(l => l.Baseline)
                    .Select(l => l.Text)));
                if (text.Length == 0) continue;

                var email = EmailAnywhere().Match(text);
                var phone = PhoneAnywhere().Match(text);
                if (!email.Success && !phone.Success) continue;

                var name = TextLine.Collapse(Leftover(text, email, phone));
                if (name.Length == 0 || !name.Any(char.IsLetter)) continue;

                rows.Add(new SheetRow([name, email.Value, phone.Value.Trim()], i + 1));
            }

        return rows.Count == 0
            ? null
            : new ContactSheet(["Name", "Email", "Phone"], rows, pageCount, fileName, sha)
            {
                Shape = SheetShape.Loose,
            };
    }

    private static string Leftover(string text, Match email, Match phone)
    {
        var keep = new System.Text.StringBuilder();
        for (var i = 0; i < text.Length; i++)
        {
            if (email.Success && i >= email.Index && i < email.Index + email.Length) continue;
            if (phone.Success && i >= phone.Index && i < phone.Index + phone.Length) continue;
            keep.Append(text[i]);
        }
        return keep.ToString().Trim(' ', ',', ';', '|', '-', '\t');
    }

    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}")]
    private static partial Regex EmailAnywhere();

    /// <summary>Ten or eleven digits, however they have been punctuated, with an
    /// optional country code. Deliberately narrow: a loose pattern turns every date and
    /// every house number into a phone number.</summary>
    [GeneratedRegex(@"(?<![\d-])(\+?1[ .\-]?)?\(?\d{3}\)?[ .\-]\d{3}[ .\-]\d{4}(?![\d-])")]
    private static partial Regex PhoneAnywhere();
}
