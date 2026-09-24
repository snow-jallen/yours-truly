using YoursTruly.Core.Import;

namespace YoursTruly.Tests;

/// <summary>A two-page export with a smaller table printed above the one that matters,
/// a heading repeated on the second page, a column found only so its edge exists, and a
/// name wrapped over two lines. Every one of those used to need the app to have been
/// taught this particular report; none of them does now.</summary>
public sealed class CallingsReportParserTests
{
    private static ContactSheet Sheet() =>
        SheetHelp.Read(SyntheticCallingsReport.Build(), "print.pdf");

    [Fact]
    public void Reads_the_columns_off_the_files_own_heading_row()
    {
        var sheet = Sheet();
        Assert.True(sheet.HeadingsFound);
        Assert.Equal(2, sheet.PageCount);
        Assert.Equal(
            ["Name", "Gender", "Age", "Birth Date", "Phone Number", "Email", "Current Unit"],
            sheet.Columns);
    }

    [Fact]
    public void Reads_one_row_per_person_across_both_pages()
    {
        // The file's own footer says Count: 7. Reading exactly that many back is the
        // strongest check available that no row was dropped or invented.
        var sheet = Sheet();
        Assert.Equal(7, sheet.Rows.Count);
        Assert.Equal(
            ["Ashdown, Marigold", "Quilley, Barnaby", "Crowther, Dell",
             "Featherstonehaugh, Wilhelmina", "Zelmore, Briony",
             "Wraithwell, Mordecai", "Yelverton, Katriona"],
            sheet.Rows.Select(r => r.Cell(sheet, "Name")));
    }

    [Fact]
    public void Leaves_out_the_smaller_table_printed_above_the_people()
    {
        // "Pilkington, Hattie" is in a table with different columns and no contact
        // details. Its heading names one of the things a list of people is built
        // around and no more, which is below the bar for being taken as a heading row.
        var sheet = Sheet();
        Assert.DoesNotContain(sheet.Rows,
            r => r.Text.Contains("Pilkington", StringComparison.Ordinal));
    }

    [Fact]
    public void Keeps_each_field_in_its_own_column()
    {
        var sheet = Sheet();
        var row = sheet.Rows[0];
        Assert.Equal("Ashdown, Marigold", row.Cell(sheet, "Name"));   // the Gender glyph does not join it
        Assert.Equal("17 Jan", row.Cell(sheet, "Birth Date"));
        Assert.Equal("(435) 555-0100", row.Cell(sheet, "Phone"));
        Assert.Equal("m.ashdown@example.com", row.Cell(sheet, "Email"));
        Assert.Equal("Manti 2nd Ward", row.Cell(sheet, "Current Unit"));
    }

    [Fact]
    public void A_gender_column_is_read_and_then_ignored()
    {
        // It has to be found or the "F" joins the name. Nothing stores it.
        var sheet = Sheet();
        Assert.Equal("F", sheet.Rows[0].Cell(sheet, "Gender"));
        Assert.Equal(ImportField.Ignore, sheet.GuessFor("Gender"));
    }

    [Fact]
    public void Rejoins_a_name_wrapped_over_two_lines()
    {
        var sheet = Sheet();
        var row = sheet.Rows[3];
        Assert.Equal("Featherstonehaugh, Wilhelmina", row.Cell(sheet, "Name"));
        Assert.Equal("3 Jul", row.Cell(sheet, "Birth Date"));
        Assert.Equal("Manti 10th Ward", row.Cell(sheet, "Current Unit"));
    }

    [Fact]
    public void Leaves_a_missing_field_empty_rather_than_borrowing_the_next_column()
    {
        var sheet = Sheet();
        Assert.Equal("", sheet.Rows[1].Cell(sheet, "Email"));
        Assert.Equal("555-0127", sheet.Rows[1].Cell(sheet, "Phone"));
        Assert.Equal("", sheet.Rows[2].Cell(sheet, "Phone"));
        Assert.Equal("d.crowther@example.com", sheet.Rows[2].Cell(sheet, "Email"));
    }

    [Fact]
    public void Normalises_into_people_the_rest_of_the_app_can_use()
    {
        var people = Sheet().People();

        Assert.All(people, p => Assert.NotNull(p.BirthMonth));
        Assert.All(people, p => Assert.Single(p.Groups));
        Assert.All(people.Where(p => p.PhoneE164 is not null),
            p => Assert.Matches(@"^\+1\d{10}$", p.PhoneE164!));

        // The Age column is printed on every row and filled on none of them.
        Assert.All(people, p => Assert.Null(p.Age));
    }
}
