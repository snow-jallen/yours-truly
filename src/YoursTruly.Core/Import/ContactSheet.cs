namespace YoursTruly.Core.Import;

/// <summary>One row of a file, exactly as it was printed, before anything has decided
/// what any of it means. Kept verbatim so an import can always be traced back to what
/// the file said.</summary>
public sealed record SheetRow(IReadOnlyList<string> Cells, int Page)
{
    public string Cell(int column) => column >= 0 && column < Cells.Count ? Cells[column] : "";

    public bool IsEmpty => Cells.All(c => c.Length == 0);

    public string Text => string.Join(' ', Cells.Where(c => c.Length > 0));
}

/// <summary>A file of people, read but not yet understood: the columns it prints and
/// the rows under them. What each column means is the user's answer, given on the
/// import screen — see <see cref="ImportMapping"/>.</summary>
public sealed record ContactSheet(
    IReadOnlyList<string> Columns,
    IReadOnlyList<SheetRow> Rows,
    int PageCount,
    string FileName,
    string Sha256)
{
    /// <summary>How the file had to be read. Worth saying on screen: only a table
    /// names its own columns, and in the other two shapes the columns are the app's
    /// invention rather than the file's.</summary>
    public SheetShape Shape { get; init; } = SheetShape.Table;

    public bool HeadingsFound => Shape is SheetShape.Table;

    /// <summary>The first few values in a column, for showing the user what they are
    /// deciding about. Blanks are left out — a column's first three rows being empty
    /// says nothing about the column.</summary>
    public IReadOnlyList<string> Sample(int column, int howMany = 3) =>
        [.. Rows.Select(r => r.Cell(column)).Where(v => v.Length > 0).Distinct().Take(howMany)];
}

/// <summary>What a file turned out to be.</summary>
public enum SheetShape
{
    /// <summary>A table whose own heading row names its columns.</summary>
    Table,

    /// <summary>A printed directory: blocks of people laid out in bands down the page,
    /// with no table anywhere in it.</summary>
    Records,

    /// <summary>Neither, so every line was searched for an address and a number.</summary>
    Loose,
}

/// <summary>A file that could not be read as a list of people.</summary>
public sealed class ImportException(string message) : Exception(message);
