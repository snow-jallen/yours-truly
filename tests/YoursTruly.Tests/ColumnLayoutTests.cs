using YoursTruly.Core.Import;

namespace YoursTruly.Tests;

public sealed class ColumnLayoutTests
{
    // Column positions from a real two-page export, which are the ones that break a
    // layout built from a fixed field order: phone and e-mail are the other way round
    // from the other report, and there is a Gender column nothing wants.
    private const int Name = 0, Gender = 1, Age = 2, Birthday = 3, Phone = 4, Email = 5, Unit = 6;

    private static ColumnLayout Layout() =>
        ColumnLayout.From([54.1, 152.4, 215.6, 240.6, 282.6, 343.2, 480.3])!;

    [Fact]
    public void Reads_each_field_from_its_own_column()
    {
        var lines = PdfLines.Of(8.0,
            (55.6, 600, "Ashdown, Marigold"), (153.9, 600, "F"), (242.1, 600, "17 Jan"),
            (284.1, 600, "(435) 555-0100"), (344.7, 600, "m.ashdown@example.com"),
            (481.8, 600, "Manti 2nd Ward"));
        var layout = Layout();

        Assert.Equal("Ashdown, Marigold", layout.Cell(lines, Name));
        Assert.Equal("17 Jan", layout.Cell(lines, Birthday));
        Assert.Equal("(435) 555-0100", layout.Cell(lines, Phone));
        Assert.Equal("m.ashdown@example.com", layout.Cell(lines, Email));
        Assert.Equal("Manti 2nd Ward", layout.Cell(lines, Unit));
    }

    [Fact]
    public void A_column_nothing_wants_still_has_to_have_an_edge()
    {
        // Gender is found only so that its edge exists. Without it the "F" joins the
        // name and every person reads "Ashdown, Marigold F".
        var lines = PdfLines.Of(8.0, (55.6, 600, "Ashdown, Marigold"), (153.9, 600, "F"));
        Assert.Equal("Ashdown, Marigold", Layout().Cell(lines, Name));
        Assert.Equal("F", Layout().Cell(lines, Gender));
    }

    [Fact]
    public void Leaves_a_field_empty_rather_than_borrowing_the_next_column()
    {
        var lines = PdfLines.Of(8.0,
            (55.6, 600, "Brackenbury, Tavish"), (153.9, 600, "M"), (242.1, 600, "3 Jul"),
            (481.8, 600, "Manti 6th Ward"));
        var layout = Layout();

        Assert.Equal("", layout.Cell(lines, Phone));
        Assert.Equal("", layout.Cell(lines, Email));
        Assert.Equal("Manti 6th Ward", layout.Cell(lines, Unit));
    }

    [Fact]
    public void Rejoins_a_cell_that_wraps_over_several_lines()
    {
        // A wrapped name sits 4.5pt either side of the row, so its two lines are
        // separate TextLines that both belong to the Name column.
        var lines = PdfLines.Of(8.0,
            (55.6, 604.5, "Featherstonehaugh,"), (153.9, 600, "F"), (55.6, 595.5, "Wilhelmina"));
        Assert.Equal("Featherstonehaugh, Wilhelmina", Layout().Cell(lines, Name));
    }

    [Fact]
    public void Rejoins_a_wrapped_email_with_nothing_between_the_halves()
    {
        // Recognised by what the halves make when joined with nothing, rather than by
        // the column having been declared an e-mail column somewhere. A space here
        // looks fine on screen right up until the send fails.
        var lines = PdfLines.Of(9.0,
            (344.7, 606, "k.yelverton@averylongdomain"), (344.7, 594, "name.example"));
        Assert.Equal("k.yelverton@averylongdomainname.example", Layout().Cell(lines, Email));
    }

    [Fact]
    public void Rejoins_a_wrapped_phone_number_the_same_way()
    {
        var lines = PdfLines.Of(9.0, (284.1, 606, "(435) 555-"), (284.1, 594, "0100"));
        Assert.Equal("(435) 555-0100", Layout().Cell(lines, Phone));
    }

    [Fact]
    public void Rejoins_wrapped_words_with_a_space_between_them()
    {
        // "812 North 700" and "East" are two words of one value, not two halves of one
        // word — so this one does get its space back.
        var lines = PdfLines.Of(9.0, (481.8, 606, "812 North 700"), (481.8, 594, "East"));
        Assert.Equal("812 North 700 East", Layout().Cell(lines, Unit));
    }

    [Fact]
    public void Answers_for_a_column_the_layout_does_not_have()
    {
        var layout = Layout();
        Assert.Equal(7, layout.Count);
        Assert.Equal("", layout.Cell(PdfLines.Of(8.0, (55.6, 600, "Ashdown, Marigold")), 9));
    }

    [Fact]
    public void Refuses_columns_that_do_not_run_left_to_right()
    {
        // A heading matched somewhere it does not belong shows up as an out-of-order
        // position. Rejecting the layout is what stops four fields folding into one.
        Assert.Null(ColumnLayout.From([225.6, 152.4, 480.3]));
    }

    [Fact]
    public void Refuses_an_empty_layout() => Assert.Null(ColumnLayout.From([]));
}
