using YoursTruly.Core.Import;

namespace YoursTruly.Tests;

/// <summary>Reading a fixture the way the app does, and getting at a column by the
/// heading the file printed rather than by counting commas in the test.</summary>
internal static class SheetHelp
{
    public static ContactSheet Read(byte[] pdf, string fileName) =>
        ContactSheetReader.Read(new MemoryStream(pdf), fileName);

    public static string Cell(this SheetRow row, ContactSheet sheet, string heading) =>
        row.Cell(sheet.Column(heading));

    public static int Column(this ContactSheet sheet, string heading)
    {
        for (var i = 0; i < sheet.Columns.Count; i++)
            if (Simplify(sheet.Columns[i]).Contains(Simplify(heading), StringComparison.Ordinal)) return i;
        throw new InvalidOperationException(
            $"No column matching '{heading}'. This file has: {string.Join(" | ", sheet.Columns)}");
    }

    /// <summary>Punctuation-blind, so a test can ask for "Email" and find the column
    /// the file calls "Individual E-mail".</summary>
    private static string Simplify(string heading) =>
        new(heading.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    public static ImportField GuessFor(this ContactSheet sheet, string heading) =>
        ImportMapping.Guess(sheet).Columns[sheet.Column(heading)].Field;

    public static IReadOnlyList<NormalizedPerson> People(this ContactSheet sheet) =>
        Normalizer.Normalize(sheet, ImportMapping.Guess(sheet));
}
