using YoursTruly.Core.Import;

namespace YoursTruly.Tests;

/// <summary>A two-page export with a title and a heading above the table on page one,
/// a name wrapped over two lines, an e-mail wrapped over two, and a birth date printed
/// with its year on a couple of rows and without it on the rest.</summary>
public sealed class MemberListParserTests
{
    private static ContactSheet Sheet() =>
        SheetHelp.Read(SyntheticMemberList.Build(), "member-list.pdf");

    [Fact]
    public void Reads_the_columns_off_the_files_own_heading_row()
    {
        var sheet = Sheet();
        Assert.True(sheet.HeadingsFound);
        Assert.Equal(2, sheet.PageCount);
        Assert.Equal(["Name", "Gender", "Age", "Birth Date", "Phone Number", "Email"], sheet.Columns);
    }

    [Fact]
    public void Reads_one_row_per_person_across_both_pages()
    {
        var sheet = Sheet();
        Assert.Equal(7, sheet.Rows.Count);
        Assert.Equal(
            ["Ashdown, Marigold", "Quilley, Barnaby", "Crowther, Dell", "Winslade, Verity",
             "Featherstonehaugh, Wilhelmina", "Wraithwell, Mordecai", "Yelverton, Katriona"],
            sheet.Rows.Select(r => r.Cell(sheet, "Name")));
    }

    [Fact]
    public void Leaves_out_the_title_and_the_heading_printed_above_the_table()
    {
        var sheet = Sheet();
        Assert.DoesNotContain(sheet.Rows, r => r.Text.Contains("Member List", StringComparison.Ordinal));
        Assert.DoesNotContain(sheet.Rows, r => r.Text.Contains("12564", StringComparison.Ordinal));
    }

    [Fact]
    public void Leaves_out_the_footer_that_starts_left_of_the_first_column()
    {
        // The copyright line and the date beside it start outside the table. Nothing in
        // a table does, which is what makes that a rule rather than a list of wordings.
        var sheet = Sheet();
        Assert.DoesNotContain(sheet.Rows, r => r.Text.Contains("Church Use Only", StringComparison.Ordinal));
        Assert.DoesNotContain(sheet.Rows, r => r.Text.Contains("Count:", StringComparison.Ordinal));
    }

    [Fact]
    public void Keeps_each_field_in_its_own_column()
    {
        var sheet = Sheet();
        var row = sheet.Rows[0];
        Assert.Equal("Ashdown, Marigold", row.Cell(sheet, "Name"));
        Assert.Equal("17 Jan", row.Cell(sheet, "Birth Date"));
        Assert.Equal("(435) 555-0100", row.Cell(sheet, "Phone"));
        Assert.Equal("m.ashdown@example.com", row.Cell(sheet, "Email"));
    }

    [Fact]
    public void Rejoins_a_name_wrapped_over_two_lines()
    {
        var sheet = Sheet();
        var row = sheet.Rows[4];
        Assert.Equal("Featherstonehaugh, Wilhelmina", row.Cell(sheet, "Name"));
        Assert.Equal("20 Aug", row.Cell(sheet, "Birth Date"));
        Assert.Equal("(801) 555-0155", row.Cell(sheet, "Phone"));
    }

    [Fact]
    public void Rejoins_a_wrapped_e_mail_with_nothing_between_the_halves()
    {
        // A space here would make the address undeliverable, and it would look fine on
        // screen right up until the send failed. Recognised by what the halves make
        // when they are joined with nothing, which is a fact about the value rather
        // than about the file it came out of.
        var sheet = Sheet();
        Assert.Equal("k.yelverton@averylongdomainname.example", sheet.Rows[6].Cell(sheet, "Email"));
    }

    [Fact]
    public void Leaves_a_missing_field_empty_rather_than_borrowing_the_next_column()
    {
        var sheet = Sheet();
        Assert.Equal("", sheet.Rows[2].Cell(sheet, "Email"));
        Assert.Equal("555-0133", sheet.Rows[2].Cell(sheet, "Phone"));
        Assert.Equal("", sheet.Rows[3].Cell(sheet, "Phone"));
        Assert.Equal("v.winslade@example.com", sheet.Rows[3].Cell(sheet, "Email"));
    }

    [Fact]
    public void Reads_the_full_birth_date_the_file_prints_on_some_rows()
    {
        var sheet = Sheet();
        var row = sheet.Rows[1];
        Assert.Equal("9 Feb 1982", row.Cell(sheet, "Birth Date"));
        Assert.Equal("44", row.Cell(sheet, "Age"));

        var (month, day) = Normalizer.ParseBirthday(row.Cell(sheet, "Birth Date"));
        Assert.Equal(2, month);
        Assert.Equal(9, day);
    }

    [Fact]
    public void A_column_filled_on_two_rows_of_seven_is_the_users_call()
    {
        // This Age column is real but nearly always empty, so importing it would blank
        // the ages already on record to honour the two rows that have one. That used to
        // be a judgment written into the code, per report. It is now a drop-down on the
        // import screen, which is the right place for a judgment about one file.
        var sheet = Sheet();
        Assert.Contains(sheet.Rows, r => r.Cell(sheet, "Age").Length > 0);

        var mapping = ImportMapping.Guess(sheet);
        Assert.True(mapping.Carries(ImportField.Age));

        var without = mapping.With(sheet.Column("Age"), ImportField.Ignore);
        Assert.False(without.Carries(ImportField.Age));
        Assert.True(without.IsReady);
    }
}
