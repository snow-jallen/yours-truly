namespace YoursTruly.Core.Import;

/// <summary>What one column of a file is being imported as.</summary>
public sealed record ColumnMapping(int Column, string Heading, ImportField Field);

/// <summary>What every column of a file is being imported as, and whether that makes
/// sense. the app proposes one from the headings; the user has the last word on the
/// import screen, and nothing is written until they do.</summary>
public sealed record ImportMapping(IReadOnlyList<ColumnMapping> Columns)
{
    /// <summary>The obvious reading of a file's headings, which is right often enough
    /// that most imports are a glance and a button.
    ///
    /// Where nothing reads as a name, the first unrecognised column is taken for one.
    /// A roster headed "Player" or "Scout" or "Child" names its people in a word nobody
    /// can list in advance, and it is nearly always the first column — so guess, show
    /// the guess, and let it be corrected, rather than refuse the file and make the
    /// user work out what the app wanted.</summary>
    public static ImportMapping Guess(ContactSheet sheet)
    {
        var columns = sheet.Columns
            .Select((h, i) => new ColumnMapping(i, h, FieldGuess.Default(h)))
            .ToList();

        var named = columns.Any(c =>
            c.Field is ImportField.Name or ImportField.FirstName or ImportField.LastName);
        if (!named)
        {
            var first = columns.FirstOrDefault(c => FieldGuess.For(c.Heading) is null);
            if (first is not null) columns[first.Column] = first with { Field = ImportField.Name };
        }

        return new ImportMapping(columns);
    }

    public ImportMapping With(int column, ImportField field) =>
        new([.. Columns.Select(c => c.Column == column ? c with { Field = field } : c)]);

    public int? ColumnFor(ImportField field) =>
        Columns.FirstOrDefault(c => c.Field == field)?.Column;

    public IReadOnlyList<int> GroupColumns =>
        [.. Columns.Where(c => c.Field is ImportField.Groups).Select(c => c.Column)];

    public bool Has(ImportField field) => Columns.Any(c => c.Field == field);

    /// <summary>A field the file is importing, and therefore one an import from it may
    /// overwrite. A file with no e-mail column has said nothing about anyone's e-mail
    /// address and must not blank the ones already recorded.</summary>
    public bool Carries(ImportField field) => Has(field);

    /// <summary>What is wrong with this mapping, in the words to put on the screen, or
    /// null when it is ready to import.</summary>
    public string? Problem
    {
        get
        {
            if (!Has(ImportField.Name) && !Has(ImportField.LastName) && !Has(ImportField.FirstName))
                return "Say which column holds people's names. Nothing can be imported without one.";

            if (Has(ImportField.Name) && (Has(ImportField.FirstName) || Has(ImportField.LastName)))
                return "One column is set to Name and another to a part of the name. Use either the "
                     + "whole name in one column, or a first name and a surname in two.";

            foreach (var once in ImportFields.All.Where(f => f.OnlyOnce()))
            {
                var claimed = Columns.Where(c => c.Field == once).ToList();
                if (claimed.Count > 1)
                    return $"Two columns are both set to {once.Label()} — "
                         + $"“{claimed[0].Heading}” and “{claimed[1].Heading}”. Only one can be.";
            }

            if (!Has(ImportField.Email) && !Has(ImportField.Phone))
                return "Say which column holds email addresses or phone numbers. Without one there "
                     + "is no way to reach anybody in this file.";

            return null;
        }
    }

    public bool IsReady => Problem is null;
}
