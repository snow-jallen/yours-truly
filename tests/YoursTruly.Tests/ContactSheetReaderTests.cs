using YoursTruly.Core.Import;

namespace YoursTruly.Tests;

/// <summary>Reading a table out of a PDF, on a fixture laid out the way a real
/// directory export is: cells centred vertically on their row rather than aligned to
/// its top, headings wrapped over three lines, and page furniture to step over.</summary>
public sealed class ContactSheetReaderTests
{
    private static ContactSheet Sheet() => SheetHelp.Read(SyntheticReport.Build(), "synthetic.pdf");

    [Fact]
    public void Reads_one_row_per_person_and_no_page_furniture()
    {
        var sheet = Sheet();
        Assert.Equal(3, sheet.Rows.Count);
        Assert.Equal(
            ["Ashby, Miriam", "Quilley, Barnaby", "Crowther, Dell"],
            sheet.Rows.Select(r => r.Cell(sheet, "Name")));
    }

    [Fact]
    public void Reads_the_columns_off_the_files_own_heading_row()
    {
        // Nothing here was taught to the app. The headings wrap over three lines and
        // are stacked back together by where they sit, not by a list of expected words.
        var sheet = Sheet();
        Assert.True(sheet.HeadingsFound);
        Assert.Equal(
            ["Preferred Name", "Individual E-mail", "Individual Phone", "Unit", "Age",
             "Birthday (1 Jan)", "Address - Street 1"],
            sheet.Columns);
    }

    [Fact]
    public void Guesses_what_each_column_is_for()
    {
        var sheet = Sheet();
        Assert.Equal(ImportField.Name, sheet.GuessFor("Preferred Name"));
        Assert.Equal(ImportField.Email, sheet.GuessFor("Individual E-mail"));
        Assert.Equal(ImportField.Phone, sheet.GuessFor("Individual Phone"));
        Assert.Equal(ImportField.Age, sheet.GuessFor("Age"));
        Assert.Equal(ImportField.Birthday, sheet.GuessFor("Birthday"));

        // A column nothing recognises is offered as a group rather than refused, which
        // is how a Unit column, a Team column and a Class column all become useful.
        Assert.Equal(ImportField.Groups, sheet.GuessFor("Unit"));

        // And an address is read and thrown away. the app posts nothing.
        Assert.Equal(ImportField.Ignore, sheet.GuessFor("Address"));
    }

    [Fact]
    public void Rejoins_a_cell_that_wraps_across_three_lines()
    {
        // "Manti", "2nd" and "Ward" are on three different baselines, one of which is
        // shared with the e-mail and two of which are shared with nothing else.
        var sheet = Sheet();
        Assert.Equal("Manti 2nd Ward", sheet.Rows[0].Cell(sheet, "Unit"));
    }

    [Fact]
    public void Keeps_each_field_in_its_own_column()
    {
        var sheet = Sheet();
        var row = sheet.Rows[0];
        Assert.Equal("m.ashby@example.com", row.Cell(sheet, "E-mail"));
        Assert.Equal("(435) 555-0111", row.Cell(sheet, "Phone"));
        Assert.Equal("86", row.Cell(sheet, "Age"));
        Assert.Equal("17 Jan", row.Cell(sheet, "Birthday"));
        Assert.Equal("812 North 700 East", row.Cell(sheet, "Address"));
    }

    [Fact]
    public void Leaves_a_missing_field_empty_rather_than_borrowing_the_next_column()
    {
        var sheet = Sheet();
        var row = sheet.Rows[2];
        Assert.Equal("", row.Cell(sheet, "E-mail"));
        Assert.Equal("555-0133", row.Cell(sheet, "Phone"));
        Assert.Equal("Manti 4th Ward", row.Cell(sheet, "Unit"));
        Assert.Equal("", row.Cell(sheet, "Address"));
    }

    [Fact]
    public void An_unrecognised_column_becomes_a_group_everyone_in_it_joins()
    {
        var people = Sheet().People();
        Assert.Equal(["Manti 2nd Ward"], people[0].Groups);
        Assert.Equal(["Sterling Ward"], people[1].Groups);
    }

    [Fact]
    public void Reads_a_row_laid_out_at_a_real_exports_own_geometry()
    {
        // Contrast with the fixture above: this one uses a real export's own absolute
        // numbers (8.9pt text, 12.8pt wraps, 23.2pt row gap) rather than values merely
        // chosen to contrast with each other. The gap at which one row ends and the
        // next begins is worked out from the page, and this is what says it works on
        // spacing nobody picked to be easy.
        var sheet = SheetHelp.Read(SyntheticReport.BuildAtRealGeometry(), "real-geometry.pdf");

        var row = Assert.Single(sheet.Rows);
        Assert.Equal("Ashby, Miriam", row.Cell(sheet, "Name"));
        Assert.Equal("m.ashby@example.com", row.Cell(sheet, "E-mail"));
        Assert.Equal("(435) 555-0111", row.Cell(sheet, "Phone"));
        Assert.Equal("40", row.Cell(sheet, "Age"));
        Assert.Equal("6 May", row.Cell(sheet, "Birthday"));
    }

    [Fact]
    public void Refuses_a_pdf_with_nobody_in_it_and_says_what_it_looked_for()
    {
        var error = Assert.Throws<ImportException>(
            () => SheetHelp.Read(NothingButAHeadline(), "holiday-photos.pdf"));

        Assert.Contains("holiday-photos.pdf", error.Message, StringComparison.Ordinal);
        Assert.Contains("email", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("phone", error.Message, StringComparison.OrdinalIgnoreCase);

        // A scanned picture of a list is the failure most worth naming: it looks like
        // exactly the right file and has no text in it whatsoever.
        Assert.Contains("scanned", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] NothingButAHeadline()
    {
        var builder = new UglyToad.PdfPig.Writer.PdfDocumentBuilder();
        var page = builder.AddPage(612, 792);
        var font = builder.AddStandard14Font(
            UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica);
        page.AddText("Christmas Party", 12, new UglyToad.PdfPig.Core.PdfPoint(72, 700), font);
        return builder.Build();
    }
}
