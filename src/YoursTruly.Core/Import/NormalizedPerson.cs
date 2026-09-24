using System.Globalization;
using System.Text.RegularExpressions;

namespace YoursTruly.Core.Import;

/// <summary>A row of a file cleaned into the shapes the database stores.</summary>
public sealed record NormalizedPerson(
    string LastName,
    string FirstName,
    string DisplayName,
    int? Age,
    int? BirthMonth,
    int? BirthDay,
    string? Email,
    string? PhoneRaw,
    string? PhoneE164,
    bool AreaCodeAssumed,
    IReadOnlyList<string> Groups)
{
    public string SortName => $"{LastName}, {FirstName}";

    /// <summary>Whether the file gave this person a name at all. A row without one
    /// cannot be imported — there is nothing to match them on and nothing to address a
    /// message to — but it must be counted rather than quietly dropped.</summary>
    public bool IsNamed => LastName.Length > 0 || FirstName.Length > 0;

    public bool CanBeReached => Email is not null || PhoneRaw is not null;
}

/// <summary>Turns what a file printed into what the app stores: a name split in two, a
/// birthday without its year, a number a service can dial.</summary>
public static partial class Normalizer
{
    /// <summary>Supplied when a file prints a seven-digit number. Those are flagged so
    /// the app can ask rather than quietly dial the wrong state.</summary>
    public const string DefaultAreaCode = "435";

    public static IReadOnlyList<NormalizedPerson> Normalize(
        ContactSheet sheet, ImportMapping mapping, string defaultAreaCode = DefaultAreaCode)
    {
        return [.. Everybody(sheet, mapping, defaultAreaCode).Where(p => p.IsNamed)];
    }

    /// <summary>How many people the file can reach but cannot name.
    ///
    /// These are dropped by <see cref="Normalize"/>, and the count is what stops that
    /// being a silent loss. A reader that has misread the page usually still finds the
    /// email addresses — they are unmistakable — and fails on the names, which have no
    /// syntax at all. So this number is the honest measure of a bad read, and the
    /// import screen says it out loud instead of showing a green button over half a
    /// list.</summary>
    public static int Unnamed(
        ContactSheet sheet, ImportMapping mapping, string defaultAreaCode = DefaultAreaCode) =>
        Everybody(sheet, mapping, defaultAreaCode).Count(p => !p.IsNamed && p.CanBeReached);

    private static IEnumerable<NormalizedPerson> Everybody(
        ContactSheet sheet, ImportMapping mapping, string defaultAreaCode)
    {
        // "Ashgrove, Adelaide" or "Adelaide Ashgrove" — decided from the file rather
        // than assumed, and only where the whole name is in one column.
        var nameColumn = mapping.ColumnFor(ImportField.Name);
        var surnameFirst = nameColumn is int c && ContactSheetReader.CommaNames(sheet.Rows, c);

        return sheet.Rows.Select(row => Normalize(row, mapping, surnameFirst, defaultAreaCode));
    }

    private static NormalizedPerson Normalize(
        SheetRow row, ImportMapping mapping, bool surnameFirst, string defaultAreaCode)
    {
        var (last, first, display) = ReadName(row, mapping, surnameFirst);
        var (month, day) = ParseBirthday(Cell(row, mapping, ImportField.Birthday));
        var (raw, e164, assumed) = ParsePhone(Cell(row, mapping, ImportField.Phone), defaultAreaCode);

        return new NormalizedPerson(
            LastName: last,
            FirstName: first,
            DisplayName: display,
            Age: int.TryParse(Cell(row, mapping, ImportField.Age), NumberStyles.None,
                              CultureInfo.InvariantCulture, out var age) ? age : null,
            BirthMonth: month,
            BirthDay: day,
            Email: Blank(Cell(row, mapping, ImportField.Email)),
            PhoneRaw: raw,
            PhoneE164: e164,
            AreaCodeAssumed: assumed,
            Groups: [.. mapping.GroupColumns
                .Select(c => YoursTruly.Core.Domain.GroupName.Clean(row.Cell(c)))
                .OfType<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)]);
    }

    /// <summary>The surname, the given name, and the name as the file printed it.
    /// Two columns are used when the file has them; otherwise the one column is split,
    /// at the comma where there is one and at the last space where there is not.</summary>
    private static (string Last, string First, string Display) ReadName(
        SheetRow row, ImportMapping mapping, bool surnameFirst)
    {
        var separate = Blank(Cell(row, mapping, ImportField.LastName));
        var given = Blank(Cell(row, mapping, ImportField.FirstName));
        if (separate is not null || given is not null)
        {
            var last = separate ?? "";
            var first = given ?? "";
            return (last, first, first.Length > 0 && last.Length > 0 ? $"{last}, {first}" : last + first);
        }

        var whole = Cell(row, mapping, ImportField.Name).Trim();
        if (whole.Contains(',', StringComparison.Ordinal))
        {
            var parts = whole.Split(',', 2, StringSplitOptions.TrimEntries);
            return (parts[0], parts.Length > 1 ? parts[1] : "", whole);
        }

        // No comma. Where the rest of the file writes surname first, one name on its
        // own is a surname; otherwise the last word is.
        var words = whole.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length <= 1) return (whole, "", whole);
        return surnameFirst
            ? (words[0], string.Join(' ', words.Skip(1)), whole)
            : (words[^1], string.Join(' ', words.Take(words.Length - 1)), whole);
    }

    private static string Cell(SheetRow row, ImportMapping mapping, ImportField field) =>
        mapping.ColumnFor(field) is int column ? row.Cell(column) : "";

    /// <summary>Files usually print a day and a month and no year, and the app stores
    /// only those two. A full date is read and its year thrown away, because a birthday
    /// that failed to parse would be stored as no birthday at all — and on a file that
    /// carries birthdays, that clears one already on record.</summary>
    public static (int? Month, int? Day) ParseBirthday(string? value)
    {
        var text = (value ?? "").Trim();
        if (text.Length == 0) return (null, null);

        var named = NamedMonth().Match(text);
        if (named.Success)
        {
            var month = Array.IndexOf(Months, named.Groups[2].Value[..3].ToLowerInvariant()) + 1;
            var day = int.Parse(named.Groups[1].Value, CultureInfo.InvariantCulture);
            return month > 0 && day is >= 1 and <= 31 ? (month, day) : (null, null);
        }

        var numeric = NumericDate().Match(text);
        if (numeric.Success)
        {
            var month = int.Parse(numeric.Groups[1].Value, CultureInfo.InvariantCulture);
            var day = int.Parse(numeric.Groups[2].Value, CultureInfo.InvariantCulture);
            return month is >= 1 and <= 12 && day is >= 1 and <= 31 ? (month, day) : (null, null);
        }

        return (null, null);
    }

    private static readonly string[] Months =
        ["jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec"];

    /// <summary>Returns the number as printed, an E.164 form when one can be built,
    /// and whether an area code had to be supplied.</summary>
    public static (string? Raw, string? E164, bool AreaCodeAssumed) ParsePhone(
        string? value, string defaultAreaCode = DefaultAreaCode)
    {
        var raw = Blank(value);
        if (raw is null) return (null, null, false);

        var digits = new string(raw.Where(char.IsAsciiDigit).ToArray());
        return digits.Length switch
        {
            10 => (raw, $"+1{digits}", false),
            11 when digits[0] == '1' => (raw, $"+{digits}", false),
            7 => (raw, $"+1{defaultAreaCode}{digits}", true),
            _ => (raw, null, false),
        };
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    [GeneratedRegex(@"^\s*(\d{1,2})\s+([A-Za-z]{3,9})\.?(?:\s+\d{2,4})?\s*$")]
    private static partial Regex NamedMonth();

    /// <summary>Month first, as the United States writes it. Ambiguous with the rest of
    /// the world and unavoidable: 03/04 is two guesses and this makes the local one.</summary>
    [GeneratedRegex(@"^\s*(\d{1,2})[/\-.](\d{1,2})(?:[/\-.]\d{2,4})?\s*$")]
    private static partial Regex NumericDate();
}
