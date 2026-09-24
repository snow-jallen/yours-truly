using YoursTruly.Core.Import;

namespace YoursTruly.Tests;

/// <summary>Files nobody taught the app to read. This is the point of the whole
/// reader: a soccer roster, a class list, a phone tree — anything with names and a way
/// of reaching people.</summary>
public sealed class RosterImportTests
{
    private static ContactSheet Table() => SheetHelp.Read(SyntheticRoster.Table(), "roster.pdf");

    [Fact]
    public void Finds_a_table_whose_headings_nobody_listed_in_advance()
    {
        var sheet = Table();
        Assert.True(sheet.HeadingsFound);
        Assert.Equal(["Player", "Parent Email", "Cell", "Team"], sheet.Columns);
        Assert.Equal(3, sheet.Rows.Count);
    }

    [Fact]
    public void Tells_one_row_from_the_next_on_a_page_with_nothing_wrapped_on_it()
    {
        // The hard case for any rule about vertical spacing: every gap on the page is
        // the same, so there is no small heap of within-row gaps to find. One line per
        // row is the right answer, and the wrong answer swallows the whole table into
        // a single person.
        Assert.Equal(3, Table().Rows.Count);
    }

    [Fact]
    public void Takes_the_first_unrecognised_column_for_the_name()
    {
        // "Player" is not a word anybody can list in advance, and it is where the names
        // are. Guess it, show the guess, and let it be corrected — which beats refusing
        // the file and leaving the user to work out what the app wanted.
        var sheet = Table();
        var mapping = ImportMapping.Guess(sheet);

        Assert.Equal(ImportField.Name, mapping.Columns[sheet.Column("Player")].Field);
        Assert.Equal(ImportField.Email, mapping.Columns[sheet.Column("Parent Email")].Field);
        Assert.Equal(ImportField.Phone, mapping.Columns[sheet.Column("Cell")].Field);
        Assert.Equal(ImportField.Groups, mapping.Columns[sheet.Column("Team")].Field);
        Assert.Null(mapping.Problem);
    }

    [Fact]
    public void Splits_a_name_written_the_way_most_of_the_world_writes_it()
    {
        var people = Table().People();
        Assert.Equal("Ashgrove", people[0].LastName);
        Assert.Equal("Adelaide", people[0].FirstName);
        Assert.Equal("Adelaide Ashgrove", people[0].DisplayName);
    }

    [Fact]
    public void A_column_of_teams_becomes_a_group_each()
    {
        var people = Table().People();
        Assert.Equal(["U12 Blue"], people[0].Groups);
        Assert.Equal(["U12 Blue"], people[1].Groups);
        Assert.Equal(["U14 Red"], people[2].Groups);
    }

    [Theory]
    [InlineData("Ward")]
    [InlineData("Unit")]
    [InlineData("Team")]
    [InlineData("Class")]
    [InlineData("Year")]
    [InlineData("Route")]
    public void Any_column_nothing_recognises_is_offered_as_groups(string heading)
    {
        // Including the ones this app used to have a field for. A Ward column is not a
        // special case any more — it is a column of values, each of which becomes a
        // group, exactly like a Team column or a Class column. Nothing has to be taught
        // the word, and nothing has to be taught the next word either.
        Assert.Null(FieldGuess.For(heading));
        Assert.Equal(ImportField.Groups, FieldGuess.Default(heading));
    }

    [Fact]
    public void Every_row_lands_in_the_group_its_own_cell_names()
    {
        var sheet = new ContactSheet(
            ["Name", "Email", "Ward"],
            [
                new SheetRow(["Ashgrove, Adelaide", "a@example.com", "Manti 2nd Ward"], 1),
                new SheetRow(["Quilley, Barnaby", "b@example.com", "Manti 2nd Ward"], 1),
                new SheetRow(["Winslade, Verity", "v@example.com", "Sterling Ward"], 1),
                new SheetRow(["Peacock, Alvin", "p@example.com", ""], 1),
            ],
            1, "directory.pdf", new string('a', 64));

        var mapping = ImportMapping.Guess(sheet);
        Assert.Equal(ImportField.Groups, mapping.Columns[2].Field);

        var people = Normalizer.Normalize(sheet, mapping);
        Assert.Equal(["Manti 2nd Ward"], people[0].Groups);
        Assert.Equal(["Manti 2nd Ward"], people[1].Groups);
        Assert.Equal(["Sterling Ward"], people[2].Groups);

        // A blank cell puts somebody in no group rather than in a group called nothing.
        Assert.Empty(people[3].Groups);

        // And the plan says which groups the file would make, before it makes them.
        var plan = ImportPlanner.Plan(people, [], mapping);
        Assert.Equal(["Manti 2nd Ward", "Sterling Ward"], plan.Groups);
    }

    [Fact]
    public void A_file_can_put_people_into_two_kinds_of_group_at_once()
    {
        // A team and a year group are two things worth gathering people by, and the
        // import screen lets both columns be groups.
        var sheet = new ContactSheet(
            ["Name", "Email", "Team", "Year"],
            [new SheetRow(["Ashgrove, Adelaide", "a@example.com", "U12 Blue", "Year 7"], 1)],
            1, "roster.pdf", new string('a', 64));

        var people = Normalizer.Normalize(sheet, ImportMapping.Guess(sheet));
        Assert.Equal(["U12 Blue", "Year 7"], Assert.Single(people).Groups);
    }

    [Fact]
    public void Reads_a_list_with_no_table_in_it_at_all()
    {
        var sheet = SheetHelp.Read(SyntheticRoster.Loose(), "phone-tree.pdf");

        // Said out loud, because these columns are the app's invention and not the
        // file's, and that changes how much they should be trusted.
        Assert.False(sheet.HeadingsFound);
        Assert.Equal(["Name", "Email", "Phone"], sheet.Columns);

        var people = sheet.People();
        Assert.Equal(["Ashgrove", "Quilley", "Winslade"], people.Select(p => p.LastName));
        Assert.Equal("a.ashgrove@example.com", people[0].Email);
        Assert.Equal("+14355550101", people[0].PhoneE164);
        Assert.Equal("+14355550102", people[1].PhoneE164);
    }

    [Fact]
    public void A_line_of_prose_with_no_way_of_reaching_anybody_is_not_a_person()
    {
        // Without a heading row there is nothing to say a sentence is not a name, so
        // only lines carrying an address or a number are kept.
        var sheet = SheetHelp.Read(SyntheticRoster.Loose(), "phone-tree.pdf");
        Assert.DoesNotContain(sheet.Rows, r => r.Text.Contains("keep this list", StringComparison.Ordinal));
        Assert.DoesNotContain(sheet.Rows, r => r.Text.Contains("phone tree", StringComparison.Ordinal));
    }
}
