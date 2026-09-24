using YoursTruly.Core.Import;

namespace YoursTruly.Tests;

/// <summary>Finding the heading row, which is the whole of how the app now knows what
/// a file is. It used to be told: three reports, each with its column words written
/// down. It is told nothing, and a line naming two of the things a list of people is
/// built around is taken as the heading.</summary>
public sealed class HeadingDetectionTests
{
    /// <summary>A heading wrapped across three lines exactly as one real export wraps
    /// it, at the x positions that export uses.</summary>
    private static IReadOnlyList<TextLine> WrappedHeading() => PdfLines.Of(8.9,
        (41, 622, "Preferred"), (279, 622, "Individual"), (446, 622, "Birthday"), (515, 622, "Address -"),
        (114, 615, "Individual E-mail"), (355, 615, "Unit"), (402, 615, "Age"),
        (41, 608, "Name"), (279, 608, "Phone"), (446, 608, "(1 Jan)"), (515, 608, "Street 1"));

    private static IReadOnlyList<TextLine> OneLineHeading() => PdfLines.Of(8.0,
        (54.1, 600, "Name"), (152.4, 600, "Gender"), (215.6, 600, "Age"),
        (240.6, 600, "Birth Date"), (282.6, 600, "Phone Number"), (343.2, 600, "Email"),
        (480.3, 600, "Current Unit"));

    [Fact]
    public void Stacks_a_heading_wrapped_over_three_lines_back_into_columns()
    {
        var heading = Headings.Detect(WrappedHeading());

        Assert.NotNull(heading);
        Assert.Equal(
            ["Preferred Name", "Individual E-mail", "Individual Phone", "Unit", "Age",
             "Birthday (1 Jan)", "Address - Street 1"],
            heading.Headings);
    }

    [Fact]
    public void Joins_the_words_of_one_heading_and_not_the_gap_to_the_next()
    {
        // "Phone Number" is one column and "Birth Date" is another, told apart by how
        // far the words sit from each other relative to the size of the text.
        var heading = Headings.Detect(OneLineHeading());
        Assert.NotNull(heading);
        Assert.Equal(7, heading.Headings.Count);
        Assert.Contains("Phone Number", heading.Headings);
        Assert.Contains("Birth Date", heading.Headings);
    }

    [Fact]
    public void A_line_naming_only_one_of_them_is_not_a_heading_row()
    {
        // The table of callings printed above the people carries a Name of its own and
        // nothing else the app recognises. Two is the bar, and this is one.
        var callings = PdfLines.Of(8.0,
            (35.1, 600, "Calling"), (225.6, 600, "Name"), (321.7, 600, "Sustained"),
            (408.5, 600, "Set Apart"), (483.5, 600, "Current Unit"));

        Assert.Null(Headings.Detect(callings));
    }

    [Fact]
    public void A_toolbar_is_not_a_heading_row()
    {
        var toolbar = PdfLines.Of(8.9, (41, 700, "Group by Unit Edit Report"));
        Assert.Null(Headings.Detect(toolbar));
    }

    [Fact]
    public void A_row_of_real_people_is_not_mistaken_for_the_heading()
    {
        // Somebody called name@example.com would otherwise read as a line naming a
        // person, and the first row of data would be taken for the heading row.
        var data = PdfLines.Of(9.0,
            (41.8, 600, "Ashdown, Marigold"), (153.9, 600, "name@example.com"),
            (346.0, 600, "(435) 555-0100"));

        Assert.Null(Headings.Detect(data));
    }

    [Theory]
    [InlineData("Name", ImportField.Name)]
    [InlineData("Preferred Name", ImportField.Name)]
    [InlineData("Player", null)]
    [InlineData("First Name", ImportField.FirstName)]
    [InlineData("Surname", ImportField.LastName)]
    [InlineData("E-mail", ImportField.Email)]
    [InlineData("Email Address", ImportField.Email)]
    [InlineData("Parent Email", ImportField.Email)]
    [InlineData("Phone Number", ImportField.Phone)]
    [InlineData("Cell", ImportField.Phone)]
    [InlineData("Mobile", ImportField.Phone)]
    [InlineData("DOB", ImportField.Birthday)]
    [InlineData("Birth Date", ImportField.Birthday)]
    [InlineData("Age", ImportField.Age)]
    [InlineData("Home Address", ImportField.Ignore)]
    [InlineData("Gender", ImportField.Ignore)]
    public void A_heading_is_matched_on_whole_words(string heading, ImportField? expected) =>
        Assert.Equal(expected, FieldGuess.For(heading));

    [Theory]
    [InlineData("Manager")]
    [InlineData("Village")]
    public void A_short_word_inside_a_longer_one_does_not_count(string heading)
    {
        // "age" is in "manager" and in "village", and neither of those is an age.
        Assert.Null(FieldGuess.For(heading));
        Assert.Equal(ImportField.Groups, FieldGuess.Default(heading));
    }

    [Fact]
    public void A_heading_nothing_recognises_is_offered_as_a_group()
    {
        // Refusing it would throw away the one column that makes a roster useful.
        Assert.Equal(ImportField.Groups, FieldGuess.Default("Team"));
        Assert.Equal(ImportField.Groups, FieldGuess.Default("Current Unit"));
    }
}

public sealed class ImportMappingTests
{
    private static ContactSheet Sheet(params string[] columns) =>
        new(columns, [new SheetRow([.. columns.Select(_ => "x")], 1)], 1, "f.pdf", new string('a', 64));

    private static ImportMapping Of(params (string Heading, ImportField Field)[] columns) =>
        new([.. columns.Select((c, i) => new ColumnMapping(i, c.Heading, c.Field))]);

    [Fact]
    public void A_mapping_with_a_name_and_a_way_of_reaching_people_is_ready()
    {
        var mapping = Of(("Name", ImportField.Name), ("Email", ImportField.Email));
        Assert.Null(mapping.Problem);
        Assert.True(mapping.IsReady);
    }

    [Fact]
    public void A_mapping_with_no_name_column_says_so_in_words()
    {
        var mapping = Of(("Email", ImportField.Email), ("Phone", ImportField.Phone));
        Assert.Contains("names", mapping.Problem, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_mapping_with_no_way_of_reaching_anybody_says_so_too()
    {
        var mapping = Of(("Name", ImportField.Name), ("Team", ImportField.Groups));
        Assert.Contains("email", mapping.Problem, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Two_columns_cannot_both_be_the_phone_number()
    {
        var mapping = Of(
            ("Name", ImportField.Name), ("Home", ImportField.Phone), ("Mobile", ImportField.Phone));
        Assert.Contains("Two columns", mapping.Problem, StringComparison.Ordinal);
        Assert.Contains("Mobile", mapping.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_columns_of_groups_are_perfectly_sensible()
    {
        // A team and a year group are two things worth gathering people by.
        var mapping = Of(
            ("Name", ImportField.Name), ("Email", ImportField.Email),
            ("Team", ImportField.Groups), ("Year", ImportField.Groups));

        Assert.Null(mapping.Problem);
        Assert.Equal([2, 3], mapping.GroupColumns);
    }

    [Fact]
    public void A_whole_name_and_half_a_name_cannot_both_be_mapped()
    {
        var mapping = Of(
            ("Name", ImportField.Name), ("Surname", ImportField.LastName), ("Email", ImportField.Email));
        Assert.Contains("whole name", mapping.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public void Changing_one_column_leaves_the_others_alone()
    {
        var mapping = ImportMapping.Guess(Sheet("Name", "Email", "Age"));
        var without = mapping.With(2, ImportField.Ignore);

        Assert.Equal(ImportField.Name, without.Columns[0].Field);
        Assert.Equal(ImportField.Email, without.Columns[1].Field);
        Assert.Equal(ImportField.Ignore, without.Columns[2].Field);
        Assert.False(without.Carries(ImportField.Age));
    }
}
